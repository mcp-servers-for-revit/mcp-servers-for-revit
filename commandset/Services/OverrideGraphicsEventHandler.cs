using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class OverrideGraphicsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> ElementIds { get; set; } = new List<long>();
        public long ViewId { get; set; } = 0;
        public int ProjectionLineColorR { get; set; } = -1;
        public int ProjectionLineColorG { get; set; } = -1;
        public int ProjectionLineColorB { get; set; } = -1;
        public int SurfaceForegroundColorR { get; set; } = -1;
        public int SurfaceForegroundColorG { get; set; } = -1;
        public int SurfaceForegroundColorB { get; set; } = -1;
        public int Transparency { get; set; } = -1;
        public bool? IsHalftone { get; set; }
        public int ProjectionLineWeight { get; set; } = -1;
        public string Action { get; set; } = "set";
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
                var view = ViewId > 0
                    ? doc.GetElement(ToElementId(ViewId)) as View
                    : doc.ActiveView;

                if (view == null)
                {
                    Result = new AIResult<object> { Success = false, Message = "View not found" };
                    return;
                }

                int successCount = 0;

                using (var transaction = new Transaction(doc, "Override Graphics"))
                {
                    transaction.Start();

                    foreach (var id in ElementIds)
                    {
                        var elemId = ToElementId(id);
                        if (doc.GetElement(elemId) == null) continue;

                        if (Action == "reset")
                        {
                            view.SetElementOverrides(elemId, new OverrideGraphicSettings());
                            successCount++;
                            continue;
                        }

                        var ogs = new OverrideGraphicSettings();

                        if (ProjectionLineColorR >= 0)
                            ogs.SetProjectionLineColor(new Color(
                                (byte)ProjectionLineColorR, (byte)ProjectionLineColorG, (byte)ProjectionLineColorB));

                        if (SurfaceForegroundColorR >= 0)
                        {
                            ogs.SetSurfaceForegroundPatternColor(new Color(
                                (byte)SurfaceForegroundColorR, (byte)SurfaceForegroundColorG, (byte)SurfaceForegroundColorB));

                            // Apply solid fill pattern
                            var solidFill = new FilteredElementCollector(doc)
                                .OfClass(typeof(FillPatternElement))
                                .Cast<FillPatternElement>()
                                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill);
                            if (solidFill != null)
                                ogs.SetSurfaceForegroundPatternId(solidFill.Id);
                        }

                        if (Transparency >= 0)
                            ogs.SetSurfaceTransparency(Math.Min(Transparency, 100));

                        if (IsHalftone.HasValue)
                            ogs.SetHalftone(IsHalftone.Value);

                        if (ProjectionLineWeight >= 0)
                            ogs.SetProjectionLineWeight(ProjectionLineWeight);

                        view.SetElementOverrides(elemId, ogs);
                        successCount++;
                    }

                    transaction.Commit();
                }

                Result = new AIResult<object>
                {
                    Success = successCount > 0,
                    Message = $"{(Action == "reset" ? "Reset" : "Applied")} graphic overrides for {successCount} elements in '{view.Name}'",
                    Response = new { viewName = view.Name, action = Action, successCount }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Override graphics failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private ElementId ToElementId(long id)
        {
            return new ElementId(id);
        }

        public string GetName() => "Override Graphics";
    }
}
