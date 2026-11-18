using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class SyncCsvParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private List<ElementParameterUpdate> _updates;
        private bool _dryRun = true;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters(List<ElementParameterUpdate> updates, bool dryRun)
        {
            _updates = updates;
            _dryRun = dryRun;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var results = new List<object>();
                int successCount = 0;
                int errorCount = 0;

                Transaction transaction = null;
                if (!_dryRun)
                {
                    transaction = new Transaction(doc, "Sync CSV Parameters");
                    transaction.Start();
                }

                try
                {
                    foreach (var update in _updates)
                    {
                        var element = doc.GetElement(Utils.ElementIdExtensions.FromLong(update.ElementId));
                        if (element == null)
                        {
                            errorCount++;
                            results.Add(new
                            {
                                elementId = update.ElementId,
                                success = false,
                                message = "Element not found",
                                parameters = new List<object>()
                            });
                            continue;
                        }

                        var paramResults = new List<object>();
                        int elemSuccess = 0;

                        foreach (var kvp in update.Parameters)
                        {
                            string paramName = kvp.Key;
                            string paramValue = kvp.Value?.ToString() ?? "";

                            var param = element.LookupParameter(paramName);
                            if (param == null)
                            {
                                var typeId = element.GetTypeId();
                                if (typeId != null && typeId != ElementId.InvalidElementId)
                                {
                                    var type = doc.GetElement(typeId);
                                    param = type?.LookupParameter(paramName);
                                }
                            }

                            // Fallback: case-insensitive search across all parameters
                            if (param == null)
                            {
                                param = FindParameterCaseInsensitive(element, paramName);
                                if (param == null)
                                {
                                    var typeId = element.GetTypeId();
                                    if (typeId != null && typeId != ElementId.InvalidElementId)
                                    {
                                        var type = doc.GetElement(typeId);
                                        if (type != null)
                                            param = FindParameterCaseInsensitive(type, paramName);
                                    }
                                }
                            }

                            if (param == null)
                            {
                                // Suggest similar parameter names
                                var suggestions = GetSimilarParameterNames(element, paramName);
                                string msg = suggestions.Count > 0
                                    ? $"Parameter not found. Did you mean: {string.Join(", ", suggestions)}?"
                                    : "Parameter not found";
                                paramResults.Add(new { name = paramName, success = false, message = msg });
                                continue;
                            }

                            if (param.IsReadOnly)
                            {
                                paramResults.Add(new { name = paramName, success = false, message = "Parameter is read-only" });
                                continue;
                            }

                            string oldValue = GetParameterValueAsString(param);

                            if (_dryRun)
                            {
                                paramResults.Add(new { name = paramName, success = true, oldValue, newValue = paramValue, message = "Would update (dry run)" });
                                elemSuccess++;
                            }
                            else
                            {
                                bool set = SetParameterValue(param, paramValue);
                                if (set)
                                {
                                    paramResults.Add(new { name = paramName, success = true, oldValue, newValue = paramValue });
                                    elemSuccess++;
                                }
                                else
                                {
                                    paramResults.Add(new { name = paramName, success = false, message = "Failed to set value" });
                                }
                            }
                        }

                        if (elemSuccess > 0) successCount++;
                        else errorCount++;

                        results.Add(new
                        {
                            elementId = update.ElementId,
                            elementName = element.Name,
                            success = elemSuccess > 0,
                            parameters = paramResults
                        });
                    }

                    if (!_dryRun && transaction != null)
                        transaction.Commit();
                }
                catch
                {
                    if (!_dryRun && transaction != null)
                        transaction.RollBack();
                    throw;
                }
                finally
                {
                    transaction?.Dispose();
                }

                string mode = _dryRun ? " (dry run)" : "";
                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Processed {_updates.Count} elements: {successCount} updated, {errorCount} errors{mode}",
                    Response = new { dryRun = _dryRun, totalProcessed = _updates.Count, successCount, errorCount, results }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Sync CSV parameters failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private string GetParameterValueAsString(Parameter param)
        {
            switch (param.StorageType)
            {
                case StorageType.String: return param.AsString() ?? "";
                case StorageType.Integer: return param.AsInteger().ToString();
                case StorageType.Double: return param.AsDouble().ToString("F6");
                case StorageType.ElementId: return param.AsElementId().GetValue().ToString();
                default: return "";
            }
        }

        private bool SetParameterValue(Parameter param, string value)
        {
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        return param.Set(value);
                    case StorageType.Integer:
                        if (int.TryParse(value, out int intVal)) return param.Set(intVal);
                        if (bool.TryParse(value, out bool boolVal)) return param.Set(boolVal ? 1 : 0);
                        return false;
                    case StorageType.Double:
                        if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double dblVal))
                            return param.Set(dblVal);
                        return false;
                    case StorageType.ElementId:
                        if (long.TryParse(value, out long idVal))
                            return param.Set(Utils.ElementIdExtensions.FromLong(idVal));
                        return false;
                    default:
                        return false;
                }
            }
            catch { return false; }
        }

        private Parameter FindParameterCaseInsensitive(Element element, string paramName)
        {
            foreach (Parameter p in element.Parameters)
            {
                if (string.Equals(p.Definition?.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        private List<string> GetSimilarParameterNames(Element element, string paramName)
        {
            var names = new List<string>();
            string lower = paramName.ToLowerInvariant();
            foreach (Parameter p in element.Parameters)
            {
                string name = p.Definition?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.ToLowerInvariant().Contains(lower) || lower.Contains(name.ToLowerInvariant()))
                    names.Add(name);
            }
            if (names.Count > 5) names = names.GetRange(0, 5);
            return names;
        }

        public string GetName() => "Sync CSV Parameters";
    }

    public class ElementParameterUpdate
    {
        public long ElementId { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    }
}
