using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class SectionBoxFromSelectionEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<long> ElementIds { get; set; } = new List<long>();
        public bool UseCurrentSelection { get; set; } = true;
        public double OffsetMm { get; set; } = 1000; // offset in mm
        public bool DuplicateView { get; set; } = true;
        public string ViewName { get; set; } = "";
        public bool IsolateElements { get; set; } = false;

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 15000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc.Document;

                // Get element IDs - from selection or from parameter
                var elementIds = new List<ElementId>();
                if (UseCurrentSelection || ElementIds.Count == 0)
                {
                    elementIds = uidoc.Selection.GetElementIds().ToList();
                }
                else
                {
                    foreach (var id in ElementIds)
                    {
#if REVIT2024_OR_GREATER
                        elementIds.Add(new ElementId(id));
#else
                        elementIds.Add(new ElementId((int)id));
#endif
                    }
                }

                if (elementIds.Count == 0)
                    throw new InvalidOperationException("No elements selected or specified");

                // Calculate bounding box from all elements
                BoundingBoxXYZ combinedBox = null;
                foreach (var eid in elementIds)
                {
                    var elem = doc.GetElement(eid);
                    if (elem == null) continue;
                    var bb = elem.get_BoundingBox(null);
                    if (bb == null) continue;
                    if (combinedBox == null)
                    {
                        combinedBox = new BoundingBoxXYZ();
                        combinedBox.Min = bb.Min;
                        combinedBox.Max = bb.Max;
                    }
                    else
                    {
                        combinedBox.Min = new XYZ(
                            Math.Min(combinedBox.Min.X, bb.Min.X),
                            Math.Min(combinedBox.Min.Y, bb.Min.Y),
                            Math.Min(combinedBox.Min.Z, bb.Min.Z));
                        combinedBox.Max = new XYZ(
                            Math.Max(combinedBox.Max.X, bb.Max.X),
                            Math.Max(combinedBox.Max.Y, bb.Max.Y),
                            Math.Max(combinedBox.Max.Z, bb.Max.Z));
                    }
                }

                if (combinedBox == null)
                    throw new InvalidOperationException("Could not compute bounding box for selected elements");

                // Apply offset (convert mm to feet)
                double offsetFt = OffsetMm / 304.8;
                combinedBox.Min = new XYZ(combinedBox.Min.X - offsetFt, combinedBox.Min.Y - offsetFt, combinedBox.Min.Z - offsetFt);
                combinedBox.Max = new XYZ(combinedBox.Max.X + offsetFt, combinedBox.Max.Y + offsetFt, combinedBox.Max.Z + offsetFt);

                using (var transaction = new Transaction(doc, "Section Box From Selection"))
                {
                    transaction.Start();
                    try
                    {

                    View3D targetView = null;

                    if (DuplicateView)
                    {
                        // Create new 3D view
                        var vft = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);

                        if (vft == null)
                            throw new InvalidOperationException("No 3D view family type found");

                        targetView = View3D.CreateIsometric(doc, vft.Id);

                        string name = !string.IsNullOrEmpty(ViewName) ? ViewName : $"SectionBox - {DateTime.Now:yyyyMMdd_HHmmss}";
                        try { targetView.Name = name; } catch { /* name conflict, keep default */ }
                    }
                    else
                    {
                        // Use active 3D view
                        targetView = doc.ActiveView as View3D;
                        if (targetView == null)
                            throw new InvalidOperationException("Active view is not a 3D view. Set duplicateView=true or activate a 3D view.");
                    }

                    // Apply section box
                    targetView.SetSectionBox(combinedBox);

                    // Optionally isolate elements
                    if (IsolateElements)
                    {
                        targetView.IsolateElementsTemporary(elementIds);
                    }

                    transaction.Commit();

                    Result = new AIResult<object>
                    {
                        Success = true,
                        Message = $"Section box applied to {elementIds.Count} elements with {OffsetMm}mm offset",
                        Response = new
                        {
#if REVIT2024_OR_GREATER
                            viewId = targetView.Id.Value,
#else
                            viewId = targetView.Id.IntegerValue,
#endif
                            viewName = targetView.Name,
                            elementCount = elementIds.Count,
                            offsetMm = OffsetMm,
                            isolated = IsolateElements,
                            isNewView = DuplicateView,
                            sectionBox = new
                            {
                                min = new { x = combinedBox.Min.X * 304.8, y = combinedBox.Min.Y * 304.8, z = combinedBox.Min.Z * 304.8 },
                                max = new { x = combinedBox.Max.X * 304.8, y = combinedBox.Max.Y * 304.8, z = combinedBox.Max.Z * 304.8 }
                            }
                        }
                    };

                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Section box from selection failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Section Box From Selection";
    }
}
