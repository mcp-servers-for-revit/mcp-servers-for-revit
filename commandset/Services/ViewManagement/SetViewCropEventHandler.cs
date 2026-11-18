using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class SetViewCropEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long? ViewId { get; set; }
        public bool? CropActive { get; set; }
        public bool? CropVisible { get; set; }
        public List<long> ElementIds { get; set; } = new List<long>();
        public double OffsetMm { get; set; } = 300;
        public double? MinXMm { get; set; }
        public double? MinYMm { get; set; }
        public double? MaxXMm { get; set; }
        public double? MaxYMm { get; set; }
        public bool Reset { get; set; }

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 30000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var doc = uidoc.Document;

                // Get the target view
                View view = null;
                if (ViewId.HasValue)
                {
                    var viewId = RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(ViewId.Value);
                    view = doc.GetElement(viewId) as View;
                    if (view == null)
                        throw new InvalidOperationException($"View with ID {ViewId.Value} not found");
                }
                else
                {
                    view = uidoc.ActiveView;
                }

                if (view == null)
                    throw new InvalidOperationException("No valid view found");

                using (var transaction = new Transaction(doc, "Set View Crop"))
                {
                    transaction.Start();
                    try
                    {
                        // Reset: disable crop box and return
                        if (Reset)
                        {
                            view.CropBoxActive = false;
                            transaction.Commit();

                            Result = new AIResult<object>
                            {
                                Success = true,
                                Message = "Crop box reset (disabled)",
                                Response = new
                                {
                                    viewId = view.Id.GetValue(),
                                    viewName = view.Name,
                                    cropBoxActive = false,
                                    cropBoxVisible = view.CropBoxVisible
                                }
                            };
                            return;
                        }

                        // Fit crop box to elements bounding box + offset
                        if (ElementIds != null && ElementIds.Count > 0)
                        {
                            BoundingBoxXYZ combinedBox = null;
                            foreach (var eid in ElementIds)
                            {
                                var elementId = RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(eid);
                                var elem = doc.GetElement(elementId);
                                if (elem == null) continue;
                                var bb = elem.get_BoundingBox(view);
                                if (bb == null)
                                    bb = elem.get_BoundingBox(null);
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
                                throw new InvalidOperationException("Could not compute bounding box for specified elements");

                            double offset = OffsetMm / 304.8;
                            combinedBox.Min = new XYZ(
                                combinedBox.Min.X - offset,
                                combinedBox.Min.Y - offset,
                                combinedBox.Min.Z - offset);
                            combinedBox.Max = new XYZ(
                                combinedBox.Max.X + offset,
                                combinedBox.Max.Y + offset,
                                combinedBox.Max.Z + offset);

                            view.CropBoxActive = true;
                            view.CropBox = combinedBox;
                        }
                        // Explicit crop bounds in mm
                        else if (MinXMm.HasValue && MinYMm.HasValue && MaxXMm.HasValue && MaxYMm.HasValue)
                        {
                            var cropBox = new BoundingBoxXYZ();
                            cropBox.Min = new XYZ(
                                MinXMm.Value / 304.8,
                                MinYMm.Value / 304.8,
                                view.CropBox != null ? view.CropBox.Min.Z : 0);
                            cropBox.Max = new XYZ(
                                MaxXMm.Value / 304.8,
                                MaxYMm.Value / 304.8,
                                view.CropBox != null ? view.CropBox.Max.Z : 10);

                            view.CropBoxActive = true;
                            view.CropBox = cropBox;
                        }

                        // Toggle crop active
                        if (CropActive.HasValue)
                        {
                            view.CropBoxActive = CropActive.Value;
                        }

                        // Toggle crop visibility
                        if (CropVisible.HasValue)
                        {
                            view.CropBoxVisible = CropVisible.Value;
                        }

                        transaction.Commit();

                        // Build response with crop box info in mm
                        var currentCropBox = view.CropBox;
                        Result = new AIResult<object>
                        {
                            Success = true,
                            Message = "View crop updated successfully",
                            Response = new
                            {
                                viewId = view.Id.GetValue(),
                                viewName = view.Name,
                                cropBoxActive = view.CropBoxActive,
                                cropBoxVisible = view.CropBoxVisible,
                                cropBox = currentCropBox != null ? new
                                {
                                    min = new
                                    {
                                        x = Math.Round(currentCropBox.Min.X * 304.8, 2),
                                        y = Math.Round(currentCropBox.Min.Y * 304.8, 2),
                                        z = Math.Round(currentCropBox.Min.Z * 304.8, 2)
                                    },
                                    max = new
                                    {
                                        x = Math.Round(currentCropBox.Max.X * 304.8, 2),
                                        y = Math.Round(currentCropBox.Max.Y * 304.8, 2),
                                        z = Math.Round(currentCropBox.Max.Z * 304.8, 2)
                                    }
                                } : (object)null
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
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Set view crop failed: {ex.Message}"
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Set View Crop";
    }
}
