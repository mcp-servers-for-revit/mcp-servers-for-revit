using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Models.Views;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class ModifyScheduleEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long ScheduleId { get; set; }
        public string ScheduleName { get; set; } = "";
        public string Action { get; set; } = "";
        public List<string> FieldNames { get; set; } = new List<string>();
        public List<ScheduleFilterInfo> Filters { get; set; } = new List<ScheduleFilterInfo>();
        public List<ScheduleSortInfo> SortFields { get; set; } = new List<ScheduleSortInfo>();
        public string NewName { get; set; } = "";
        public bool? ShowTitle { get; set; }
        public bool? ShowHeaders { get; set; }
        public bool? ShowGridLines { get; set; }
        public bool? IsItemized { get; set; }

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

                using (var transaction = new Transaction(doc, "Modify Schedule"))
                {
                    transaction.Start();
                    try
                    {
                        string details = ExecuteAction(schedule);

                        transaction.Commit();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Successfully performed '{Action}' on schedule '{schedule.Name}'",
                            Response = new
                            {
#if REVIT2024_OR_GREATER
                                scheduleId = schedule.Id.Value,
#else
                                scheduleId = schedule.Id.IntegerValue,
#endif
                                scheduleName = schedule.Name,
                                action = Action,
                                details
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
                    Message = $"Modify schedule failed: {ex.Message}"
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

        private string ExecuteAction(ViewSchedule schedule)
        {
            var def = schedule.Definition;

            switch (Action.ToLowerInvariant().Replace("_", ""))
            {
                case "addfield":
                    return AddFields(schedule, def);

                case "removefield":
                    return RemoveFields(schedule, def);

                case "setfilters":
                    return SetFilters(schedule, def);

                case "clearfilters":
                    def.ClearFilters();
                    return "All filters cleared";

                case "setsorting":
                    return SetSorting(schedule, def);

                case "clearsorting":
                    def.ClearSortGroupFields();
                    return "All sort/group fields cleared";

                case "rename":
                    if (string.IsNullOrEmpty(NewName))
                        throw new ArgumentException("NewName is required for rename action");
                    var oldName = schedule.Name;
                    schedule.Name = NewName;
                    return $"Renamed from '{oldName}' to '{NewName}'";

                case "setdisplayoptions":
                    return SetDisplayOptions(def);

                default:
                    throw new ArgumentException($"Unknown action: {Action}. Valid actions: add_field, remove_field, set_filters, clear_filters, set_sorting, clear_sorting, rename, set_display_options");
            }
        }

        private string AddFields(ViewSchedule schedule, ScheduleDefinition def)
        {
            if (FieldNames == null || FieldNames.Count == 0)
                throw new ArgumentException("FieldNames is required for add_field action");

            var schedulableFields = def.GetSchedulableFields();
            var added = new List<string>();
            var notFound = new List<string>();

            foreach (var fieldName in FieldNames)
            {
                var matchingField = schedulableFields.FirstOrDefault(sf =>
                {
                    var name = sf.GetName(schedule.Document);
                    return name.Equals(fieldName, StringComparison.OrdinalIgnoreCase);
                });

                if (matchingField != null)
                {
                    def.AddField(matchingField);
                    added.Add(fieldName);
                }
                else
                {
                    notFound.Add(fieldName);
                }
            }

            var result = $"Added {added.Count} field(s): {string.Join(", ", added)}";
            if (notFound.Count > 0)
                result += $". Not found: {string.Join(", ", notFound)}";
            return result;
        }

        private string RemoveFields(ViewSchedule schedule, ScheduleDefinition def)
        {
            if (FieldNames == null || FieldNames.Count == 0)
                throw new ArgumentException("FieldNames is required for remove_field action");

            var removed = new List<string>();
            var notFound = new List<string>();
            var fieldCount = def.GetFieldCount();

            foreach (var fieldName in FieldNames)
            {
                bool found = false;
                for (int i = fieldCount - 1; i >= 0; i--)
                {
                    var field = def.GetField(i);
                    if (field.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        def.RemoveField(i);
                        removed.Add(fieldName);
                        found = true;
                        fieldCount--;
                        break;
                    }
                }
                if (!found)
                    notFound.Add(fieldName);
            }

            var result = $"Removed {removed.Count} field(s): {string.Join(", ", removed)}";
            if (notFound.Count > 0)
                result += $". Not found: {string.Join(", ", notFound)}";
            return result;
        }

        private string SetFilters(ViewSchedule schedule, ScheduleDefinition def)
        {
            if (Filters == null || Filters.Count == 0)
                throw new ArgumentException("Filters is required for set_filters action");

            def.ClearFilters();

            // Build a map of existing field names to their ScheduleFieldIds
            var fieldMap = new Dictionary<string, ScheduleFieldId>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                var field = def.GetField(i);
                fieldMap[field.GetName()] = field.FieldId;
            }

            int addedCount = 0;
            foreach (var filterInfo in Filters)
            {
                ScheduleFieldId fieldId = null;

                if (!string.IsNullOrEmpty(filterInfo.FieldName) &&
                    fieldMap.TryGetValue(filterInfo.FieldName, out var resolvedFieldId))
                {
                    fieldId = resolvedFieldId;
                }
                else if (filterInfo.FieldIndex >= 0 && filterInfo.FieldIndex < def.GetFieldCount())
                {
                    fieldId = def.GetField(filterInfo.FieldIndex).FieldId;
                }

                if (fieldId == null)
                    continue;

                var filterType = (ScheduleFilterType)Enum.Parse(typeof(ScheduleFilterType), filterInfo.FilterType, true);
                var filter = new ScheduleFilter(fieldId, filterType, filterInfo.FilterValue);
                def.AddFilter(filter);
                addedCount++;
            }

            return $"Set {addedCount} filter(s)";
        }

        private string SetSorting(ViewSchedule schedule, ScheduleDefinition def)
        {
            if (SortFields == null || SortFields.Count == 0)
                throw new ArgumentException("SortFields is required for set_sorting action");

            def.ClearSortGroupFields();

            // Build a map of existing field names to their ScheduleFieldIds
            var fieldMap = new Dictionary<string, ScheduleFieldId>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                var field = def.GetField(i);
                fieldMap[field.GetName()] = field.FieldId;
            }

            int addedCount = 0;
            foreach (var sortInfo in SortFields)
            {
                ScheduleFieldId fieldId = null;

                if (!string.IsNullOrEmpty(sortInfo.FieldName) &&
                    fieldMap.TryGetValue(sortInfo.FieldName, out var resolvedFieldId))
                {
                    fieldId = resolvedFieldId;
                }
                else if (sortInfo.FieldIndex >= 0 && sortInfo.FieldIndex < def.GetFieldCount())
                {
                    fieldId = def.GetField(sortInfo.FieldIndex).FieldId;
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
                def.AddSortGroupField(sortGroupField);
                addedCount++;
            }

            return $"Set {addedCount} sort field(s)";
        }

        private string SetDisplayOptions(ScheduleDefinition def)
        {
            var changes = new List<string>();

            if (ShowTitle.HasValue)
            {
                def.ShowTitle = ShowTitle.Value;
                changes.Add($"ShowTitle={ShowTitle.Value}");
            }
            if (ShowHeaders.HasValue)
            {
                def.ShowHeaders = ShowHeaders.Value;
                changes.Add($"ShowHeaders={ShowHeaders.Value}");
            }
            if (ShowGridLines.HasValue)
            {
                def.ShowGridLines = ShowGridLines.Value;
                changes.Add($"ShowGridLines={ShowGridLines.Value}");
            }
            if (IsItemized.HasValue)
            {
                def.IsItemized = IsItemized.Value;
                changes.Add($"IsItemized={IsItemized.Value}");
            }

            if (changes.Count == 0)
                return "No display options specified";

            return $"Updated: {string.Join(", ", changes)}";
        }

        public string GetName() => "Modify Schedule";
    }
}
