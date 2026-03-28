using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Views;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateScheduleEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public ScheduleCreationInfo ScheduleInfo { get; set; }
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
                string scheduleType = ScheduleInfo.Type ?? "Regular";

                using (var transaction = new Transaction(doc, "Create Schedule"))
                {
                    transaction.Start();

                    var categoryId = ResolveCategoryId();
                    ViewSchedule schedule;

                    switch (scheduleType)
                    {
                        case "KeySchedule":
                            schedule = ViewSchedule.CreateKeySchedule(doc, categoryId);
                            break;
                        case "MaterialTakeoff":
                            schedule = ViewSchedule.CreateMaterialTakeoff(doc, categoryId);
                            break;
                        default:
                            schedule = ViewSchedule.CreateSchedule(doc, categoryId);
                            break;
                    }

                    if (!string.IsNullOrEmpty(ScheduleInfo.Name))
                        schedule.Name = ScheduleInfo.Name;

                    // Add fields
                    var addedFields = AddFields(schedule);

                    // Add filters
                    AddFilters(schedule, addedFields);

                    // Add sort/group fields
                    AddSortFields(schedule, addedFields);

                    // Set display properties
                    SetDisplayProperties(schedule);

                    transaction.Commit();

                    Result = new AIResult<object>
                    {
                        Success = true,
                        Message = $"Successfully created {scheduleType} schedule '{schedule.Name}'",
                        Response = new
                        {
                            scheduleId = schedule.Id.Value,
                            name = schedule.Name
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to create schedule: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private ElementId ResolveCategoryId()
        {
            if (!string.IsNullOrEmpty(ScheduleInfo.CategoryName))
            {
                var bic = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), ScheduleInfo.CategoryName);
                return new ElementId(bic);
            }

            if (ScheduleInfo.CategoryId > 0)
            {
                return new ElementId((long)ScheduleInfo.CategoryId);
            }

            // Multi-category schedule
            return ElementId.InvalidElementId;
        }

        private Dictionary<string, ScheduleFieldId> AddFields(ViewSchedule schedule)
        {
            var addedFields = new Dictionary<string, ScheduleFieldId>(StringComparer.OrdinalIgnoreCase);
            var schedulableFields = schedule.Definition.GetSchedulableFields();

            if (ScheduleInfo.Fields == null || ScheduleInfo.Fields.Count == 0)
                return addedFields;

            foreach (var fieldInfo in ScheduleInfo.Fields)
            {
                var matchingField = schedulableFields.FirstOrDefault(sf =>
                {
                    var name = sf.GetName(schedule.Document);
                    return name.Equals(fieldInfo.ParameterName, StringComparison.OrdinalIgnoreCase);
                });

                if (matchingField == null)
                    continue;

                var addedField = schedule.Definition.AddField(matchingField);
                addedFields[fieldInfo.ParameterName] = addedField.FieldId;

                if (!string.IsNullOrEmpty(fieldInfo.Heading))
                    addedField.ColumnHeading = fieldInfo.Heading;

                if (fieldInfo.IsHidden)
                    addedField.IsHidden = true;

                if (!string.IsNullOrEmpty(fieldInfo.HorizontalAlignment))
                {
                    switch (fieldInfo.HorizontalAlignment.ToLower())
                    {
                        case "center":
                            addedField.HorizontalAlignment = ScheduleHorizontalAlignment.Center;
                            break;
                        case "right":
                            addedField.HorizontalAlignment = ScheduleHorizontalAlignment.Right;
                            break;
                        default:
                            addedField.HorizontalAlignment = ScheduleHorizontalAlignment.Left;
                            break;
                    }
                }
            }

            return addedFields;
        }

        private void AddFilters(ViewSchedule schedule, Dictionary<string, ScheduleFieldId> addedFields)
        {
            if (ScheduleInfo.Filters == null || ScheduleInfo.Filters.Count == 0)
                return;

            foreach (var filterInfo in ScheduleInfo.Filters)
            {
                ScheduleFieldId fieldId = null;

                if (!string.IsNullOrEmpty(filterInfo.FieldName) &&
                    addedFields.TryGetValue(filterInfo.FieldName, out var resolvedFieldId))
                {
                    fieldId = resolvedFieldId;
                }
                else if (filterInfo.FieldIndex >= 0 && filterInfo.FieldIndex < schedule.Definition.GetFieldCount())
                {
                    fieldId = schedule.Definition.GetField(filterInfo.FieldIndex).FieldId;
                }

                if (fieldId == null)
                    continue;

                var filterType = (ScheduleFilterType)Enum.Parse(typeof(ScheduleFilterType), filterInfo.FilterType, true);
                var filter = new ScheduleFilter(fieldId, filterType, filterInfo.FilterValue);
                schedule.Definition.AddFilter(filter);
            }
        }

        private void AddSortFields(ViewSchedule schedule, Dictionary<string, ScheduleFieldId> addedFields)
        {
            if (ScheduleInfo.SortFields == null || ScheduleInfo.SortFields.Count == 0)
                return;

            foreach (var sortInfo in ScheduleInfo.SortFields)
            {
                ScheduleFieldId fieldId = null;

                if (!string.IsNullOrEmpty(sortInfo.FieldName) &&
                    addedFields.TryGetValue(sortInfo.FieldName, out var resolvedFieldId))
                {
                    fieldId = resolvedFieldId;
                }
                else if (sortInfo.FieldIndex >= 0 && sortInfo.FieldIndex < schedule.Definition.GetFieldCount())
                {
                    fieldId = schedule.Definition.GetField(sortInfo.FieldIndex).FieldId;
                }

                if (fieldId == null)
                    continue;

                var sortOrder = ScheduleSortOrder.Ascending;
                if (!string.IsNullOrEmpty(sortInfo.SortOrder) &&
                    sortInfo.SortOrder.Equals("Descending", StringComparison.OrdinalIgnoreCase))
                {
                    sortOrder = ScheduleSortOrder.Descending;
                }

                var sortGroupField = new ScheduleSortGroupField(fieldId, sortOrder);
                schedule.Definition.AddSortGroupField(sortGroupField);
            }
        }

        private void SetDisplayProperties(ViewSchedule schedule)
        {
            var definition = schedule.Definition;

            if (ScheduleInfo.ShowTitle.HasValue)
                definition.ShowTitle = ScheduleInfo.ShowTitle.Value;

            if (ScheduleInfo.ShowHeaders.HasValue)
                definition.ShowHeaders = ScheduleInfo.ShowHeaders.Value;

            if (ScheduleInfo.ShowGridLines.HasValue)
                definition.ShowGridLines = ScheduleInfo.ShowGridLines.Value;
        }

        public string GetName() => "Create Schedule";
    }
}
