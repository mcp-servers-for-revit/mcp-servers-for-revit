using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class AddPrefixSuffixEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string ParameterName { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";
        public string Separator { get; set; } = "";
        public List<string> Categories { get; set; } = new List<string>();
        public string Scope { get; set; } = "whole_model";
        public bool SkipEmpty { get; set; } = true;
        public string FilterValue { get; set; } = "";
        public bool DryRun { get; set; } = false;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 30000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var uidoc = app.ActiveUIDocument;

                if (string.IsNullOrEmpty(ParameterName))
                    throw new ArgumentException("parameterName is required");

                if (string.IsNullOrEmpty(Prefix) && string.IsNullOrEmpty(Suffix))
                    throw new ArgumentException("At least one of prefix or suffix must be provided");

                // Collect elements based on scope
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

                var allElements = collector.WhereElementIsNotElementType().ToList();

                // Filter by categories
                var elements = new List<Element>();
                if (Categories.Count > 0)
                {
                    foreach (var elem in allElements)
                    {
                        foreach (var cat in Categories)
                        {
                            if (CategoryResolver.CategoryMatches(doc, elem, cat))
                            {
                                elements.Add(elem);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    elements = allElements;
                }

                int modified = 0;
                int skipped = 0;
                int errors = 0;
                var preview = new List<object>();

                using (var transaction = DryRun ? null : new Transaction(doc, "Add Prefix/Suffix"))
                {
                    if (!DryRun) transaction.Start();
                    try
                    {
                        foreach (var elem in elements)
                        {
                            var param = elem.LookupParameter(ParameterName);
                            if (param == null || param.IsReadOnly)
                            {
                                skipped++;
                                continue;
                            }

                            string currentValue = param.AsValueString() ?? param.AsString() ?? "";

                            if (SkipEmpty && string.IsNullOrEmpty(currentValue))
                            {
                                skipped++;
                                continue;
                            }

                            if (!string.IsNullOrEmpty(FilterValue) && !currentValue.Contains(FilterValue))
                            {
                                skipped++;
                                continue;
                            }

                            // Build new value
                            string newValue = "";
                            if (!string.IsNullOrEmpty(Prefix))
                                newValue = Prefix + Separator + currentValue;
                            else
                                newValue = currentValue;

                            if (!string.IsNullOrEmpty(Suffix))
                                newValue = newValue + Separator + Suffix;

                            try
                            {
                                if (DryRun)
                                {
                                    preview.Add(new
                                    {
#if REVIT2024_OR_GREATER
                                        elementId = elem.Id.Value,
#else
                                        elementId = elem.Id.IntegerValue,
#endif
                                        elementName = elem.Name,
                                        oldValue = currentValue,
                                        newValue
                                    });
                                    modified++;
                                }
                                else
                                {
                                    param.Set(newValue);
                                    modified++;
                                }
                            }
                            catch
                            {
                                errors++;
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
                        ? $"Dry run: {modified} elements would be modified, {skipped} skipped, {errors} errors"
                        : $"Modified {modified} elements, {skipped} skipped, {errors} errors",
                    Response = new
                    {
                        parameterName = ParameterName,
                        prefix = Prefix,
                        suffix = Suffix,
                        separator = Separator,
                        modified,
                        skipped,
                        errors,
                        totalElements = elements.Count,
                        dryRun = DryRun,
                        preview = DryRun ? preview : null
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Add prefix/suffix failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Add Prefix Suffix";
    }
}
