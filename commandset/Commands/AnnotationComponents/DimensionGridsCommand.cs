using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.AnnotationComponents;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.AnnotationComponents;

/// <summary>
///     Adds overall and bay-by-bay (sub) dimension strings to the grids in the active plan view.
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
                IncludeOverall = parameters?["includeOverall"]?.ToObject<bool>() ?? true,
                IncludeBays = parameters?["includeBays"]?.ToObject<bool>() ?? true,
                OverallOffsetMm = parameters?["overallOffset"]?.ToObject<double>() ?? 600,
                BayOffsetMm = parameters?["bayOffset"]?.ToObject<double>() ?? 1800,
                SkipIfExisting = parameters?["skipIfExisting"]?.ToObject<bool>() ?? true
            };

            if (!options.IncludeOverall && !options.IncludeBays)
                throw new ArgumentException("At least one of includeOverall or includeBays must be true");

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
}
