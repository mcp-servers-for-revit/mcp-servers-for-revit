using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using Newtonsoft.Json.Linq;

namespace RevitMCPCommandSet.Services
{
    public class SetElementParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<SetParameterRequest> Requests { get; set; }
        public AIResult<List<SetParameterResult>> Result { get; private set; }

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
                var results = new List<SetParameterResult>();

                using (var transaction = new Transaction(doc, "Set Element Parameters"))
                {
                    transaction.Start();

                    foreach (var request in Requests)
                    {
                        var result = new SetParameterResult
                        {
                            ElementId = request.ElementId,
                            ParameterName = request.ParameterName
                        };

                        try
                        {
                            var elementId = new ElementId(request.ElementId);
                            var element = doc.GetElement(elementId);
                            if (element == null)
                            {
                                result.Success = false;
                                result.Message = $"Element {request.ElementId} not found";
                                results.Add(result);
                                continue;
                            }

                            // Try instance parameter first
                            Parameter param = element.LookupParameter(request.ParameterName);

                            // Try type parameter if instance not found
                            if (param == null)
                            {
                                var typeId = element.GetTypeId();
                                if (typeId != ElementId.InvalidElementId)
                                {
                                    var typeElement = doc.GetElement(typeId);
                                    param = typeElement?.LookupParameter(request.ParameterName);
                                }
                            }

                            if (param == null)
                            {
                                result.Success = false;
                                result.Message = $"Parameter '{request.ParameterName}' not found";
                                results.Add(result);
                                continue;
                            }

                            if (param.IsReadOnly)
                            {
                                result.Success = false;
                                result.Message = $"Parameter '{request.ParameterName}' is read-only";
                                results.Add(result);
                                continue;
                            }

                            bool setResult = SetParameterValue(param, request.Value);
                            result.Success = setResult;
                            result.Message = setResult ? "Parameter set successfully" : "Failed to set parameter value";
                        }
                        catch (Exception ex)
                        {
                            result.Success = false;
                            result.Message = ex.Message;
                        }

                        results.Add(result);
                    }

                    transaction.Commit();
                }

                int successCount = results.Count(r => r.Success);
                Result = new AIResult<List<SetParameterResult>>
                {
                    Success = successCount > 0,
                    Message = $"Set {successCount}/{results.Count} parameters successfully",
                    Response = results
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<SetParameterResult>>
                {
                    Success = false,
                    Message = $"Failed to set parameters: {ex.Message}",
                    Response = new List<SetParameterResult>()
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private bool SetParameterValue(Parameter param, object value)
        {
            if (value == null) return false;

            // Handle JToken values from JSON deserialization
            if (value is JToken jToken)
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        return param.Set(jToken.Value<string>() ?? "");
                    case StorageType.Integer:
                        return param.Set(jToken.Value<int>());
                    case StorageType.Double:
                        return param.Set(jToken.Value<double>());
                    case StorageType.ElementId:
                        return param.Set(new ElementId(jToken.Value<long>()));
                    default:
                        return false;
                }
            }

            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.Set(value.ToString());
                case StorageType.Integer:
                    if (value is int intVal) return param.Set(intVal);
                    if (int.TryParse(value.ToString(), out int parsedInt)) return param.Set(parsedInt);
                    return false;
                case StorageType.Double:
                    if (value is double dblVal) return param.Set(dblVal);
                    if (double.TryParse(value.ToString(), out double parsedDbl)) return param.Set(parsedDbl);
                    return false;
                case StorageType.ElementId:
                    if (long.TryParse(value.ToString(), out long parsedLong))
                        return param.Set(new ElementId(parsedLong));
                    return false;
                default:
                    return false;
            }
        }

        public string GetName() => "Set Element Parameters";
    }
}
