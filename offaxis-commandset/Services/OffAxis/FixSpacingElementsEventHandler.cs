using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils.OffAxis;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.OffAxis
{
    public class FixSpacingElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<int> TargetIds { get; set; } = new List<int>();
        public double MaxMoveInches { get; set; } = 1.0;
        public bool PreviewOnly { get; set; } = false;

        public object Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        private struct Geo2D
        {
            public double P;
            public double Ang;
            public double Ux;
            public double Uy;
        }

        private static Geo2D ComputeG2(XYZ a, XYZ b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / len, uy = dy / len;
            double pp = (a.X + b.X) / 2.0 * (-uy) + (a.Y + b.Y) / 2.0 * ux;
            double ang = Math.Atan2(dy, dx) * OffAxisGeometryUtils.RadToDeg;
            if (ang < 0) ang += 180.0;
            return new Geo2D { P = pp, Ang = ang, Ux = ux, Uy = uy };
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var document = app.ActiveUIDocument?.Document;
                if (document == null)
                {
                    Result = new { Error = "No active document" };
                    return;
                }

                if (document.IsReadOnlyFile || document.IsReadOnly || document.IsLinked)
                {
                    Result = new { Error = "Document is read-only or linked" };
                    return;
                }

                double gridFt = 0.25 / 12.0;
                double dirTolDeg = 0.1;
                double snapEpsIn = 0.0012;

                var gridRecs = new List<(double p, double ang)>();
                foreach (var gr in new FilteredElementCollector(document).OfClass(typeof(Grid)).WhereElementIsNotElementType().Cast<Grid>())
                {
                    if (gr.Curve is Line lx)
                    {
                        var qx = ComputeG2(lx.GetEndPoint(0), lx.GetEndPoint(1));
                        gridRecs.Add((qx.P, qx.Ang));
                    }
                }

                double GetPhi(double pp, double aa)
                {
                    var cand = gridRecs.Where(gm =>
                    {
                        double dd = Math.Abs(gm.ang - aa);
                        if (dd > 90.0) dd = 180.0 - dd;
                        return dd <= dirTolDeg;
                    }).ToList();
                    if (cand.Count == 0) return 0;
                    var near = cand.OrderBy(mm => Math.Abs(mm.p - pp)).First();
                    return near.p - gridFt * Math.Floor(near.p / gridFt);
                }

                double GetDeltaIn(double pp, double aa)
                {
                    double ph = GetPhi(pp, aa);
                    double sn = ph + gridFt * Math.Round((pp - ph) / gridFt);
                    return (pp - sn) * 12.0;
                }

                var dims = new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
                var constrainedIds = new HashSet<int>();
                foreach (var dim in dims)
                {
                    try
                    {
                        var refs = dim.References;
                        if (refs != null)
                        {
                            for (int i = 0; i < refs.Size; i++)
                            {
                                var elem = document.GetElement(refs.get_Item(i));
                                if (elem != null) constrainedIds.Add(elem.Id.GetIntValue());
                            }
                        }
                    }
                    catch { }
                }

                var log = new List<object>();
                var preprocessor = new SilentFailuresPreprocessor();
                int fixedCnt = 0, okCnt = 0, skipCnt = 0, failCnt = 0;

                foreach (int id in TargetIds)
                {
                    var el = document.GetElement(new ElementId(id));
                    if (el == null) { log.Add(new { Id = id, Status = "missing" }); skipCnt++; continue; }
                    if (el.Pinned) { log.Add(new { Id = id, Status = "pinned" }); skipCnt++; continue; }
                    if (constrainedIds.Contains(id)) { log.Add(new { Id = id, Status = "dim-constrained" }); skipCnt++; continue; }

                    Line ln = null;
                    if (el.Location is LocationCurve lc && lc.Curve is Line ll) ln = ll;
                    if (ln == null) { log.Add(new { Id = id, Status = "no-line" }); skipCnt++; continue; }

                    XYZ p0 = ln.GetEndPoint(0), p1 = ln.GetEndPoint(1);
                    var q = ComputeG2(p0, p1);
                    double dIn0 = GetDeltaIn(q.P, q.Ang);
                    double dIn = dIn0;
                    string status = "";
                    int passes = 0;

                    if (PreviewOnly)
                    {
                        log.Add(new
                        {
                            Id = id,
                            Category = el.Category?.Name,
                            DeltaInBefore = Math.Round(dIn0, 4),
                            Status = "PREVIEW",
                            Preview = true
                        });
                        continue;
                    }

                    for (int pass = 0; pass < 3; pass++)
                    {
                        passes = pass + 1;
                        if (Math.Abs(dIn) < snapEpsIn)
                        {
                            if (pass == 0) { okCnt++; status = "already-on-lattice"; }
                            else { fixedCnt++; status = "FIXED"; }
                            break;
                        }
                        if (Math.Abs(dIn) > MaxMoveInches) { failCnt++; status = "EXCEEDS_MAX_MOVE"; break; }

                        double ph = GetPhi(q.P, q.Ang);
                        double sn = ph + gridFt * Math.Round((q.P - ph) / gridFt);
                        double moveFt = sn - q.P;
                        XYZ nv = new XYZ(-q.Uy, q.Ux, 0);
                        Line nl = Line.CreateBound(p0 + nv * moveFt, p1 + nv * moveFt);

                        bool committed = false;
                        try
                        {
                            using (Transaction tx = new Transaction(document, "SpacingLive " + id))
                            {
                                tx.Start();
                                var fo = tx.GetFailureHandlingOptions();
                                fo.SetFailuresPreprocessor(preprocessor);
                                fo.SetClearAfterRollback(true);
                                fo.SetForcedModalHandling(false);
                                tx.SetFailureHandlingOptions(fo);

                                ((LocationCurve)el.Location).Curve = nl;
                                committed = tx.Commit() == TransactionStatus.Committed;
                            }
                        }
                        catch { committed = false; }

                        if (!committed) { failCnt++; status = "FAIL_ROLLBACK"; break; }

                        if (((LocationCurve)el.Location).Curve is Line ln2)
                        {
                            p0 = ln2.GetEndPoint(0);
                            p1 = ln2.GetEndPoint(1);
                            q = ComputeG2(p0, p1);
                            dIn = GetDeltaIn(q.P, q.Ang);
                        }
                        else break;
                    }

                    if (string.IsNullOrEmpty(status)) { fixedCnt++; status = "FIXED-3pass"; }
                    log.Add(new
                    {
                        Id = id,
                        Category = el.Category?.Name,
                        DeltaInBefore = Math.Round(dIn0, 4),
                        DeltaInAfter = Math.Round(dIn, 4),
                        Passes = passes,
                        Status = status
                    });
                }

                Result = new
                {
                    Checked = TargetIds.Count,
                    Fixed = fixedCnt,
                    AlreadyOk = okCnt,
                    Skipped = skipCnt,
                    Failed = failCnt,
                    PreviewOnly = PreviewOnly,
                    Log = log,
                    FailuresHandled = preprocessor.Log.Count > 0 ? preprocessor.Log : null
                };
            }
            catch (Exception ex)
            {
                Result = new { Error = ex.Message, StackTrace = ex.StackTrace };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Fix Spacing Elements";
    }
}
