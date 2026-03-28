using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class PlaceViewportEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public ViewportCreationInfo ViewportInfo { get; set; }
        public AIResult<object> Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                using (var transaction = new Transaction(doc, "Place Viewport"))
                {
                    transaction.Start();

                    var sheetId = new ElementId((long)ViewportInfo.SheetId);
                    var viewId = new ElementId((long)ViewportInfo.ViewId);

                    // Verify sheet exists
                    var sheet = doc.GetElement(sheetId) as ViewSheet;
                    if (sheet == null)
                        throw new ArgumentException($"Sheet with ID {ViewportInfo.SheetId} not found");

                    // Verify view exists
                    var view = doc.GetElement(viewId) as View;
                    if (view == null)
                        throw new ArgumentException($"View with ID {ViewportInfo.ViewId} not found");

                    // Check if view can be added
                    if (!Viewport.CanAddViewToSheet(doc, sheetId, viewId))
                        throw new InvalidOperationException("This view cannot be added to the sheet (it may already be placed on another sheet)");

                    // Position in feet (convert from mm)
                    XYZ position = new XYZ(
                        ViewportInfo.PositionX / 304.8,
                        ViewportInfo.PositionY / 304.8,
                        0
                    );

                    var viewport = Viewport.Create(doc, sheetId, viewId, position);

                    transaction.Commit();

                    Result = new AIResult<object>
                    {
                        Success = true,
                        Message = $"Successfully placed viewport on sheet",
                        Response = new
                        {
                            viewportId = viewport.Id.Value,
                            sheetId = sheetId.Value,
                            viewId = viewId.Value,
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to place viewport: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Place Viewport";
    }
}
