using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
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

            // Resolve categories by display name, BuiltInCategory name, or OST_ prefix
            var categoryIds = new List<ElementId>();
            foreach (var catName in CategoryNames)
            {
                // Try display name match (localized)
                var cat = doc.Settings.Categories.Cast<Category>()
                    .FirstOrDefault(c => c.Name.Equals(catName, StringComparison.OrdinalIgnoreCase));

                // Try BuiltInCategory enum name (e.g., "Walls" → OST_Walls, or "OST_Walls")
                if (cat == null)
                {
                    string enumName = catName.StartsWith("OST_") ? catName : "OST_" + catName;
                    if (Enum.TryParse<BuiltInCategory>(enumName, true, out var bic))
                    {
                        cat = doc.Settings.Categories.Cast<Category>()
                            .FirstOrDefault(c => c.Id.Equals(new ElementId(bic)));
                    }
                }

                // Try partial/contains match as last resort
                if (cat == null)
                {
                    cat = doc.Settings.Categories.Cast<Category>()
                        .FirstOrDefault(c => c.Name.IndexOf(catName, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (cat != null)
                    categoryIds.Add(cat.Id);
            }

            if (categoryIds.Count == 0)
                throw new Exception($"No valid categories found for: {string.Join(", ", CategoryNames)}. Use localized names (e.g., 'Muri' in Italian) or BuiltInCategory names (e.g., 'OST_Walls').");

            ParameterFilterElement filter;
            using (var transaction = new Transaction(doc, "Create View Filter"))
            {
                transaction.Start();

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
            // Try to find the parameter by collecting an element from the categories
            foreach (var catId in categoryIds)
            {
                var elements = new FilteredElementCollector(doc)
                    .OfCategoryId(catId)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault();

                if (elements != null)
                {
                    foreach (Parameter param in elements.Parameters)
                    {
                        if (param.Definition.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                        {
                            return param.Id;
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
                    return ParameterFilterRuleFactory.CreateEqualsRule(paramId, value);
                case "DoesNotEqual":
                    if (int.TryParse(value, out int neqIntVal))
                        return ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, neqIntVal);
                    return ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value);
                case "Contains":
                    return ParameterFilterRuleFactory.CreateContainsRule(paramId, value);
                case "DoesNotContain":
                    return ParameterFilterRuleFactory.CreateNotContainsRule(paramId, value);
                case "BeginsWith":
                    return ParameterFilterRuleFactory.CreateBeginsWithRule(paramId, value);
                case "EndsWith":
                    return ParameterFilterRuleFactory.CreateEndsWithRule(paramId, value);
                case "HasValue":
                    return ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId);
                case "HasNoValue":
                    return ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId);
                case "IsGreaterThan":
                    if (int.TryParse(value, out int gtVal))
                        return ParameterFilterRuleFactory.CreateGreaterRule(paramId, gtVal);
                    return ParameterFilterRuleFactory.CreateGreaterRule(paramId, value);
                case "IsLessThan":
                    if (int.TryParse(value, out int ltVal))
                        return ParameterFilterRuleFactory.CreateLessRule(paramId, ltVal);
                    return ParameterFilterRuleFactory.CreateLessRule(paramId, value);
                default:
                    throw new ArgumentException($"Unknown filter rule type: {ruleType}");
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

        public string GetName() => "Create View Filter";
    }
}
