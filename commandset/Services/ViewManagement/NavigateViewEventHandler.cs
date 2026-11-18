using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class NavigateViewEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string Action { get; set; } = "activate";
        public long? ViewId { get; set; }
        public string ViewName { get; set; }
        public List<long> ElementIds { get; set; } = new List<long>();
        public double? ZoomFactor { get; set; }

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 15000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc.Document;

                switch (Action.ToLowerInvariant())
                {
                    case "activate":
                        ExecuteActivate(uidoc, doc);
                        break;
                    case "zoom_to_fit":
                        ExecuteZoomToFit(uidoc, doc);
                        break;
                    case "zoom_to_elements":
                        ExecuteZoomToElements(uidoc, doc);
                        break;
                    case "zoom":
                        ExecuteZoom(uidoc, doc);
                        break;
                    case "close":
                        ExecuteClose(uidoc, doc);
                        break;
                    default:
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message = $"Unknown action: {Action}. Valid actions: activate, zoom_to_fit, zoom_to_elements, zoom, close."
                        };
                        break;
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Navigate view failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private void ExecuteActivate(UIDocument uidoc, Document doc)
        {
            var targetView = FindView(doc);
            if (targetView == null)
                throw new InvalidOperationException("Could not find the specified view. Provide a valid viewId or viewName.");

            uidoc.ActiveView = targetView;

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Activated view '{targetView.Name}'",
                Response = BuildViewResponse("activate", targetView, "View activated successfully")
            };
        }

        private void ExecuteZoomToFit(UIDocument uidoc, Document doc)
        {
            View targetView = null;
            if (ViewId.HasValue || !string.IsNullOrEmpty(ViewName))
            {
                targetView = FindView(doc);
                if (targetView == null)
                    throw new InvalidOperationException("Could not find the specified view.");
            }
            else
            {
                targetView = uidoc.ActiveView;
            }

            var uiView = GetUIView(uidoc, targetView);
            uiView.ZoomToFit();

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Zoomed to fit in view '{targetView.Name}'",
                Response = BuildViewResponse("zoom_to_fit", targetView, "Zoomed to fit successfully")
            };
        }

        private void ExecuteZoomToElements(UIDocument uidoc, Document doc)
        {
            if (ElementIds == null || ElementIds.Count == 0)
                throw new InvalidOperationException("No element IDs provided for zoom_to_elements action.");

            var elementIdCollection = new List<ElementId>();
            foreach (var id in ElementIds)
            {
                elementIdCollection.Add(RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(id));
            }

            uidoc.ShowElements(elementIdCollection);

            var activeView = uidoc.ActiveView;
            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Zoomed to {elementIdCollection.Count} element(s)",
                Response = new
                {
                    action = "zoom_to_elements",
                    viewId = activeView.Id.GetValue(),
                    viewName = activeView.Name,
                    viewType = activeView.ViewType.ToString(),
                    success = true,
                    message = $"Zoomed to {elementIdCollection.Count} element(s)",
                    elementCount = elementIdCollection.Count
                }
            };
        }

        private void ExecuteZoom(UIDocument uidoc, Document doc)
        {
            if (!ZoomFactor.HasValue)
                throw new InvalidOperationException("zoomFactor is required for the 'zoom' action.");

            View targetView = null;
            if (ViewId.HasValue || !string.IsNullOrEmpty(ViewName))
            {
                targetView = FindView(doc);
                if (targetView == null)
                    throw new InvalidOperationException("Could not find the specified view.");
            }
            else
            {
                targetView = uidoc.ActiveView;
            }

            var uiView = GetUIView(uidoc, targetView);
            uiView.Zoom(ZoomFactor.Value);

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Zoomed with factor {ZoomFactor.Value} in view '{targetView.Name}'",
                Response = new
                {
                    action = "zoom",
                    viewId = targetView.Id.GetValue(),
                    viewName = targetView.Name,
                    viewType = targetView.ViewType.ToString(),
                    success = true,
                    message = $"Zoom factor {ZoomFactor.Value} applied",
                    zoomFactor = ZoomFactor.Value
                }
            };
        }

        private void ExecuteClose(UIDocument uidoc, Document doc)
        {
            if (!ViewId.HasValue && string.IsNullOrEmpty(ViewName))
                throw new InvalidOperationException("viewId or viewName is required for the 'close' action.");

            var targetView = FindView(doc);
            if (targetView == null)
                throw new InvalidOperationException("Could not find the specified view.");

            var uiView = GetUIViewOrNull(uidoc, targetView);
            if (uiView == null)
                throw new InvalidOperationException($"View '{targetView.Name}' is not currently open.");

            uiView.Close();

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Closed view '{targetView.Name}'",
                Response = BuildViewResponse("close", targetView, "View closed successfully")
            };
        }

        private View FindView(Document doc)
        {
            if (ViewId.HasValue)
            {
                var elementId = RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(ViewId.Value);
                var element = doc.GetElement(elementId);
                return element as View;
            }

            if (!string.IsNullOrEmpty(ViewName))
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name.IndexOf(ViewName, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return null;
        }

        private UIView GetUIView(UIDocument uidoc, View targetView)
        {
            var uiView = GetUIViewOrNull(uidoc, targetView);
            if (uiView != null)
                return uiView;

            // View is not open; activate it first, then retrieve its UIView
            uidoc.ActiveView = targetView;
            uiView = uidoc.GetOpenUIViews().FirstOrDefault(uv => uv.ViewId == targetView.Id);
            if (uiView == null)
                throw new InvalidOperationException($"Could not obtain UIView for view '{targetView.Name}'.");
            return uiView;
        }

        private UIView GetUIViewOrNull(UIDocument uidoc, View targetView)
        {
            var uiViews = uidoc.GetOpenUIViews();
            return uiViews.FirstOrDefault(uv => uv.ViewId == targetView.Id);
        }

        private object BuildViewResponse(string action, View view, string message)
        {
            return new
            {
                action = action,
                viewId = view.Id.GetValue(),
                viewName = view.Name,
                viewType = view.ViewType.ToString(),
                success = true,
                message = message
            };
        }

        public string GetName() => "Navigate View";
    }
}
