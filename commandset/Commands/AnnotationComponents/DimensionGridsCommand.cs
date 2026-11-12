using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.AnnotationComponents;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.AnnotationComponents;

/// <summary>
///     Adds overall and bay-by-bay (sub) dimension strings to the grids in the active plan view,
///     at the screen sides the caller chooses.
/// </summary>
public class DimensionGridsCommand : ExternalEventCommandBase
{
    public DimensionGridsCommand(UIApplication uiApp)
        : base(new DimensionGridsEventHandler(), uiApp)
    {
    }

    private DimensionGridsEventHandler _handler => (DimensionGridsEventHandler)Handler;

    public override string CommandName => "dimension_grids";

    public override object Execute(JObject parameters, string requestId)
    {
        try
        {
            var options = new DimensionGridsOptions
            {
                VerticalGridsAt = RequiredChoice(parameters, "verticalGridsAt", DimensionGridsOptions.VerticalChoices),
                HorizontalGridsAt = RequiredChoice(parameters, "horizontalGridsAt", DimensionGridsOptions.HorizontalChoices),
                IncludeOverall = parameters?["includeOverall"]?.ToObject<bool>() ?? true,
                IncludeBays = parameters?["includeBays"]?.ToObject<bool>() ?? true,
                OverallOffsetMm = parameters?["overallOffset"]?.ToObject<double>() ?? 10,
                BayOffsetMm = parameters?["bayOffset"]?.ToObject<double>() ?? 18,
                SkipIfExisting = parameters?["skipIfExisting"]?.ToObject<bool>() ?? true
            };

            if (!options.IncludeOverall && !options.IncludeBays)
                throw new ArgumentException("At least one of includeOverall or includeBays must be true");
            if (options.OverallOffsetMm < 0 || options.BayOffsetMm < 0)
                throw new ArgumentException("Offsets must not be negative");

            _handler.SetParameters(options);

            if (RaiseAndWaitForCompletion(30000))
                return _handler.Result;
            throw new TimeoutException("Grid dimensioning timed out (is Revit busy or showing a dialog?)");
        }
        catch (Exception ex)
        {
            throw new Exception($"Error dimensioning grids: {ex.Message}", ex);
        }
    }

    /// <summary>Reads a required string input and checks it is one of the allowed values.</summary>
    private static string RequiredChoice(JObject parameters, string name, string[] allowed)
    {
        var value = parameters?[name]?.ToString()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException(
                $"'{name}' is required ({string.Join(", ", allowed)}). Ask the user where the dimensions should go.");
        if (!allowed.Contains(value))
            throw new ArgumentException($"'{name}' must be one of {string.Join(", ", allowed)} (got '{value}')");
        return value;
    }
}
