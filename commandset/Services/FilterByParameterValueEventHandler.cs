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
    public class FilterByParameterValueEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<string> Categories { get; set; } = new List<string>();
        public string ParameterName { get; set; } = "";
        public string Condition { get; set; } = "equals";
        public string Value { get; set; } = "";
        public bool CaseSensitive { get; set; } = false;
        public string Scope { get; set; } = "whole_model";
        public string ParameterType { get; set; } = "both";
        public List<string> ReturnParameters { get; set; } = new List<string>();

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
                    default: // whole_model
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

                // Filter by parameter value
                var matchedElements = new List<object>();
                foreach (var elem in elements)
                {
                    string paramValue = GetParameterValue(doc, elem);
                    if (paramValue == null) continue;

                    if (MatchesCondition(paramValue))
                    {
                        var elementData = new Dictionary<string, object>
                        {
#if REVIT2024_OR_GREATER
                            { "elementId", elem.Id.Value },
#else
                            { "elementId", elem.Id.IntegerValue },
#endif
                            { "category", elem.Category?.Name ?? "Unknown" },
                            { "familyName", GetFamilyName(elem) },
                            { "typeName", GetTypeName(doc, elem) },
                            { "matchedValue", paramValue }
                        };

                        // Add requested return parameters
                        if (ReturnParameters.Count > 0)
                        {
                            var extraParams = new Dictionary<string, string>();
                            foreach (var rpName in ReturnParameters)
                            {
                                var rp = elem.LookupParameter(rpName);
                                if (rp != null)
                                    extraParams[rpName] = rp.AsValueString() ?? rp.AsString() ?? "";
                                else
                                {
                                    // Try type parameter
                                    var typeElem = doc.GetElement(elem.GetTypeId());
                                    if (typeElem != null)
                                    {
                                        var tp = typeElem.LookupParameter(rpName);
                                        if (tp != null)
                                            extraParams[rpName] = tp.AsValueString() ?? tp.AsString() ?? "";
                                    }
                                }
                            }
                            elementData["parameters"] = extraParams;
                        }

                        matchedElements.Add(elementData);
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Found {matchedElements.Count} elements matching condition '{Condition}' on parameter '{ParameterName}'",
                    Response = new
                    {
                        matchCount = matchedElements.Count,
                        totalScanned = elements.Count,
                        parameterName = ParameterName,
                        condition = Condition,
                        value = Value,
                        elements = matchedElements
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Filter by parameter value failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private string GetParameterValue(Document doc, Element elem)
        {
            Parameter param = null;
            bool checkInstance = ParameterType == "instance" || ParameterType == "both";
            bool checkType = ParameterType == "type" || ParameterType == "both";

            if (checkInstance)
                param = elem.LookupParameter(ParameterName);

            if (param == null && checkType)
            {
                var typeElem = doc.GetElement(elem.GetTypeId());
                if (typeElem != null)
                    param = typeElem.LookupParameter(ParameterName);
            }

            if (param == null) return null;

            return param.AsValueString() ?? param.AsString() ?? "";
        }

        private bool MatchesCondition(string paramValue)
        {
            string compareValue = Value ?? "";
            string pv = CaseSensitive ? paramValue : paramValue.ToLowerInvariant();
            string cv = CaseSensitive ? compareValue : compareValue.ToLowerInvariant();

            switch (Condition.ToLower())
            {
                case "equals": return pv == cv;
                case "not_equals": return pv != cv;
                case "contains": return pv.Contains(cv);
                case "not_contains": return !pv.Contains(cv);
                case "begins_with": return pv.StartsWith(cv);
                case "not_begins_with": return !pv.StartsWith(cv);
                case "ends_with": return pv.EndsWith(cv);
                case "not_ends_with": return !pv.EndsWith(cv);
                case "greater_than":
                    if (double.TryParse(paramValue, out double pNum) && double.TryParse(compareValue, out double cNum))
                        return pNum > cNum;
                    return string.Compare(pv, cv, StringComparison.Ordinal) > 0;
                case "less_than":
                    if (double.TryParse(paramValue, out double pNum2) && double.TryParse(compareValue, out double cNum2))
                        return pNum2 < cNum2;
                    return string.Compare(pv, cv, StringComparison.Ordinal) < 0;
                case "is_empty": return string.IsNullOrEmpty(paramValue);
                case "is_not_empty": return !string.IsNullOrEmpty(paramValue);
                default: return false;
            }
        }

        private string GetFamilyName(Element elem)
        {
            if (elem is FamilyInstance fi)
                return fi.Symbol?.Family?.Name ?? "";
            return "";
        }

        private string GetTypeName(Document doc, Element elem)
        {
            var typeElem = doc.GetElement(elem.GetTypeId());
            return typeElem?.Name ?? "";
        }

        public string GetName() => "Filter By Parameter Value";
    }
}
