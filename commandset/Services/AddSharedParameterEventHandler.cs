using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class AddSharedParameterEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string ParameterName { get; set; }
        public string GroupName { get; set; }
        public List<string> Categories { get; set; }
        public bool IsInstance { get; set; } = true;
        public string ParameterGroup { get; set; }
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

                // Step 1: Get shared parameter file
                var sharedParamFile = app.Application.OpenSharedParameterFile();
                if (sharedParamFile == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "No shared parameter file is set. Please set a shared parameter file in Revit before adding shared parameters."
                    };
                    return;
                }

                // Step 2: Find or create the group in the shared parameter file
                DefinitionGroup defGroup = sharedParamFile.Groups.get_Item(GroupName);
                if (defGroup == null)
                    defGroup = sharedParamFile.Groups.Create(GroupName);

                // Step 3: Find or create the external definition
                ExternalDefinition externalDef = defGroup.Definitions.get_Item(ParameterName) as ExternalDefinition;
                if (externalDef == null)
                {
                    var creationOptions = new ExternalDefinitionCreationOptions(ParameterName, SpecTypeId.String.Text);
                    externalDef = defGroup.Definitions.Create(creationOptions) as ExternalDefinition;
                }

                if (externalDef == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"Failed to find or create definition '{ParameterName}' in group '{GroupName}'"
                    };
                    return;
                }

                // Step 4: Build CategorySet from category names
                var categorySet = new CategorySet();
                var unresolvedCategories = new List<string>();

                foreach (var categoryName in Categories)
                {
                    bool resolved = false;

                    // Try to parse as BuiltInCategory enum
                    foreach (BuiltInCategory bic in Enum.GetValues(typeof(BuiltInCategory)))
                    {
                        try
                        {
                            var cat = Category.GetCategory(doc, bic);
                            if (cat != null && string.Equals(cat.Name, categoryName, StringComparison.OrdinalIgnoreCase))
                            {
                                categorySet.Insert(cat);
                                resolved = true;
                                break;
                            }
                        }
                        catch
                        {
                            // Some BuiltInCategory values are not valid for GetCategory; skip them
                        }
                    }

                    if (!resolved)
                        unresolvedCategories.Add(categoryName);
                }

                if (categorySet.IsEmpty)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"None of the specified categories could be resolved: {string.Join(", ", Categories)}"
                    };
                    return;
                }

                // Step 5: Create the binding
                ElementBinding binding = IsInstance
                    ? (ElementBinding)app.Application.Create.NewInstanceBinding(categorySet)
                    : (ElementBinding)app.Application.Create.NewTypeBinding(categorySet);

                // Step 6: Insert into doc.ParameterBindings inside a transaction
                using (var transaction = new Transaction(doc, "Add Shared Parameter"))
                {
                    transaction.Start();

                    // Resolve the parameter group (BuiltInParameterGroup -> GroupTypeId)
                    ForgeTypeId groupTypeId = ResolveGroupTypeId(ParameterGroup);
                    bool inserted = doc.ParameterBindings.Insert(externalDef, binding, groupTypeId);

                    // If already bound, re-insert to update the binding
                    if (!inserted)
                        inserted = doc.ParameterBindings.ReInsert(externalDef, binding, groupTypeId);

                    transaction.Commit();

                    var warningMessage = unresolvedCategories.Count > 0
                        ? $" Warning: could not resolve categories: {string.Join(", ", unresolvedCategories)}."
                        : "";

                    Result = new AIResult<object>
                    {
                        Success = inserted,
                        Message = inserted
                            ? $"Shared parameter '{ParameterName}' added successfully.{warningMessage}"
                            : $"Failed to bind shared parameter '{ParameterName}' to the document.{warningMessage}",
                        Response = new
                        {
                            parameterName = ParameterName,
                            groupName = GroupName,
                            guid = externalDef.GUID.ToString(),
                            isInstance = IsInstance,
                            boundCategories = Categories.Except(unresolvedCategories).ToList(),
                            unresolvedCategories = unresolvedCategories
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to add shared parameter: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private static ForgeTypeId ResolveGroupTypeId(string parameterGroup)
        {
            if (string.IsNullOrEmpty(parameterGroup))
                return GroupTypeId.Data;

            // Map common display group names to GroupTypeId constants via reflection
            var groupTypeIdType = typeof(GroupTypeId);
            var properties = groupTypeIdType.GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            foreach (var prop in properties)
            {
                if (string.Equals(prop.Name, parameterGroup, StringComparison.OrdinalIgnoreCase))
                {
                    var value = prop.GetValue(null) as ForgeTypeId;
                    if (value != null) return value;
                }
            }

            return GroupTypeId.Data;
        }

        public string GetName() => "Add Shared Parameter";
    }
}
