using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Workflow
{
    public class WorkflowClashReviewEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string CategoryA { get; set; } = "";
        public string CategoryB { get; set; } = "";
        public double Tolerance { get; set; } = 0;
        public bool CreateSectionBox { get; set; } = true;
        public object Result { get; private set; }

        public void SetParameters(string categoryA, string categoryB, double tolerance, bool createSectionBox)
        {
            CategoryA = categoryA;
            CategoryB = categoryB;
            Tolerance = tolerance;
            CreateSectionBox = createSectionBox;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 120000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                double tolFt = Tolerance / 304.8;

                // Find categories
                var catA = FindCategory(doc, CategoryA);
                var catB = FindCategory(doc, CategoryB);
                if (catA == BuiltInCategory.INVALID || catB == BuiltInCategory.INVALID)
                {
                    Result = new { success = false, error = $"Category not found: {(catA == BuiltInCategory.INVALID ? CategoryA : CategoryB)}" };
                    return;
                }

                // Collect elements
                var elementsA = new FilteredElementCollector(doc)
                    .OfCategory(catA).WhereElementIsNotElementType().ToList();
                var elementsB = new FilteredElementCollector(doc)
                    .OfCategory(catB).WhereElementIsNotElementType().ToList();

                // Detect intersections via bounding box
                var clashes = new List<object>();
                var clashingIds = new HashSet<ElementId>();

                foreach (var a in elementsA)
                {
                    var bbA = a.get_BoundingBox(null);
                    if (bbA == null) continue;

                    foreach (var b in elementsB)
                    {
                        var bbB = b.get_BoundingBox(null);
                        if (bbB == null) continue;

                        if (BoundingBoxesIntersect(bbA, bbB, tolFt))
                        {
                            clashes.Add(new
                            {
#if REVIT2024_OR_GREATER
                                elementA = a.Id.Value,
                                elementB = b.Id.Value,
#else
                                elementA = a.Id.IntegerValue,
                                elementB = b.Id.IntegerValue,
#endif
                                nameA = a.Name,
                                nameB = b.Name
                            });
                            clashingIds.Add(a.Id);
                            clashingIds.Add(b.Id);
                        }
                    }
                }

                // Isolate clashing elements in active view
                var activeView = doc.ActiveView;
                long? sectionBoxViewId = null;

                using (var tx = new Transaction(doc, "Workflow Clash Review"))
                {
                    tx.Start();

                    if (clashingIds.Count > 0)
                    {
                        // Isolate
                        try { activeView.IsolateElementsTemporary(clashingIds.ToList()); } catch { }

                        // Section box on 3D view
                        if (CreateSectionBox && activeView is View3D view3D)
                        {
                            var allBB = clashingIds
                                .Select(id => doc.GetElement(id)?.get_BoundingBox(null))
                                .Where(bb => bb != null).ToList();

                            if (allBB.Count > 0)
                            {
                                double minX = allBB.Min(b => b.Min.X) - 3;
                                double minY = allBB.Min(b => b.Min.Y) - 3;
                                double minZ = allBB.Min(b => b.Min.Z) - 3;
                                double maxX = allBB.Max(b => b.Max.X) + 3;
                                double maxY = allBB.Max(b => b.Max.Y) + 3;
                                double maxZ = allBB.Max(b => b.Max.Z) + 3;

                                var sectionBox = new BoundingBoxXYZ
                                {
                                    Min = new XYZ(minX, minY, minZ),
                                    Max = new XYZ(maxX, maxY, maxZ)
                                };
                                view3D.SetSectionBox(sectionBox);
#if REVIT2024_OR_GREATER
                                sectionBoxViewId = view3D.Id.Value;
#else
                                sectionBoxViewId = view3D.Id.IntegerValue;
#endif
                            }
                        }
                    }
                    tx.Commit();
                }

                Result = new
                {
                    success = true,
                    clashCount = clashes.Count,
                    clashes = clashes.Take(100).ToList(),
                    isolatedElementCount = clashingIds.Count,
                    sectionBoxViewId,
                    summary = $"Found {clashes.Count} clashes between {CategoryA} and {CategoryB}. " +
                        $"{clashingIds.Count} elements isolated in view."
                };
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private bool BoundingBoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b, double tolerance)
        {
            return a.Min.X - tolerance <= b.Max.X && a.Max.X + tolerance >= b.Min.X
                && a.Min.Y - tolerance <= b.Max.Y && a.Max.Y + tolerance >= b.Min.Y
                && a.Min.Z - tolerance <= b.Max.Z && a.Max.Z + tolerance >= b.Min.Z;
        }

        private BuiltInCategory FindCategory(Document doc, string name)
        {
            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
#if REVIT2024_OR_GREATER
                    return (BuiltInCategory)cat.Id.Value;
#else
                    return (BuiltInCategory)cat.Id.IntegerValue;
#endif
            }
            return BuiltInCategory.INVALID;
        }

        public string GetName() => "Workflow Clash Review";
    }
}
