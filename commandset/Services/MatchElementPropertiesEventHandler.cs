using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class MatchElementPropertiesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long SourceElementId { get; set; } = 0;
        public List<long> TargetElementIds { get; set; } = new List<long>();
        public List<string> ParameterNames { get; set; } = new List<string>();
        public bool IncludeTypeParameters { get; set; } = false;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(long sourceElementId, List<long> targetElementIds, List<string> parameterNames, bool includeTypeParameters)
        {
            SourceElementId = sourceElementId;
            TargetElementIds = targetElementIds ?? new List<long>();
            ParameterNames = parameterNames ?? new List<string>();
            IncludeTypeParameters = includeTypeParameters;
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

                var sourceElement = doc.GetElement(ToElementId(SourceElementId));
                if (sourceElement == null)
                {
                    Result = new AIResult<object> { Success = false, Message = "Source element not found" };
                    return;
                }

                // Collect source parameter values
                var sourceValues = CollectParameterValues(doc, sourceElement);

                if (sourceValues.Count == 0)
                {
                    Result = new AIResult<object> { Success = false, Message = "No matching parameters found on source element" };
                    return;
                }

                int totalCopied = 0;
                var results = new List<object>();

                using (var transaction = new Transaction(doc, "Match Element Properties"))
                {
                    transaction.Start();
                    try
                    {
                        foreach (var targetId in TargetElementIds)
                        {
                            var targetElement = doc.GetElement(ToElementId(targetId));
                            if (targetElement == null) continue;

                            int copiedCount = 0;
                            var paramResults = new List<string>();

                            foreach (var kvp in sourceValues)
                            {
                                var targetParam = targetElement.LookupParameter(kvp.Key);
                                if (targetParam == null && IncludeTypeParameters)
                                {
                                    var typeId = targetElement.GetTypeId();
                                    if (typeId != ElementId.InvalidElementId)
                                        targetParam = doc.GetElement(typeId)?.LookupParameter(kvp.Key);
                                }

                                if (targetParam == null || targetParam.IsReadOnly) continue;

                                try
                                {
                                    CopyParameterValue(targetParam, kvp.Value);
                                    copiedCount++;
                                    paramResults.Add(kvp.Key);
                                }
                                catch { /* skip uncopiable params */ }
                            }

                            totalCopied += copiedCount;
                            results.Add(new
                            {
                                elementId = targetId,
                                parametersCopied = copiedCount,
                                parameters = paramResults
                            });
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                Result = new AIResult<object>
                {
                    Success = totalCopied > 0,
                    Message = $"Copied {totalCopied} parameter values across {TargetElementIds.Count} elements",
                    Response = new { sourceElementId = SourceElementId, totalCopied, results }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Match properties failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private Dictionary<string, (StorageType Type, object Value)> CollectParameterValues(Document doc, Element element)
        {
            var values = new Dictionary<string, (StorageType, object)>();
            bool filterByNames = ParameterNames.Count > 0;

            void ProcessParameters(ParameterSet parameters)
            {
                foreach (Parameter param in parameters)
                {
                    if (!param.HasValue || param.IsReadOnly) continue;
                    string name = param.Definition?.Name;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (filterByNames && !ParameterNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                    object value = param.StorageType switch
                    {
                        StorageType.String => param.AsString(),
                        StorageType.Integer => param.AsInteger(),
                        StorageType.Double => param.AsDouble(),
                        StorageType.ElementId => param.AsElementId(),
                        _ => null
                    };

                    if (value != null)
                        values[name] = (param.StorageType, value);
                }
            }

            ProcessParameters(element.Parameters);

            if (IncludeTypeParameters)
            {
                var typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    var typeElem = doc.GetElement(typeId);
                    if (typeElem != null)
                        ProcessParameters(typeElem.Parameters);
                }
            }

            return values;
        }

        private void CopyParameterValue(Parameter target, (StorageType Type, object Value) source)
        {
            switch (source.Type)
            {
                case StorageType.String:
                    target.Set((string)source.Value ?? "");
                    break;
                case StorageType.Integer:
                    target.Set((int)source.Value);
                    break;
                case StorageType.Double:
                    target.Set((double)source.Value);
                    break;
                case StorageType.ElementId:
                    target.Set((ElementId)source.Value);
                    break;
            }
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Match Element Properties";
    }
}
