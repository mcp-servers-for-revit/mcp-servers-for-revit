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
    public class ManageProjectParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public string Action { get; set; } = "list";
        public string ParameterName { get; set; } = "";
        public string DataType { get; set; } = "Text";
        public string GroupUnder { get; set; } = "PG_IDENTITY_DATA";
        public bool IsInstance { get; set; } = true;
        public List<string> Categories { get; set; } = new List<string>();
        public bool IsShared { get; set; } = false;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters(string action, string parameterName, string dataType, string groupUnder,
            bool isInstance, List<string> categories, bool isShared)
        {
            Action = action ?? "list";
            ParameterName = parameterName ?? "";
            DataType = dataType ?? "Text";
            GroupUnder = groupUnder ?? "PG_IDENTITY_DATA";
            IsInstance = isInstance;
            Categories = categories ?? new List<string>();
            IsShared = isShared;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                switch (Action.ToLowerInvariant())
                {
                    case "list":
                        ExecuteList(doc);
                        break;
                    case "create":
                        ExecuteCreate(doc);
                        break;
                    case "delete":
                        ExecuteDelete(doc);
                        break;
                    case "modify":
                        ExecuteModify(doc);
                        break;
                    default:
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message = $"Unknown action '{Action}'. Valid actions: list, create, delete, modify."
                        };
                        break;
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Manage project parameters failed: {ex.Message}"
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        // -------------------------------------------------------------------------
        // LIST
        // -------------------------------------------------------------------------
        private void ExecuteList(Document doc)
        {
            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();
            iterator.Reset();

            var parameterList = new List<object>();

            while (iterator.MoveNext())
            {
                var definition = iterator.Key as InternalDefinition;
                if (definition == null) continue;

                var binding = iterator.Current as ElementBinding;
                if (binding == null) continue;

                string bindingType = binding is InstanceBinding ? "Instance" : "Type";

                var boundCategories = new List<string>();
                if (binding.Categories != null)
                {
                    foreach (Category cat in binding.Categories)
                        boundCategories.Add(cat.Name);
                }

                string storageTypeName = "Unknown";
                string groupName = "General";
#if REVIT2024_OR_GREATER
                try
                {
                    var specId = definition.GetDataType();
                    storageTypeName = specId != null ? specId.TypeId ?? "Unknown" : "Unknown";
                }
                catch { }
                try
                {
                    var groupId = definition.GetGroupTypeId();
                    groupName = groupId != null ? groupId.TypeId ?? "General" : "General";
                }
                catch { }
#else
                try
                {
                    // Use reflection to access ParameterType which may be inaccessible
                    var ptProp = definition.GetType().GetProperty("ParameterType");
                    if (ptProp != null) storageTypeName = ptProp.GetValue(definition)?.ToString() ?? "Unknown";
                }
                catch { }
                try
                {
                    var pgProp = definition.GetType().GetProperty("ParameterGroup");
                    if (pgProp != null) groupName = pgProp.GetValue(definition)?.ToString() ?? "General";
                }
                catch { }
#endif

                parameterList.Add(new
                {
                    name = definition.Name,
                    bindingType,
                    storageType = storageTypeName,
                    group = groupName,
                    boundCategories = boundCategories.OrderBy(c => c).ToList(),
                    categoryCount = boundCategories.Count
                });
            }

            // Sort by name — build a typed list instead of relying on dynamic
            parameterList.Sort((a, b) =>
            {
                // Use dictionary-based access for anonymous types
                string nameA = a.GetType().GetProperty("name")?.GetValue(a)?.ToString() ?? "";
                string nameB = b.GetType().GetProperty("name")?.GetValue(b)?.ToString() ?? "";
                return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            });

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Found {parameterList.Count} project parameter(s).",
                Response = new
                {
                    totalCount = parameterList.Count,
                    parameters = parameterList,
                    nextSteps = new[]
                    {
                        "Use action=\"create\" to add a new project parameter",
                        "Use action=\"modify\" with a parameterName and new categories[] to update bindings",
                        "Use action=\"delete\" with a parameterName to remove a parameter"
                    }
                }
            };
        }

        // -------------------------------------------------------------------------
        // CREATE
        // -------------------------------------------------------------------------
        private void ExecuteCreate(Document doc)
        {
            if (string.IsNullOrWhiteSpace(ParameterName))
            {
                Result = new AIResult<object> { Success = false, Message = "parameterName is required for create." };
                return;
            }

            if (Categories == null || Categories.Count == 0)
            {
                Result = new AIResult<object> { Success = false, Message = "At least one category is required for create." };
                return;
            }

            // Validate that parameter does not already exist
            var bindingMap = doc.ParameterBindings;
            var existingIterator = bindingMap.ForwardIterator();
            existingIterator.Reset();
            while (existingIterator.MoveNext())
            {
                var existingDef = existingIterator.Key as InternalDefinition;
                if (existingDef != null && existingDef.Name.Equals(ParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"A project parameter named '{ParameterName}' already exists. Use action=\"modify\" to change its bindings."
                    };
                    return;
                }
            }

            // Build CategorySet
            var categorySet = BuildCategorySet(doc, Categories, out var missingCategories);
            if (categorySet.IsEmpty)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"None of the specified categories could be found in this model. Missing: {string.Join(", ", missingCategories)}"
                };
                return;
            }

            using (var transaction = new Transaction(doc, "Create Project Parameter"))
            {
                transaction.Start();
                try
                {
                    string tempSpFile = System.IO.Path.GetTempFileName();
                    System.IO.File.WriteAllText(tempSpFile, "");

                    var app = doc.Application;
                    string previousSpFile = app.SharedParametersFilename;

                    try
                    {
                        app.SharedParametersFilename = tempSpFile;
                        var spFile = app.OpenSharedParameterFile();

                        var spGroup = spFile.Groups.Create("TempGroup");

#if REVIT2024_OR_GREATER
                        var specTypeId = ResolveForgeTypeId(DataType);
                        var options = new ExternalDefinitionCreationOptions(ParameterName, specTypeId)
                        {
                            Visible = true,
                            Description = $"Created via MCP on {DateTime.Now:yyyy-MM-dd}"
                        };
#else
                        // R23: Use reflection to construct ExternalDefinitionCreationOptions
                        // because ParameterType enum may be inaccessible in some NuGet wrappers
                        var paramTypeValue = ResolveParameterType(DataType);
                        var optionsCtor = typeof(ExternalDefinitionCreationOptions).GetConstructor(
                            new[] { typeof(string), typeof(Document).Assembly.GetType("Autodesk.Revit.DB.ParameterType") });
                        var options = (ExternalDefinitionCreationOptions)optionsCtor.Invoke(new object[] { ParameterName, paramTypeValue });
                        options.Visible = true;
                        options.Description = $"Created via MCP on {DateTime.Now:yyyy-MM-dd}";
#endif
                        var externalDef = spGroup.Definitions.Create(options) as ExternalDefinition;

                        ElementBinding newBinding = IsInstance
                            ? (ElementBinding)doc.Application.Create.NewInstanceBinding(categorySet)
                            : (ElementBinding)doc.Application.Create.NewTypeBinding(categorySet);

#if REVIT2024_OR_GREATER
                        var paramGroupId = ResolveGroupTypeId(GroupUnder);
                        bool inserted = doc.ParameterBindings.Insert(externalDef, newBinding, paramGroupId);
#else
                        // R23: Use reflection for Insert overload that takes BuiltInParameterGroup
                        var paramGroupValue = ResolveParameterGroup(GroupUnder);
                        var insertMethod = doc.ParameterBindings.GetType().GetMethod("Insert",
                            new[] { typeof(Definition), typeof(Binding), typeof(Document).Assembly.GetType("Autodesk.Revit.DB.BuiltInParameterGroup") });
                        bool inserted = insertMethod != null
                            ? (bool)insertMethod.Invoke(doc.ParameterBindings, new object[] { externalDef, newBinding, paramGroupValue })
                            : doc.ParameterBindings.Insert(externalDef, newBinding);
#endif

                        if (inserted)
                        {
                            transaction.Commit();
                            Result = new AIResult<object>
                            {
                                Success = true,
                                Message = $"Project parameter '{ParameterName}' created successfully as {(IsInstance ? "Instance" : "Type")} parameter.",
                                Response = new
                                {
                                    parameterName = ParameterName,
                                    dataType = DataType,
                                    group = GroupUnder,
                                    bindingType = IsInstance ? "Instance" : "Type",
                                    boundCategories = Categories,
                                    skippedCategories = missingCategories,
                                    nextSteps = new[]
                                    {
                                        "Use action=\"list\" to verify the parameter was created",
                                        "Use action=\"modify\" to add more categories later",
                                        $"Use set_element_parameters to assign values to '{ParameterName}'"
                                    }
                                }
                            };
                        }
                        else
                        {
                            transaction.RollBack();
                            Result = new AIResult<object>
                            {
                                Success = false,
                                Message = $"Failed to insert binding for '{ParameterName}'. The parameter may conflict with an existing definition."
                            };
                        }
                    }
                    finally
                    {
                        // Restore previous shared parameter file
                        app.SharedParametersFilename = previousSpFile ?? "";
                        try { System.IO.File.Delete(tempSpFile); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw new Exception($"Create failed: {ex.Message}", ex);
                }
            }
        }

        // -------------------------------------------------------------------------
        // DELETE
        // -------------------------------------------------------------------------
        private void ExecuteDelete(Document doc)
        {
            if (string.IsNullOrWhiteSpace(ParameterName))
            {
                Result = new AIResult<object> { Success = false, Message = "parameterName is required for delete." };
                return;
            }

            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();
            iterator.Reset();

            Definition targetDefinition = null;
            while (iterator.MoveNext())
            {
                var def = iterator.Key as InternalDefinition;
                if (def != null && def.Name.Equals(ParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    targetDefinition = def;
                    break;
                }
            }

            if (targetDefinition == null)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Project parameter '{ParameterName}' not found. Use action=\"list\" to see all available parameters."
                };
                return;
            }

            using (var transaction = new Transaction(doc, "Delete Project Parameter"))
            {
                transaction.Start();
                try
                {
                    bool removed = doc.ParameterBindings.Remove(targetDefinition);
                    if (removed)
                    {
                        transaction.Commit();
                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Project parameter '{ParameterName}' deleted successfully.",
                            Response = new
                            {
                                deletedParameter = ParameterName,
                                nextSteps = new[]
                                {
                                    "Use action=\"list\" to confirm the parameter was removed",
                                    "Note: existing element data for this parameter is no longer accessible"
                                }
                            }
                        };
                    }
                    else
                    {
                        transaction.RollBack();
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message = $"Failed to remove parameter '{ParameterName}'. It may be read-only or managed externally."
                        };
                    }
                }
                catch (Exception ex)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw new Exception($"Delete failed: {ex.Message}", ex);
                }
            }
        }

        // -------------------------------------------------------------------------
        // MODIFY
        // -------------------------------------------------------------------------
        private void ExecuteModify(Document doc)
        {
            if (string.IsNullOrWhiteSpace(ParameterName))
            {
                Result = new AIResult<object> { Success = false, Message = "parameterName is required for modify." };
                return;
            }

            if (Categories == null || Categories.Count == 0)
            {
                Result = new AIResult<object> { Success = false, Message = "At least one category is required for modify." };
                return;
            }

            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();
            iterator.Reset();

            Definition targetDefinition = null;
            ElementBinding existingBinding = null;

            while (iterator.MoveNext())
            {
                var def = iterator.Key as InternalDefinition;
                if (def != null && def.Name.Equals(ParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    targetDefinition = def;
                    existingBinding = iterator.Current as ElementBinding;
                    break;
                }
            }

            if (targetDefinition == null)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Project parameter '{ParameterName}' not found. Use action=\"list\" to see all available parameters."
                };
                return;
            }

            // Collect existing categories so we can merge
            var existingCategoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingBinding?.Categories != null)
            {
                foreach (Category cat in existingBinding.Categories)
                    existingCategoryNames.Add(cat.Name);
            }

            // Build a merged CategorySet: existing + newly requested
            var allCategoryNames = existingCategoryNames
                .Union(Categories, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mergedCategorySet = BuildCategorySet(doc, allCategoryNames, out var missingCategories);

            using (var transaction = new Transaction(doc, "Modify Project Parameter Bindings"))
            {
                transaction.Start();
                try
                {
                    // Rebuild binding with merged categories, preserving original binding type
                    bool keepInstance = existingBinding is InstanceBinding;
                    ElementBinding newBinding = keepInstance
                        ? (ElementBinding)doc.Application.Create.NewInstanceBinding(mergedCategorySet)
                        : (ElementBinding)doc.Application.Create.NewTypeBinding(mergedCategorySet);

                    bool reinserted = doc.ParameterBindings.ReInsert(targetDefinition, newBinding);

                    if (reinserted)
                    {
                        transaction.Commit();

                        var addedCategories = Categories
                            .Except(existingCategoryNames, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = $"Parameter '{ParameterName}' bindings updated. Added {addedCategories.Count} new category binding(s).",
                            Response = new
                            {
                                parameterName = ParameterName,
                                bindingType = keepInstance ? "Instance" : "Type",
                                previousCategories = existingCategoryNames.OrderBy(c => c).ToList(),
                                addedCategories = addedCategories.OrderBy(c => c).ToList(),
                                allCategories = allCategoryNames
                                    .Except(missingCategories, StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(c => c).ToList(),
                                skippedCategories = missingCategories,
                                nextSteps = new[]
                                {
                                    "Use action=\"list\" to verify the updated bindings",
                                    $"Use set_element_parameters to assign values to '{ParameterName}' on the newly bound categories"
                                }
                            }
                        };
                    }
                    else
                    {
                        transaction.RollBack();
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message = $"Failed to update bindings for '{ParameterName}'."
                        };
                    }
                }
                catch (Exception ex)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw new Exception($"Modify failed: {ex.Message}", ex);
                }
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------
        private CategorySet BuildCategorySet(Document doc, List<string> categoryNames, out List<string> missingCategories)
        {
            var categorySet = doc.Application.Create.NewCategorySet();
            missingCategories = new List<string>();

            foreach (var catName in categoryNames)
            {
                Category found = null;
                var cat = CategoryResolver.ResolveToCategory(doc, catName);
                if (cat != null && cat.AllowsBoundParameters)
                    found = cat;

                if (found != null)
                    categorySet.Insert(found);
                else
                    missingCategories.Add(catName);
            }

            return categorySet;
        }

#if REVIT2024_OR_GREATER
        private ForgeTypeId ResolveForgeTypeId(string dataType)
        {
            switch ((dataType ?? "Text").ToLowerInvariant())
            {
                case "integer":    return SpecTypeId.Int.Integer;
                case "number":     return SpecTypeId.Number;
                case "length":     return SpecTypeId.Length;
                case "area":       return SpecTypeId.Area;
                case "volume":     return SpecTypeId.Volume;
                case "yesno":      return SpecTypeId.Boolean.YesNo;
                case "url":        return SpecTypeId.String.Url;
                case "text":
                default:           return SpecTypeId.String.Text;
            }
        }

        private ForgeTypeId ResolveGroupTypeId(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return GroupTypeId.IdentityData;

            switch (groupName.ToUpperInvariant())
            {
                case "PG_IDENTITY_DATA":
                case "IDENTITY_DATA":   return GroupTypeId.IdentityData;
                case "PG_TEXT":
                case "TEXT":            return GroupTypeId.Text;
                case "PG_GENERAL":
                case "GENERAL":         return GroupTypeId.General;
                case "PG_CONSTRAINTS":
                case "CONSTRAINTS":     return GroupTypeId.Constraints;
                case "PG_DATA":
                case "DATA":            return GroupTypeId.Data;
                case "PG_GEOMETRY":
                case "GEOMETRY":        return GroupTypeId.Geometry;
                default:                return GroupTypeId.IdentityData;
            }
        }
#else
        // R23: ParameterType may be inaccessible in some NuGet wrappers.
        // Use reflection to create ExternalDefinitionCreationOptions with the correct ParameterType enum value.
        private object ResolveParameterType(string dataType)
        {
            // Resolve ParameterType via reflection since the enum may be internal in some wrappers
            var ptType = typeof(Document).Assembly.GetType("Autodesk.Revit.DB.ParameterType");
            if (ptType == null) return null;

            string enumName;
            switch ((dataType ?? "Text").ToLowerInvariant())
            {
                case "integer":  enumName = "Integer"; break;
                case "number":   enumName = "Number"; break;
                case "length":   enumName = "Length"; break;
                case "area":     enumName = "Area"; break;
                case "volume":   enumName = "Volume"; break;
                case "yesno":    enumName = "YesNo"; break;
                case "url":      enumName = "URL"; break;
                case "text":
                default:         enumName = "Text"; break;
            }

            return Enum.Parse(ptType, enumName);
        }

        private object ResolveParameterGroup(string groupName)
        {
            var bipgType = typeof(Document).Assembly.GetType("Autodesk.Revit.DB.BuiltInParameterGroup");
            if (bipgType == null) return null;

            string enumName;
            switch ((groupName ?? "").ToUpperInvariant())
            {
                case "PG_TEXT":
                case "TEXT":            enumName = "PG_TEXT"; break;
                case "PG_GENERAL":
                case "GENERAL":         enumName = "PG_GENERAL"; break;
                case "PG_CONSTRAINTS":
                case "CONSTRAINTS":     enumName = "PG_CONSTRAINTS"; break;
                case "PG_DATA":
                case "DATA":            enumName = "PG_DATA"; break;
                case "PG_GEOMETRY":
                case "GEOMETRY":        enumName = "PG_GEOMETRY"; break;
                case "PG_IDENTITY_DATA":
                case "IDENTITY_DATA":
                default:                enumName = "PG_IDENTITY_DATA"; break;
            }

            return Enum.Parse(bipgType, enumName);
        }
#endif

        public string GetName() => "Manage Project Parameters";
    }
}
