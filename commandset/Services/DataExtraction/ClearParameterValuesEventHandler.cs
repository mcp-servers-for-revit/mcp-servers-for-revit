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
    public class ClearParameterValuesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string ParameterName { get; set; } = "";
        public List<string> Categories { get; set; } = new List<string>();
        public string Scope { get; set; } = "whole_model";
        public string FilterValue { get; set; } = "";
        public string ParameterType { get; set; } = "instance";
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

                var allElements = ParameterType == "type"
                    ? collector.WhereElementIsElementType().ToList()
                    : collector.WhereElementIsNotElementType().ToList();

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

                int cleared = 0;
                int skipped = 0;
                int errors = 0;
                var preview = new List<object>();

                using (var transaction = DryRun ? null : new Transaction(doc, "Clear Parameter Values"))
                {
                    if (!DryRun) transaction.Start();
                    try
                    {
                        foreach (var elem in elements)
                        {
                            Parameter param;
                            if (ParameterType == "type")
                            {
                                var typeElem = doc.GetElement(elem.GetTypeId());
                                param = typeElem?.LookupParameter(ParameterName);
                            }
                            else
                            {
                                param = elem.LookupParameter(ParameterName);
                            }

                            if (param == null || param.IsReadOnly)
                            {
                                skipped++;
                                continue;
                            }

                            string currentValue = param.AsValueString() ?? param.AsString() ?? "";

                            if (!string.IsNullOrEmpty(FilterValue) && !currentValue.Contains(FilterValue))
                            {
                                skipped++;
                                continue;
                            }

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
                                        oldValue = currentValue
                                    });
                                    cleared++;
                                }
                                else
                                {
                                    switch (param.StorageType)
                                    {
                                        case StorageType.String:
                                            param.Set("");
                                            break;
                                        case StorageType.Integer:
                                            param.Set(0);
                                            break;
                                        case StorageType.Double:
                                            param.Set(0.0);
                                            break;
                                        case StorageType.ElementId:
                                            param.Set(ElementId.InvalidElementId);
                                            break;
                                    }
                                    cleared++;
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
                        ? $"Dry run: {cleared} parameters would be cleared, {skipped} skipped, {errors} errors"
                        : $"Cleared {cleared} parameters, {skipped} skipped, {errors} errors",
                    Response = new
                    {
                        parameterName = ParameterName,
                        parameterType = ParameterType,
                        cleared,
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
                Result = new AIResult<object> { Success = false, Message = $"Clear parameter values failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Clear Parameter Values";
    }
}
