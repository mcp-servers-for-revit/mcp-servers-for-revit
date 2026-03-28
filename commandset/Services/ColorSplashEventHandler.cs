using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ColorSplashEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;

        /// <summary>
        /// Event wait object
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        /// <summary>
        /// Results data
        /// </summary>
        public object ColoringResults { get; private set; }

        private string _categoryName;
        private string _parameterName;
        private bool _useGradient;
        private JArray _customColors;
        private Random _random = new Random();

        /// <summary>
        /// Set parameters for color splash operation
        /// </summary>
        public void SetParameters(string categoryName, string parameterName, bool useGradient, JArray customColors)
        {
            _categoryName = categoryName;
            _parameterName = parameterName;
            _useGradient = useGradient;
            _customColors = customColors;
            _resetEvent.Reset();
        }

        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                // Get active view
                View activeView = doc.ActiveView;
                if (!activeView.CanUseTemporaryVisibilityModes())
                {
                    ColoringResults = new
                    {
                        success = false,
                        message = $"Cannot modify visibility settings in {activeView.ViewType} views"
                    };
                    return;
                }

                // Find category
                Category category = null;
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat.Name.Equals(_categoryName, StringComparison.OrdinalIgnoreCase))
                    {
                        category = cat;
                        break;
                    }
                }

                if (category == null)
                {
                    ColoringResults = new
                    {
                        success = false,
                        message = $"Category '{_categoryName}' not found"
                    };
                    return;
                }

                // Get elements of the category in the current view
                FilteredElementCollector collector = new FilteredElementCollector(doc, activeView.Id)
                    .OfCategoryId(category.Id)
                    .WhereElementIsNotElementType()
                    .WhereElementIsViewIndependent();

                ICollection<Element> elements = collector.ToElements();

                if (elements.Count == 0)
                {
                    ColoringResults = new
                    {
                        success = false,
                        message = $"No elements of category '{_categoryName}' found in the current view"
                    };
                    return;
                }

                // Group elements by parameter value
                Dictionary<string, List<ElementId>> parameterValueGroups = new Dictionary<string, List<ElementId>>();

                foreach (Element element in elements)
                {
                    // Check for type parameter first, then instance parameter
                    Parameter parameter = element.LookupParameter(_parameterName);

                    if (parameter == null)
                    {
                        // Try to get from element type
                        ElementId typeId = element.GetTypeId();
                        if (typeId != ElementId.InvalidElementId)
                        {
                            Element elementType = doc.GetElement(typeId);
                            if (elementType != null)
                            {
                                parameter = elementType.LookupParameter(_parameterName);
                            }
                        }
                    }

                    if (parameter != null && parameter.HasValue)
                    {
                        string paramValue = GetParameterValueAsString(parameter);

                        if (!parameterValueGroups.ContainsKey(paramValue))
                        {
                            parameterValueGroups[paramValue] = new List<ElementId>();
                        }

                        parameterValueGroups[paramValue].Add(element.Id);
                    }
                    else
                    {
                        // Handle elements with no parameter value or null value
                        string nullValueKey = "None";
                        if (!parameterValueGroups.ContainsKey(nullValueKey))
                        {
                            parameterValueGroups[nullValueKey] = new List<ElementId>();
                        }

                        parameterValueGroups[nullValueKey].Add(element.Id);
                    }
                }

                if (parameterValueGroups.Count == 0)
                {
                    ColoringResults = new
                    {
                        success = false,
                        message = $"No elements with parameter '{_parameterName}' found"
                    };
                    return;
                }

                // Generate colors for each group
                Dictionary<string, int[]> colorMap = GenerateColors(parameterValueGroups.Keys.ToList());

                // Apply colors to elements
                using (Transaction transaction = new Transaction(doc, "Color Splash"))
                {
                    transaction.Start();

                    // Get solid fill pattern
                    ElementId solidFillPatternId = GetSolidFillPatternId();

                    List<object> coloringResults = new List<object>();

                    foreach (var group in parameterValueGroups)
                    {
                        string paramValue = group.Key;
                        List<ElementId> elementIds = group.Value;
                        int[] rgb = colorMap[paramValue];

                        OverrideGraphicSettings overrides = new OverrideGraphicSettings();

                        // Set color
                        Color color = new Color((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);
                        overrides.SetProjectionLineColor(color);
                        overrides.SetSurfaceForegroundPatternColor(color);
                        overrides.SetCutForegroundPatternColor(color);

                        // Set solid fill pattern
                        if (solidFillPatternId != ElementId.InvalidElementId)
                        {
                            overrides.SetSurfaceForegroundPatternId(solidFillPatternId);
                            overrides.SetCutForegroundPatternId(solidFillPatternId);
                        }

                        // Apply overrides to each element
                        foreach (ElementId id in elementIds)
                        {
                            activeView.SetElementOverrides(id, overrides);
                        }

                        coloringResults.Add(new
                        {
                            parameterValue = paramValue,
                            count = elementIds.Count,
                            color = new { r = rgb[0], g = rgb[1], b = rgb[2] },
                            elementIds = elementIds.Select(id => id.GetValue().ToString()).ToList()
                        });
                    }

                    transaction.Commit();

                    ColoringResults = new
                    {
                        success = true,
                        totalElements = elements.Count,
                        coloredGroups = parameterValueGroups.Count,
                        results = coloringResults
                    };
                }
            }
            catch (Exception ex)
            {
                ColoringResults = new
                {
                    success = false,
                    message = $"Error: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set(); // Signal that operation is complete
            }
        }

        /// <summary>
        /// Wait for operation to complete
        /// </summary>
        /// <param name="timeoutMilliseconds">Timeout in milliseconds</param>
        /// <returns>Whether operation completed within timeout</returns>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
        return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// IExternalEventHandler.GetName implementation
        /// </summary>
        public string GetName()
        {
            return "Color Splash";
        }

        /// <summary>
        /// Get parameter value as string
        /// </summary>
        private string GetParameterValueAsString(Parameter parameter)
        {
            if (!parameter.HasValue)
                return "None";

            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    return parameter.AsValueString() ?? parameter.AsDouble().ToString();

                case StorageType.ElementId:
                    ElementId id = parameter.AsElementId();
                    if (id == ElementId.InvalidElementId)
                        return "None";

                    Element element = doc.GetElement(id);
                    return element?.Name ?? id.GetValue().ToString();
                case StorageType.Integer:
                    // For Revit 2023+ we should use ForgeTypeId approach
                    if (parameter.Definition is InternalDefinition internalDef)
                    {
                        try
                        {
                            // Try to get data type and check if it's a boolean
                            var paramTypeId = internalDef.GetDataType();
                            bool isBoolean = paramTypeId != null &&
                                           paramTypeId.Equals(SpecTypeId.Boolean.YesNo);

                            if (isBoolean)
                            {
                                return parameter.AsInteger() == 1 ? "True" : "False";
                            }
                        }
                        catch
                        {
                            // Fallback if any issues with newer API approach
                            // Check only known common boolean parameters to avoid errors
                            if (internalDef.BuiltInParameter == BuiltInParameter.IS_VISIBLE_PARAM)
                            {
                                return parameter.AsInteger() == 1 ? "True" : "False";
                            }
                        }
                    }

                    return parameter.AsValueString() ?? parameter.AsInteger().ToString();
                case StorageType.String:
                    return parameter.AsString() ?? "None";

                default:
                    return "None";
            }
        }

        /// <summary>
        /// Generate colors for each parameter value
        /// </summary>
        private Dictionary<string, int[]> GenerateColors(List<string> paramValues)
        {
            Dictionary<string, int[]> colorMap = new Dictionary<string, int[]>();

            // If custom colors are provided, use them
            if (_customColors != null && _customColors.Count > 0)
            {
                int colorIndex = 0;
                foreach (string value in paramValues)
                {
                    if (colorIndex < _customColors.Count)
                    {
                        JToken colorToken = _customColors[colorIndex];
                        if (colorToken["r"] != null && colorToken["g"] != null && colorToken["b"] != null)
                        {
                            colorMap[value] = new int[]
                            {
                                colorToken["r"].ToObject<int>(),
                                colorToken["g"].ToObject<int>(),
                                colorToken["b"].ToObject<int>()
                            };
                        }
                        else
                        {
                            colorMap[value] = GenerateRandomColor();
                        }
                    }
                    else
                    {
                        colorMap[value] = GenerateRandomColor();
                    }
                    colorIndex++;
                }
            }
            // If gradient is requested, generate gradient colors
            else if (_useGradient && paramValues.Count > 1)
            {
                // Start with blue, end with red
                int[] startColor = new int[] { 0, 0, 180 };
                int[] endColor = new int[] { 180, 0, 0 };

                for (int i = 0; i < paramValues.Count; i++)
                {
                    double ratio = (double)i / (paramValues.Count - 1);
                    int[] color = new int[]
                    {
                        (int)(startColor[0] + (endColor[0] - startColor[0]) * ratio),
                        (int)(startColor[1] + (endColor[1] - startColor[1]) * ratio),
                        (int)(startColor[2] + (endColor[2] - startColor[2]) * ratio)
                    };

                    colorMap[paramValues[i]] = color;
                }
            }
            // Otherwise, generate random colors
            else
            {
                foreach (string value in paramValues)
                {
                    colorMap[value] = GenerateRandomColor();
                }
            }

            return colorMap;
        }

        /// <summary>
        /// Generate a random RGB color
        /// </summary>
        private int[] GenerateRandomColor()
        {
            return new int[]
            {
                _random.Next(30, 200),  // R - keeping below 255 to ensure we don't exceed byte range
                _random.Next(30, 200),  // G
                _random.Next(30, 200)   // B
            };
        }

        /// <summary>
        /// Get solid fill pattern ID for overrides
        /// </summary>
        private ElementId GetSolidFillPatternId()
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfClass(typeof(FillPatternElement));

            foreach (FillPatternElement patternElement in collector)
            {
                FillPattern pattern = patternElement.GetFillPattern();
                if (pattern.IsSolidFill)
                {
                    return patternElement.Id;
                }
            }

            return ElementId.InvalidElementId;
        }
    }
}