using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetElementParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public long[] ElementIds { get; set; }
        public bool IncludeTypeParameters { get; set; } = true;
        public AIResult<List<ElementParametersResult>> Result { get; private set; }

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
                var results = new List<ElementParametersResult>();

                foreach (var id in ElementIds)
                {
#if REVIT2024_OR_GREATER
                    var elementId = new ElementId(id);
#else
                    var elementId = new ElementId((int)id);
#endif
                    var element = doc.GetElement(elementId);
                    if (element == null) continue;

                    var result = new ElementParametersResult
                    {
#if REVIT2024_OR_GREATER
                        ElementId = element.Id.Value,
#else
                        ElementId = element.Id.IntegerValue,
#endif
                        ElementName = element.Name,
                        Category = element.Category?.Name,
                    };

                    // Instance parameters
                    foreach (Parameter param in element.Parameters)
                    {
                        result.Parameters.Add(ExtractParameterInfo(param));
                    }

                    // Type parameters
                    if (IncludeTypeParameters)
                    {
                        var typeId = element.GetTypeId();
                        if (typeId != ElementId.InvalidElementId)
                        {
                            var typeElement = doc.GetElement(typeId);
                            if (typeElement != null)
                            {
                                foreach (Parameter param in typeElement.Parameters)
                                {
                                    var info = ExtractParameterInfo(param);
                                    info.Name = "[Type] " + info.Name;
                                    result.Parameters.Add(info);
                                }
                            }
                        }
                    }

                    results.Add(result);
                }

                Result = new AIResult<List<ElementParametersResult>>
                {
                    Success = true,
                    Message = $"Retrieved parameters for {results.Count} elements",
                    Response = results
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<ElementParametersResult>>
                {
                    Success = false,
                    Message = $"Failed to get parameters: {ex.Message}",
                    Response = new List<ElementParametersResult>()
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private ParamData ExtractParameterInfo(Parameter param)
        {
            var info = new ParamData
            {
                Name = param.Definition?.Name ?? "Unknown",
                IsReadOnly = param.IsReadOnly,
                IsShared = param.IsShared,
                HasValue = param.HasValue,
                StorageType = param.StorageType.ToString(),
                GroupName = param.Definition?.GetGroupTypeId()?.TypeId ?? ""
            };

            if (param.HasValue)
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        info.Value = param.AsString();
                        break;
                    case StorageType.Integer:
                        info.Value = param.AsInteger();
                        break;
                    case StorageType.Double:
                        info.Value = param.AsDouble();
                        break;
                    case StorageType.ElementId:
#if REVIT2024_OR_GREATER
                        info.Value = param.AsElementId().Value;
#else
                        info.Value = param.AsElementId().IntegerValue;
#endif
                        break;
                    default:
                        info.Value = param.AsValueString();
                        break;
                }
            }

            return info;
        }

        public string GetName() => "Get Element Parameters";
    }
}
