using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetLinkedElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string LinkName { get; set; } = "";
        public List<string> Categories { get; set; } = new();
        public List<string> ParameterNames { get; set; } = new();
        public int MaxElements { get; set; } = 5000;
        public object Result { get; private set; }

        public void SetParameters(string linkName, List<string> categories,
            List<string> parameterNames, int maxElements)
        {
            LinkName = linkName;
            Categories = categories;
            ParameterNames = parameterNames;
            MaxElements = maxElements;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Find linked instances
                var linkInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

                if (!string.IsNullOrEmpty(LinkName))
                {
                    linkInstances = linkInstances
                        .Where(li => li.Name.IndexOf(LinkName, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();
                }

                if (linkInstances.Count == 0)
                {
                    Result = new { success = true, links = new List<object>(), message = "No linked models found" };
                    return;
                }

                var linksData = new List<object>();

                foreach (var linkInstance in linkInstances)
                {
                    var linkDoc = linkInstance.GetLinkDocument();
                    if (linkDoc == null) continue;

                    // Collect elements from linked document
                    var collector = new FilteredElementCollector(linkDoc)
                        .WhereElementIsNotElementType();

                    var elements = new List<Element>();
                    if (Categories.Count > 0)
                    {
                        foreach (var catName in Categories)
                        {
                            foreach (Category cat in linkDoc.Settings.Categories)
                            {
                                if (cat.Name.Equals(catName, StringComparison.OrdinalIgnoreCase))
                                {
                                    var catElements = new FilteredElementCollector(linkDoc)
#if REVIT2024_OR_GREATER
                                        .OfCategory((BuiltInCategory)cat.Id.Value)
#else
                                        .OfCategory((BuiltInCategory)cat.Id.IntegerValue)
#endif
                                        .WhereElementIsNotElementType()
                                        .Take(MaxElements)
                                        .ToList();
                                    elements.AddRange(catElements);
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        elements = collector.Take(MaxElements).ToList();
                    }

                    var elementsData = elements.Select(e =>
                    {
                        var data = new Dictionary<string, object>
                        {
#if REVIT2024_OR_GREATER
                            ["ElementId"] = e.Id.Value,
#else
                            ["ElementId"] = e.Id.IntegerValue,
#endif
                            ["Category"] = e.Category?.Name ?? "",
                            ["Name"] = e.Name
                        };

                        if (ParameterNames.Count > 0)
                        {
                            foreach (var pn in ParameterNames)
                            {
                                var p = e.LookupParameter(pn);
                                data[pn] = p != null && p.HasValue
                                    ? (object)(p.AsValueString() ?? p.AsString() ?? "")
                                    : "";
                            }
                        }
                        return data;
                    }).ToList();

                    linksData.Add(new
                    {
                        linkName = linkInstance.Name,
#if REVIT2024_OR_GREATER
                        linkId = linkInstance.Id.Value,
#else
                        linkId = linkInstance.Id.IntegerValue,
#endif
                        documentTitle = linkDoc.Title,
                        elementCount = elementsData.Count,
                        elements = elementsData
                    });
                }

                Result = new { success = true, linkCount = linksData.Count, links = linksData };
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Get Linked Elements";
    }
}
