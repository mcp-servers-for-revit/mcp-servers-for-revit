using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetSharedParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string CategoryFilter { get; set; }
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
                var parameterList = new List<object>();

                var bindingMap = doc.ParameterBindings;
                var iterator = bindingMap.ForwardIterator();
                iterator.Reset();

                while (iterator.MoveNext())
                {
                    var definition = iterator.Key as Definition;
                    var binding = iterator.Current as ElementBinding;

                    if (definition == null || binding == null)
                        continue;

                    // Collect category names from the binding
                    var categoryNames = new List<string>();
                    if (binding.Categories != null)
                    {
                        foreach (Category cat in binding.Categories)
                        {
                            if (cat != null)
                                categoryNames.Add(cat.Name);
                        }
                    }

                    // Apply category filter if provided
                    if (!string.IsNullOrEmpty(CategoryFilter))
                    {
                        bool matches = categoryNames.Any(name =>
                            name.IndexOf(CategoryFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!matches)
                            continue;
                    }

                    bool isInstance = binding is InstanceBinding;

                    // Determine if the definition is shared (has a GUID)
                    bool isShared = false;
                    string guidString = "";
                    if (definition is ExternalDefinition externalDef)
                    {
                        isShared = true;
                        guidString = externalDef.GUID.ToString();
                    }

                    var dataType = (definition as InternalDefinition)?.GetDataType()
                                   ?? (definition as ExternalDefinition)?.GetDataType();
                    var paramTypeName = dataType != null ? dataType.TypeId : "Unknown";

                    var groupTypeId = definition.GetGroupTypeId();
                    var groupName = groupTypeId != null ? groupTypeId.TypeId : "";

                    parameterList.Add(new
                    {
                        name = definition.Name,
                        isShared = isShared,
                        guid = guidString,
                        isInstance = isInstance,
                        parameterType = paramTypeName,
                        parameterGroup = groupName,
                        categories = categoryNames
                    });
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Retrieved {parameterList.Count} project parameter(s)",
                    Response = parameterList
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to get shared parameters: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Get Shared Parameters";
    }
}
