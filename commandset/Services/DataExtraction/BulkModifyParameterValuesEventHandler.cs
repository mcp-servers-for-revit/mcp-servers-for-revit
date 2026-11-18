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
    public class BulkModifyParameterValuesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> ElementIds { get; set; } = new List<long>();
        public string CategoryName { get; set; } = "";
        public string ParameterName { get; set; } = "";
        public string Operation { get; set; } = "set"; // set, prefix, suffix, find_replace, clear
        public string Value { get; set; } = "";
        public string FindText { get; set; } = "";
        public string ReplaceText { get; set; } = "";
        public bool OnlyEmpty { get; set; } = false; // only modify empty values
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

                // Get elements
                var elements = new List<Element>();
                if (ElementIds.Count > 0)
                {
                    foreach (var id in ElementIds)
                    {
#if REVIT2024_OR_GREATER
                        var elem = doc.GetElement(new ElementId(id));
#else
                        var elem = doc.GetElement(new ElementId((int)id));
#endif
                        if (elem != null) elements.Add(elem);
                    }
                }
                else if (!string.IsNullOrEmpty(CategoryName))
                {
                    // Find by category
                    var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                    foreach (var elem in collector)
                    {
                        if (CategoryResolver.CategoryMatches(doc, elem, CategoryName))
                            elements.Add(elem);
                    }
                }
                else
                {
                    throw new ArgumentException("Provide either elementIds or categoryName");
                }

                if (string.IsNullOrEmpty(ParameterName))
                    throw new ArgumentException("parameterName is required");

                int modified = 0;
                int skipped = 0;
                int errors = 0;
                var preview = new List<object>();

                using (var transaction = DryRun ? null : new Transaction(doc, "Bulk Modify Parameter Values"))
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
                        string newValue = "";

                        try
                        {
                            switch (Operation.ToLower())
                            {
                                case "set":
                                    if (OnlyEmpty && !string.IsNullOrEmpty(currentValue)) { skipped++; continue; }
                                    newValue = Value;
                                    break;
                                case "prefix":
                                    if (OnlyEmpty && !string.IsNullOrEmpty(currentValue)) { skipped++; continue; }
                                    newValue = Value + currentValue;
                                    break;
                                case "suffix":
                                    if (OnlyEmpty && !string.IsNullOrEmpty(currentValue)) { skipped++; continue; }
                                    newValue = currentValue + Value;
                                    break;
                                case "find_replace":
                                    if (!currentValue.Contains(FindText)) { skipped++; continue; }
                                    newValue = currentValue.Replace(FindText, ReplaceText);
                                    break;
                                case "clear":
                                    newValue = "";
                                    break;
                                default:
                                    throw new ArgumentException($"Unknown operation: {Operation}");
                            }

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
                                    currentValue,
                                    newValue
                                });
                                modified++;
                            }
                            else
                            {
                                if (param.StorageType == StorageType.String)
                                    param.Set(newValue);
                                else if (param.StorageType == StorageType.Integer && int.TryParse(newValue, out int intVal))
                                    param.Set(intVal);
                                else if (param.StorageType == StorageType.Double && double.TryParse(newValue, out double dblVal))
                                    param.Set(dblVal);
                                else
                                    param.Set(newValue); // try as string anyway

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
                        operation = Operation,
                        parameterName = ParameterName,
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
                Result = new AIResult<object> { Success = false, Message = $"Bulk modify failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Bulk Modify Parameter Values";
    }
}
