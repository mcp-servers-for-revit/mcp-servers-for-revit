using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetCurrentViewInfoEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Execution result
        public CurrentViewInfo ResultInfo { get; private set; }

        // Error message if execution fails
        public string ErrorMessage { get; private set; }

        // State synchronization object
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Set query parameters
        public void SetParameters()
        {
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        // Implement IWaitableExternalEventHandler interface
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc.Document;
                var activeView = doc.ActiveView;

                ResultInfo = new CurrentViewInfo
                {
#if REVIT2024_OR_GREATER
                    Id = (int)activeView.Id.Value,
#else
                    Id = activeView.Id.IntegerValue,
#endif
                    UniqueId = activeView.UniqueId,
                    Name = activeView.Name,
                    ViewType = activeView.ViewType.ToString(),
                    IsTemplate = activeView.IsTemplate,
                    Scale = activeView.Scale,
                    DetailLevel = activeView.DetailLevel.ToString(),
                };
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to get info: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Get Current View Info";
        }
    }
}
