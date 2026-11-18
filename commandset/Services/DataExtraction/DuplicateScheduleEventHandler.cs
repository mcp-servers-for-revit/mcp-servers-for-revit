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
    public class DuplicateScheduleEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long ScheduleId { get; set; }
        public string ScheduleName { get; set; } = "";
        public string NewName { get; set; } = "";

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

                using (var transaction = new Transaction(doc, "Duplicate Schedule"))
                {
                    transaction.Start();
                    try
                    {
                        var newId = schedule.Duplicate(ViewDuplicateOption.Duplicate);
                        var newSchedule = doc.GetElement(newId) as ViewSchedule;

                        if (newSchedule != null && !string.IsNullOrEmpty(NewName))
                        {
                            newSchedule.Name = NewName;
                        }

                        transaction.Commit();

                        var newScheduleName = newSchedule?.Name ?? "";

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Successfully duplicated schedule '{schedule.Name}' as '{newScheduleName}'",
                            Response = new
                            {
#if REVIT2024_OR_GREATER
                                sourceScheduleId = schedule.Id.Value,
#else
                                sourceScheduleId = schedule.Id.IntegerValue,
#endif
                                sourceScheduleName = schedule.Name,
#if REVIT2024_OR_GREATER
                                newScheduleId = newId.Value,
#else
                                newScheduleId = newId.IntegerValue,
#endif
                                newScheduleName
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
                    Message = $"Duplicate schedule failed: {ex.Message}"
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

        public string GetName() => "Duplicate Schedule";
    }
}
