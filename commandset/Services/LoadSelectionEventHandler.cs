using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services
{
    public class LoadSelectionEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string SelectionName { get; set; } = "";
        public bool SelectInView { get; set; } = true;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 10000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var uidoc = app.ActiveUIDocument;

                var allSelections = new FilteredElementCollector(doc)
                    .OfClass(typeof(SelectionFilterElement))
                    .Cast<SelectionFilterElement>()
                    .ToList();

                // If no name provided, list all saved selections
                if (string.IsNullOrEmpty(SelectionName))
                {
                    var selectionList = allSelections.Select(s => new
                    {
                        name = s.Name,
                        elementCount = s.GetElementIds().Count
                    }).ToList();

                    Result = new AIResult<object>
                    {
                        Success = true,
                        Message = $"Found {selectionList.Count} saved selections",
                        Response = new
                        {
                            count = selectionList.Count,
                            selections = selectionList
                        }
                    };
                    return;
                }

                // Find specific selection
                var selFilter = allSelections.FirstOrDefault(s => s.Name == SelectionName);
                if (selFilter == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Selection '{SelectionName}' not found. Use load_selection without a name to list all."
                    };
                    return;
                }

                var elementIds = selFilter.GetElementIds();

                // Select in view if requested
                if (SelectInView && elementIds.Count > 0)
                {
                    uidoc.Selection.SetElementIds(elementIds);
                }

                var idList = elementIds.Select(id =>
                {
#if REVIT2024_OR_GREATER
                    return id.Value;
#else
                    return (long)id.IntegerValue;
#endif
                }).ToList();

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Loaded selection '{SelectionName}' with {elementIds.Count} elements",
                    Response = new
                    {
                        name = SelectionName,
                        elementCount = elementIds.Count,
                        elementIds = idList,
                        selectedInView = SelectInView
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Load selection failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Load Selection";
    }
}
