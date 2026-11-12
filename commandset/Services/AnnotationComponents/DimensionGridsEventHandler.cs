using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.AnnotationComponents;

/// <summary>
///     Options for <see cref="DimensionGridsEventHandler" />. Offsets are in millimetres, measured
///     from the grid ends where the grid lines start, toward the building.
/// </summary>
public class DimensionGridsOptions
{
    public bool IncludeOverall { get; set; } = true;
    public bool IncludeBays { get; set; } = true;
    public double OverallOffsetMm { get; set; } = 600;
    public double BayOffsetMm { get; set; } = 1800;
    public bool SkipIfExisting { get; set; } = true;
}

/// <summary>
///     Groups the straight grids visible in the active plan view by direction and, for each group of
///     parallel grids, creates an overall dimension (first to last grid) and a bay string (every grid).
/// </summary>
public class DimensionGridsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
{
    private const double MmToFeet = 1.0 / 304.8;
    private const double ParallelTolerance = 1e-6;

    private readonly ManualResetEvent _resetEvent = new(false);
    private DimensionGridsOptions _options = new();

    public AIResult<object> Result { get; private set; }

    public void SetParameters(DimensionGridsOptions options)
    {
        _options = options ?? new DimensionGridsOptions();
        Result = null;
        _resetEvent.Reset();
    }

    public void Execute(UIApplication app)
    {
        try
        {
            Result = Run(app.ActiveUIDocument.Document);
        }
        catch (Exception ex)
        {
            Result = new AIResult<object> { Success = false, Message = $"Error dimensioning grids: {ex.Message}" };
        }
        finally
        {
            _resetEvent.Set();
        }
    }

    // Do not reset here: SetParameters already did, and resetting now could miss a completion
    // that happened between Raise() and this call.
    public bool WaitForCompletion(int timeoutMilliseconds = 10000) => _resetEvent.WaitOne(timeoutMilliseconds);

    public string GetName() => "Dimension Grids";

    private AIResult<object> Run(Document doc)
    {
        var view = doc.ActiveView;
        if (view is not ViewPlan)
            return Fail($"Active view '{view.Name}' is a {view.ViewType}; open a plan view (floor, ceiling, structural or area plan).");

        var grids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Grid)).Cast<Grid>().ToList();
        if (grids.Count < 2)
            return Fail($"Found {grids.Count} grid(s) in view '{view.Name}'; at least 2 are needed.");

        if (_options.SkipIfExisting)
        {
            var existing = FindExistingGridDimensions(doc, view);
            if (existing.Count > 0)
                return Fail(
                    $"View '{view.Name}' already has {existing.Count} grid dimension(s) (IDs {string.Join(", ", existing)}). " +
                    "Delete them or pass skipIfExisting=false.");
        }

        var skipped = new List<object>();
        var straight = new List<(Grid Grid, Line Line)>();
        foreach (var grid in grids)
        {
            var curves = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view);
            var curve = curves.Count > 0 ? curves[0] : grid.Curve;
            if (curve is Line line)
                straight.Add((grid, line));
            else
                skipped.Add(new { grid = grid.Name, reason = "Not a straight grid (arc or multi-segment)" });
        }

        // Group parallel grids (direction vectors equal or opposite).
        var groups = new List<List<(Grid Grid, Line Line)>>();
        foreach (var item in straight)
        {
            var group = groups.FirstOrDefault(g =>
                Math.Abs(Math.Abs(g[0].Line.Direction.DotProduct(item.Line.Direction)) - 1) < ParallelTolerance);
            if (group == null) groups.Add(new List<(Grid, Line)> { item });
            else group.Add(item);
        }

        var created = new List<object>();
        var z = view.GenLevel?.Elevation ?? 0;

        using var tx = new Transaction(doc, "Dimension Grids");
        tx.Start();

        foreach (var group in groups)
        {
            if (group.Count < 2)
            {
                skipped.Add(new { grid = group[0].Grid.Name, reason = "No parallel partner (angled grid)" });
                continue;
            }

            // d = along the grids, n = across them (the direction the dimension line runs).
            var d0 = group[0].Line.Direction;
            var d = new XYZ(d0.X, d0.Y, 0).Normalize();
            var n = new XYZ(-d.Y, d.X, 0);

            var sorted = group.OrderBy(g => g.Line.Origin.DotProduct(n)).ToList();
            var minAlong = group
                .SelectMany(g => new[] { g.Line.GetEndPoint(0), g.Line.GetEndPoint(1) })
                .Min(p => p.DotProduct(d));
            var a = sorted.First().Line.Origin.DotProduct(n);
            var b = sorted.Last().Line.Origin.DotProduct(n);

            Line DimLine(double offsetMm)
            {
                var along = minAlong + offsetMm * MmToFeet;
                var p0 = d * along + n * a;
                var p1 = d * along + n * b;
                return Line.CreateBound(new XYZ(p0.X, p0.Y, z), new XYZ(p1.X, p1.Y, z));
            }

            if (_options.IncludeOverall)
            {
                var refs = new ReferenceArray();
                refs.Append(new Reference(sorted.First().Grid));
                refs.Append(new Reference(sorted.Last().Grid));
                var dim = doc.Create.NewDimension(view, DimLine(_options.OverallOffsetMm), refs);
                created.Add(new
                {
                    id = IdValue(dim.Id),
                    kind = "overall",
                    grids = new[] { sorted.First().Grid.Name, sorted.Last().Grid.Name },
                    value = dim.ValueString
                });
            }

            if (_options.IncludeBays && sorted.Count > 2)
            {
                var refs = new ReferenceArray();
                foreach (var g in sorted) refs.Append(new Reference(g.Grid));
                var dim = doc.Create.NewDimension(view, DimLine(_options.BayOffsetMm), refs);
                var segments = new List<string>();
                foreach (DimensionSegment s in dim.Segments) segments.Add(s.ValueString);
                created.Add(new
                {
                    id = IdValue(dim.Id),
                    kind = "bays",
                    grids = sorted.Select(g => g.Grid.Name).ToArray(),
                    segments
                });
            }
        }

        if (created.Count == 0)
        {
            tx.RollBack();
            return new AIResult<object>
            {
                Success = false,
                Message = "No dimensions created: no group of 2+ parallel straight grids found.",
                Response = new { view = view.Name, created, skipped }
            };
        }

        tx.Commit();
        return new AIResult<object>
        {
            Success = true,
            Message = $"Created {created.Count} grid dimension(s) in view '{view.Name}'.",
            Response = new { view = view.Name, viewId = IdValue(view.Id), created, skipped }
        };
    }

    private static List<long> FindExistingGridDimensions(Document doc, View view)
    {
        return new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(Dimension)).Cast<Dimension>()
            .Where(dim => dim.References.Size > 1 &&
                          dim.References.Cast<Reference>().All(r => doc.GetElement(r.ElementId) is Grid))
            .Select(dim => IdValue(dim.Id))
            .ToList();
    }

    private static AIResult<object> Fail(string message) => new() { Success = false, Message = message };

    private static long IdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
