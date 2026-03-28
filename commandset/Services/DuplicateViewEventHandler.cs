using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class DuplicateViewEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> ViewIds { get; set; } = new List<long>();
        public string DuplicateOption { get; set; } = "duplicate";
        public string NewNameSuffix { get; set; } = "";
        public string NewNamePrefix { get; set; } = "";
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
                var option = ParseOption(DuplicateOption);
                var results = new List<object>();

                using (var transaction = new Transaction(doc, "Duplicate Views"))
                {
                    transaction.Start();

                    foreach (var viewId in ViewIds)
                    {
                        var view = doc.GetElement(ToElementId(viewId)) as View;
                        if (view == null || view.IsTemplate) continue;

                        try
                        {
                            var newViewId = view.Duplicate(option);
                            var newView = doc.GetElement(newViewId) as View;

                            if (newView != null && (!string.IsNullOrEmpty(NewNamePrefix) || !string.IsNullOrEmpty(NewNameSuffix)))
                            {
                                string baseName = view.Name;
                                newView.Name = $"{NewNamePrefix}{baseName}{NewNameSuffix}";
                            }

                            results.Add(new
                            {
                                originalViewId = view.Id.Value,
                                newViewId = newViewId.Value,
                                originalName = view.Name,
                                newName = newView?.Name ?? "",
                                success = true
                            });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new
                            {
                                originalViewId = view.Id.Value,
                                newViewId = (long)0,
                                originalName = view.Name,
                                newName = "",
                                success = false,
                                message = ex.Message
                            });
                        }
                    }

                    transaction.Commit();
                }

                int successCount = results.Count(r => ((dynamic)r).success);
                Result = new AIResult<object>
                {
                    Success = successCount > 0,
                    Message = $"Duplicated {successCount}/{ViewIds.Count} views ({DuplicateOption})",
                    Response = new { duplicateOption = DuplicateOption, successCount, views = results }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Duplicate view failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private ViewDuplicateOption ParseOption(string option)
        {
            return option?.ToLower() switch
            {
                "withdependents" or "dependent" => ViewDuplicateOption.AsDependent,
                "withdetailing" or "detailing" => ViewDuplicateOption.WithDetailing,
                _ => ViewDuplicateOption.Duplicate
            };
        }

        private ElementId ToElementId(long id)
        {
            return new ElementId(id);
        }

        public string GetName() => "Duplicate Views";
    }
}
