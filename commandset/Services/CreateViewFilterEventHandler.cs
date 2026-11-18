using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class CreateViewFilterEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Action { get; set; } = "list";
        public string FilterName { get; set; } = "";
        public List<string> CategoryNames { get; set; } = new List<string>();
        public string ParameterName { get; set; } = "";
        public string FilterRule { get; set; } = "";
        public string FilterValue { get; set; } = "";
        public long ViewId { get; set; } = 0;
        public int ColorR { get; set; } = -1;
        public int ColorG { get; set; } = -1;
        public int ColorB { get; set; } = -1;
        public bool IsVisible { get; set; } = true;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string action, string filterName, List<string> categoryNames, string parameterName, string filterRule, string filterValue, long viewId, int colorR, int colorG, int colorB, bool isVisible)
        {
            Action = action ?? "list";
            FilterName = filterName ?? "";
            CategoryNames = categoryNames ?? new List<string>();
            ParameterName = parameterName ?? "";
            FilterRule = filterRule ?? "";
            FilterValue = filterValue ?? "";
            ViewId = viewId;
            ColorR = colorR;
            ColorG = colorG;
            ColorB = colorB;
            IsVisible = isVisible;
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

                switch (Action.ToLower())
                {
                    case "create":
                        ExecuteCreate(doc);
                        break;
                    case "apply":
                        ExecuteApply(doc, app);
                        break;
                    case "list":
                        ExecuteList(doc);
                        break;
                    default:
                        throw new ArgumentException($"Unknown action: {Action}");
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"View filter operation failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void ExecuteCreate(Document doc)
        {
            if (string.IsNullOrEmpty(FilterName))
                throw new ArgumentException("filterName is required for 'create' action");
            if (CategoryNames.Count == 0)
                throw new ArgumentException("categoryNames is required for 'create' action");

            // Resolve categories using shared CategoryResolver (language-independent)
            var categoryIds = CategoryNames
                .Select(catName => CategoryResolver.ResolveToId(doc, catName))
                .Where(id => id != null)
                .ToList();

            if (categoryIds.Count == 0)
                throw new Exception($"No valid categories found for: {string.Join(", ", CategoryNames)}. Use localized names (e.g., 'Muri' in Italian) or BuiltInCategory names (e.g., 'OST_Walls').");

            ParameterFilterElement filter;
            using (var transaction = new Transaction(doc, "Create View Filter"))
            {
                transaction.Start();
                try
                {
                    if (!string.IsNullOrEmpty(ParameterName) && !string.IsNullOrEmpty(FilterRule))
                    {
                        // Find the parameter to filter by
                        var param = FindParameterInCategories(doc, categoryIds, ParameterName);
                        if (param == null)
                            throw new Exception($"Parameter '{ParameterName}' not found in specified categories");

                        var rule = CreateFilterRule(param, FilterRule, FilterValue);
                        var elementFilter = new ElementParameterFilter(rule);
                        filter = ParameterFilterElement.Create(doc, FilterName, categoryIds, elementFilter);
                    }
                    else
                    {
                        filter = ParameterFilterElement.Create(doc, FilterName, categoryIds);
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
                Success = true,
                Message = $"Successfully created view filter '{FilterName}'",
                Response = new
                {
#if REVIT2024_OR_GREATER
                    filterId = filter.Id.Value,
#else
                    filterId = filter.Id.IntegerValue,
#endif
                    filterName = filter.Name
                }
            };
        }

        private void ExecuteApply(Document doc, UIApplication app)
        {
            if (string.IsNullOrEmpty(FilterName))
                throw new ArgumentException("filterName is required for 'apply' action");

            // Find the filter
            var filter = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .FirstOrDefault(f => f.Name.Equals(FilterName, StringComparison.OrdinalIgnoreCase));

            if (filter == null)
                throw new Exception($"View filter '{FilterName}' not found");

            // Get target view
            View view;
            if (ViewId != 0)
                view = doc.GetElement(ToElementId(ViewId)) as View;
            else
                view = app.ActiveUIDocument.ActiveView;

            if (view == null)
                throw new Exception("Target view not found");

            using (var transaction = new Transaction(doc, "Apply View Filter"))
            {
                transaction.Start();
                try
                {
                    view.AddFilter(filter.Id);
                    view.SetFilterVisibility(filter.Id, IsVisible);

                    // Apply color override if specified
                    if (ColorR >= 0 && ColorG >= 0 && ColorB >= 0)
                    {
                        var overrideSettings = new OverrideGraphicSettings();
                        var color = new Color((byte)ColorR, (byte)ColorG, (byte)ColorB);
                        overrideSettings.SetProjectionLineColor(color);
                        overrideSettings.SetSurfaceForegroundPatternColor(color);
                        view.SetFilterOverrides(filter.Id, overrideSettings);
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
                Success = true,
                Message = $"Successfully applied filter '{FilterName}' to view '{view.Name}'"
            };
        }

        private void ExecuteList(Document doc)
        {
            var filters = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .ToList();

            var result = new List<object>();
            foreach (var filter in filters)
            {
                result.Add(new
                {
#if REVIT2024_OR_GREATER
                    id = filter.Id.Value,
#else
                    id = filter.Id.IntegerValue,
#endif
                    name = filter.Name,
                    categoryCount = filter.GetCategories().Count
                });
            }

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Found {result.Count} view filters",
                Response = result
            };
        }

        private ElementId FindParameterInCategories(Document doc, List<ElementId> categoryIds, string paramName)
        {
            foreach (var catId in categoryIds)
            {
                // Search instance parameters
                var instance = new FilteredElementCollector(doc)
                    .OfCategoryId(catId)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault();

                if (instance != null)
                {
                    foreach (Parameter param in instance.Parameters)
                    {
                        if (param.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                            return param.Id;
                    }

                    // Search type parameters
                    var typeId = instance.GetTypeId();
                    if (typeId != null && typeId != ElementId.InvalidElementId)
                    {
                        var typeElem = doc.GetElement(typeId);
                        if (typeElem != null)
                        {
                            foreach (Parameter param in typeElem.Parameters)
                            {
                                if (param.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                                    return param.Id;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private FilterRule CreateFilterRule(ElementId paramId, string ruleType, string value)
        {
            switch (ruleType)
            {
                case "Equals":
                    if (int.TryParse(value, out int eqIntVal))
                        return ParameterFilterRuleFactory.CreateEqualsRule(paramId, eqIntVal);
                    return EqualsRule(paramId, value);
                case "DoesNotEqual":
                    if (int.TryParse(value, out int neqIntVal))
                        return ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, neqIntVal);
                    return NotEqualsRule(paramId, value);
                case "Contains":
                    return ContainsRule(paramId, value);
                case "DoesNotContain":
                    return NotContainsRule(paramId, value);
                case "BeginsWith":
                    return BeginsWithRule(paramId, value);
                case "EndsWith":
                    return EndsWithRule(paramId, value);
                case "HasValue":
                    return ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId);
                case "HasNoValue":
                    return ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId);
                case "IsGreaterThan":
                    if (int.TryParse(value, out int gtVal))
                        return ParameterFilterRuleFactory.CreateGreaterRule(paramId, gtVal);
                    return GreaterRule(paramId, value);
                case "IsLessThan":
                    if (int.TryParse(value, out int ltVal))
                        return ParameterFilterRuleFactory.CreateLessRule(paramId, ltVal);
                    return LessRule(paramId, value);
                default:
                    throw new ArgumentException($"Unknown filter rule type: {ruleType}");
            }
        }

        // Revit 2023 removed the trailing caseSensitive argument from the string-based
        // rule factories (string comparison became case-insensitive). Revit 2020-2022
        // still require it, so pass false there to keep the same behaviour.
#if REVIT2023_OR_GREATER
        private static FilterRule EqualsRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateEqualsRule(paramId, value);
        private static FilterRule NotEqualsRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value);
        private static FilterRule ContainsRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateContainsRule(paramId, value);
        private static FilterRule NotContainsRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateNotContainsRule(paramId, value);
        private static FilterRule BeginsWithRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateBeginsWithRule(paramId, value);
        private static FilterRule EndsWithRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateEndsWithRule(paramId, value);
        private static FilterRule GreaterRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateGreaterRule(paramId, value);
        private static FilterRule LessRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateLessRule(paramId, value);
#else
        private static FilterRule EqualsRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateEqualsRule(paramId, value, false);
        private static FilterRule NotEqualsRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value, false);
        private static FilterRule ContainsRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateContainsRule(paramId, value, false);
        private static FilterRule NotContainsRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateNotContainsRule(paramId, value, false);
        private static FilterRule BeginsWithRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateBeginsWithRule(paramId, value, false);
        private static FilterRule EndsWithRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateEndsWithRule(paramId, value, false);
        private static FilterRule GreaterRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateGreaterRule(paramId, value, false);
        private static FilterRule LessRule(ElementId paramId, string value) => ParameterFilterRuleFactory.CreateLessRule(paramId, value, false);
#endif

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Create View Filter";
    }
}
