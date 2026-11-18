using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services
{
    public class RenameFamiliesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Operation { get; set; } = "prefix";
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";
        public string FindText { get; set; } = "";
        public string ReplaceText { get; set; } = "";
        public List<string> Categories { get; set; } = new List<string>();
        public string Scope { get; set; } = "whole_model";
        public bool RenameTypes { get; set; } = false;
        public bool DryRun { get; set; } = false;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }

        public void SetParameters()
        {
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var uidoc = app.ActiveUIDocument;

                // Collect FamilyInstance elements based on scope
                FilteredElementCollector collector;
                switch (Scope.ToLower())
                {
                    case "active_view":
                        collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
                        break;
                    case "selection":
                        var selectedIds = uidoc.Selection.GetElementIds();
                        if (selectedIds.Count == 0)
                            throw new ArgumentException("No elements selected in Revit");
                        collector = new FilteredElementCollector(doc, selectedIds);
                        break;
                    default:
                        collector = new FilteredElementCollector(doc);
                        break;
                }

                var allInstances = collector
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .ToList();

                // Filter by categories if provided
                var filteredInstances = new List<FamilyInstance>();
                if (Categories.Count > 0)
                {
                    foreach (var inst in allInstances)
                    {
                        foreach (var cat in Categories)
                        {
                            if (CategoryResolver.CategoryMatches(doc, inst, cat))
                            {
                                filteredInstances.Add(inst);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    filteredInstances = allInstances;
                }

                // Get unique Family objects from the instances
                var familyMap = new Dictionary<ElementId, Family>();
                foreach (var inst in filteredInstances)
                {
                    var familySymbol = inst.Symbol;
                    var family = familySymbol.Family;
                    if (!familyMap.ContainsKey(family.Id))
                    {
                        familyMap[family.Id] = family;
                    }
                }

                var familyRenames = new List<object>();
                var typeRenames = new List<object>();
                int familySuccessCount = 0;
                int typeSuccessCount = 0;

                using (var transaction = DryRun ? null : new Transaction(doc, "Rename Families"))
                {
                    if (!DryRun) transaction.Start();

                    try
                    {
                        // Rename families
                        foreach (var kvp in familyMap)
                        {
                            var family = kvp.Value;
                            string oldName = family.Name;
                            string newName = ComputeNewName(oldName);

                            if (newName == oldName) continue;

                            bool success = true;
                            string message = "";

                            if (!DryRun)
                            {
                                try
                                {
                                    family.Name = newName;
                                    familySuccessCount++;
                                }
                                catch (Exception ex)
                                {
                                    success = false;
                                    message = ex.Message;
                                }
                            }
                            else
                            {
                                familySuccessCount++;
                            }

                            familyRenames.Add(new
                            {
#if REVIT2024_OR_GREATER
                                familyId = family.Id.Value,
#else
                                familyId = family.Id.IntegerValue,
#endif
                                oldName,
                                newName,
                                success,
                                message
                            });
                        }

                        // Rename types (FamilySymbol) if requested
                        if (RenameTypes)
                        {
                            var processedSymbols = new HashSet<ElementId>();
                            foreach (var inst in filteredInstances)
                            {
                                var familySymbol = inst.Symbol;
                                if (processedSymbols.Contains(familySymbol.Id)) continue;
                                processedSymbols.Add(familySymbol.Id);

                                string oldName = familySymbol.Name;
                                string newName = ComputeNewName(oldName);

                                if (newName == oldName) continue;

                                bool success = true;
                                string message = "";

                                if (!DryRun)
                                {
                                    try
                                    {
                                        familySymbol.Name = newName;
                                        typeSuccessCount++;
                                    }
                                    catch (Exception ex)
                                    {
                                        success = false;
                                        message = ex.Message;
                                    }
                                }
                                else
                                {
                                    typeSuccessCount++;
                                }

                                typeRenames.Add(new
                                {
#if REVIT2024_OR_GREATER
                                    typeId = familySymbol.Id.Value,
#else
                                    typeId = familySymbol.Id.IntegerValue,
#endif
                                    oldName,
                                    newName,
                                    success,
                                    message
                                });
                            }
                        }

                        if (!DryRun) transaction.Commit();
                    }
                    catch
                    {
                        if (!DryRun && transaction?.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = DryRun
                        ? $"Dry run: {familySuccessCount} families and {typeSuccessCount} types would be renamed"
                        : $"Renamed {familySuccessCount} families and {typeSuccessCount} types",
                    Response = new
                    {
                        operation = Operation,
                        dryRun = DryRun,
                        familiesRenamed = familySuccessCount,
                        typesRenamed = typeSuccessCount,
                        familyRenames,
                        typeRenames = RenameTypes ? typeRenames : null
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Rename families failed: {ex.Message}"
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private string ComputeNewName(string oldName)
        {
            switch (Operation.ToLower())
            {
                case "prefix":
                    if (!string.IsNullOrEmpty(Prefix))
                        return Prefix + oldName;
                    break;
                case "suffix":
                    if (!string.IsNullOrEmpty(Suffix))
                        return oldName + Suffix;
                    break;
                case "find_replace":
                    if (!string.IsNullOrEmpty(FindText))
                        return oldName.Replace(FindText, ReplaceText ?? "");
                    break;
            }
            return oldName;
        }

        public string GetName() => "Rename Families";
    }
}
