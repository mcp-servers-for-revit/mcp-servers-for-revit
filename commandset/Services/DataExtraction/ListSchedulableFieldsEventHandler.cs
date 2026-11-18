using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class ListSchedulableFieldsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string CategoryName { get; set; } = "OST_Rooms";
        public string ScheduleType { get; set; } = "regular";

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

                // Resolve category
                var bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), CategoryName);
                var catId = new ElementId(bic);

                // Create temp schedule based on type
                ViewSchedule schedule;
                using (var tx = new Transaction(doc, "Temp schedule for field discovery"))
                {
                    tx.Start();

                    switch (ScheduleType.ToLowerInvariant())
                    {
                        case "material_takeoff":
                            schedule = ViewSchedule.CreateMaterialTakeoff(doc, catId);
                            break;
                        case "key_schedule":
                            schedule = ViewSchedule.CreateKeySchedule(doc, catId);
                            break;
                        default:
                            schedule = ViewSchedule.CreateSchedule(doc, catId);
                            break;
                    }

                    tx.Commit();
                }

                // Get schedulable fields
                var schedulableFields = schedule.Definition.GetSchedulableFields();

                var fields = schedulableFields.Select(f => new
                {
                    name = f.GetName(doc),
                    fieldType = f.FieldType.ToString(),
                    parameterId = f.ParameterId.GetValue()
                }).OrderBy(f => f.name).ToList();

                // Delete temp schedule
                using (var tx = new Transaction(doc, "Delete temp schedule"))
                {
                    tx.Start();
                    doc.Delete(schedule.Id);
                    tx.Commit();
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Found {fields.Count} schedulable fields for {CategoryName} ({ScheduleType})",
                    Response = new
                    {
                        category = CategoryName,
                        scheduleType = ScheduleType,
                        fieldCount = fields.Count,
                        fields
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"List schedulable fields failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "List Schedulable Fields";
    }
}
