using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class DeleteScheduleEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long ScheduleId { get; set; }
        public string ScheduleName { get; set; } = "";
        public bool Confirm { get; set; }

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

                if (!Confirm)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Deletion not confirmed. Set confirm=true to proceed with deletion."
                    };
                    return;
                }

                var schedule = FindSchedule(doc);

                if (schedule == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = ScheduleId > 0
                            ? $"Schedule with ID {ScheduleId} not found"
                            : $"Schedule with name '{ScheduleName}' not found"
                    };
                    return;
                }

                var deletedName = schedule.Name;
#if REVIT2024_OR_GREATER
                var deletedId = schedule.Id.Value;
#else
                var deletedId = (long)schedule.Id.IntegerValue;
#endif

                using (var transaction = new Transaction(doc, "Delete Schedule"))
                {
                    transaction.Start();
                    try
                    {
                        doc.Delete(schedule.Id);
                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Successfully deleted schedule '{deletedName}'",
                            Response = new
                            {
                                deletedScheduleId = deletedId,
                                deletedScheduleName = deletedName
                            }
                        };
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Delete schedule failed: {ex.Message}"
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private ViewSchedule FindSchedule(Document doc)
        {
            if (ScheduleId > 0)
            {
                return doc.GetElement(RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(ScheduleId)) as ViewSchedule;
            }

            if (!string.IsNullOrEmpty(ScheduleName))
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(s => s.Name.Equals(ScheduleName, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        public string GetName() => "Delete Schedule";
    }
}
