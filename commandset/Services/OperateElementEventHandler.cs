using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPCommandSet.Models.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Services
{
    public class OperateElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private UIApplication uiApp;
        private UIDocument uiDoc => uiApp.ActiveUIDocument;
        private Document doc => uiDoc.Document;
        private Autodesk.Revit.ApplicationServices.Application app => uiApp.Application;

        /// <summary>
        /// Event wait handle
        /// </summary>
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        /// <summary>
        /// Creation data (input data)
        /// </summary>
        public OperationSetting OperationData { get; private set; }
        /// <summary>
        /// Execution result (output data)
        /// </summary>
        public AIResult<string> Result { get; private set; }

        /// <summary>
        /// Set creation parameters
        /// </summary>
        public void SetParameters(OperationSetting data)
        {
            OperationData = data;
            _resetEvent.Reset();
        }
        public void Execute(UIApplication uiapp)
        {
            uiApp = uiapp;

            try
            {
                bool result = ExecuteElementOperation(uiDoc, OperationData);

                Result = new AIResult<string>
                {
                    Success = true,
                    Message = $"Operation executed successfully",
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<string>
                {
                    Success = false,
                    Message = $"Error operating on elements: {ex.Message}",
                };
            }
            finally
            {
                _resetEvent.Set(); // Notify waiting thread that operation is complete
            }
        }

        /// <summary>
        /// Wait for creation to complete
        /// </summary>
        /// <param name="timeoutMilliseconds">Timeout duration (milliseconds)</param>
        /// <returns>Whether the operation completed before timeout</returns>
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        /// <summary>
        /// IExternalEventHandler.GetName implementation
        /// </summary>
        public string GetName()
        {
            return "Operate Element";
        }

        /// <summary>
        /// Execute the corresponding element operation based on operation settings
        /// </summary>
        /// <param name="uidoc">Current UI document</param>
        /// <param name="setting">Operation settings</param>
        /// <returns>Whether the operation was successful</returns>
        public static bool ExecuteElementOperation(UIDocument uidoc, OperationSetting setting)
        {
            // Check parameter validity
            if (uidoc == null || uidoc.Document == null || setting == null || setting.ElementIds == null ||
                (setting.ElementIds.Count == 0 && setting.Action.ToLower() != "resetisolate"))
                throw new Exception("Invalid parameters: document is null or no elements specified for operation");

            Document doc = uidoc.Document;

            // Convert int element IDs to ElementId type
            ICollection<ElementId> elementIds = setting.ElementIds.Select(id => new ElementId(id)).ToList();

            // Parse operation type
            ElementOperationType action;
            if (!Enum.TryParse(setting.Action, true, out action))
            {
                throw new Exception($"Unsupported operation type:{setting.Action}");
            }

            // Execute different operations based on operation type
            switch (action)
            {
                case ElementOperationType.Select:
                    // Select elements
                    uidoc.Selection.SetElementIds(elementIds);
                    return true;

                case ElementOperationType.SelectionBox:
                    // Create section box in 3D view

                    // Check if current view is a 3D view
                    View3D targetView;

                    if (doc.ActiveView is View3D)
                    {
                        // If current view is a 3D view, create section box in current view
                        targetView = doc.ActiveView as View3D;
                    }
                    else
                    {
                        // If current view is not a 3D view, find default 3D view
                        FilteredElementCollector collector = new FilteredElementCollector(doc);
                        collector.OfClass(typeof(View3D));

                        // Try to find default 3D view or any other available 3D view
                        targetView = collector
                            .Cast<View3D>()
                            .FirstOrDefault(v => !v.IsTemplate && !v.IsLocked && (v.Name.Contains("{3D}") || v.Name.Contains("Default 3D")));

                        if (targetView == null)
                        {
                            // If no suitable 3D view found, throw exception
                            throw new Exception("Cannot find a suitable 3D view for creating section box");
                        }

                        // Activate the 3D view
                        uidoc.ActiveView = targetView;
                    }

                    // Calculate bounding box of selected elements
                    BoundingBoxXYZ boundingBox = null;

                    foreach (ElementId id in elementIds)
                    {
                        Element elem = doc.GetElement(id);
                        BoundingBoxXYZ elemBox = elem.get_BoundingBox(null);

                        if (elemBox != null)
                        {
                            if (boundingBox == null)
                            {
                                boundingBox = new BoundingBoxXYZ
                                {
                                    Min = new XYZ(elemBox.Min.X, elemBox.Min.Y, elemBox.Min.Z),
                                    Max = new XYZ(elemBox.Max.X, elemBox.Max.Y, elemBox.Max.Z)
                                };
                            }
                            else
                            {
                                // Expand bounding box to include current element
                                boundingBox.Min = new XYZ(
                                    Math.Min(boundingBox.Min.X, elemBox.Min.X),
                                    Math.Min(boundingBox.Min.Y, elemBox.Min.Y),
                                    Math.Min(boundingBox.Min.Z, elemBox.Min.Z));

                                boundingBox.Max = new XYZ(
                                    Math.Max(boundingBox.Max.X, elemBox.Max.X),
                                    Math.Max(boundingBox.Max.Y, elemBox.Max.Y),
                                    Math.Max(boundingBox.Max.Z, elemBox.Max.Z));
                            }
                        }
                    }

                    if (boundingBox == null)
                    {
                        throw new Exception("Cannot create bounding box for selected elements");
                    }

                    // Increase bounding box size to be slightly larger than elements
                    double offset = 1.0; // 1 foot offset
                    boundingBox.Min = new XYZ(boundingBox.Min.X - offset, boundingBox.Min.Y - offset, boundingBox.Min.Z - offset);
                    boundingBox.Max = new XYZ(boundingBox.Max.X + offset, boundingBox.Max.Y + offset, boundingBox.Max.Z + offset);

                    // Enable and set section box in 3D view
                    using (Transaction trans = new Transaction(doc, "Create Section Box"))
                    {
                        trans.Start();
                        targetView.IsSectionBoxActive = true;
                        targetView.SetSectionBox(boundingBox);
                        trans.Commit();
                    }

                    // Move to view center
                    uidoc.ShowElements(elementIds);
                    return true;

                case ElementOperationType.SetColor:
                    // Set elements to specified color
                    using (Transaction trans = new Transaction(doc, "Set Element Color"))
                    {
                        trans.Start();
                        SetElementsColor(doc, elementIds, setting.ColorValue);
                        trans.Commit();
                    }
                    // Scroll to make these elements visible
                    uidoc.ShowElements(elementIds);
                    return true;


                case ElementOperationType.SetTransparency:
                    // Set element transparency in current view
                    using (Transaction trans = new Transaction(doc, "Set Element Transparency"))
                    {
                        trans.Start();

                        // Create graphic override settings object
                        OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();

                        // Set transparency (ensure value is in 0-100 range)
                        int transparencyValue = Math.Max(0, Math.Min(100, setting.TransparencyValue));

                        // Set surface transparency
                        overrideSettings.SetSurfaceTransparency(transparencyValue);

                        // Apply transparency settings to each element
                        foreach (ElementId id in elementIds)
                        {
                            doc.ActiveView.SetElementOverrides(id, overrideSettings);
                        }

                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.Delete:
                    // Delete elements (requires transaction)
                    using (Transaction trans = new Transaction(doc, "Delete Elements"))
                    {
                        trans.Start();
                        doc.Delete(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.Hide:
                    // Hide elements (requires active view and transaction)
                    using (Transaction trans = new Transaction(doc, "Hide Elements"))
                    {
                        trans.Start();
                        doc.ActiveView.HideElements(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.TempHide:
                    // Temporarily hide elements (requires active view and transaction)
                    using (Transaction trans = new Transaction(doc, "Temporarily Hide Elements"))
                    {
                        trans.Start();
                        doc.ActiveView.HideElementsTemporary(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.Isolate:
                    // Isolate elements (requires active view and transaction)
                    using (Transaction trans = new Transaction(doc, "Isolate Elements"))
                    {
                        trans.Start();
                        doc.ActiveView.IsolateElementsTemporary(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.Unhide:
                    // Unhide elements (requires active view and transaction)
                    using (Transaction trans = new Transaction(doc, "Unhide Elements"))
                    {
                        trans.Start();
                        doc.ActiveView.UnhideElements(elementIds);
                        trans.Commit();
                    }
                    return true;

                case ElementOperationType.ResetIsolate:
                    // Reset isolation (requires active view and transaction)
                    using (Transaction trans = new Transaction(doc, "Reset Isolation"))
                    {
                        trans.Start();
                        doc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                        trans.Commit();
                    }
                    return true;

                default:
                    throw new Exception($"Unsupported operation type:{setting.Action}");
            }
        }

        /// <summary>
        /// Set specified elements to a specified color in the view
        /// </summary>
        /// <param name="doc">Document</param>
        /// <param name="elementIds">Collection of element IDs to set color for</param>
        /// <param name="elementColor">Color value (RGB format)</param>
        private static void SetElementsColor(Document doc, ICollection<ElementId> elementIds, int[] elementColor)
        {
            // Check if color array is valid
            if (elementColor == null || elementColor.Length < 3)
            {
                elementColor = new int[] { 255, 0, 0 }; // Default red
            }
            // Ensure RGB values are in 0-255 range
            int r = Math.Max(0, Math.Min(255, elementColor[0]));
            int g = Math.Max(0, Math.Min(255, elementColor[1]));
            int b = Math.Max(0, Math.Min(255, elementColor[2]));
            // Create Revit color object - using byte type conversion
            Color color = new Color((byte)r, (byte)g, (byte)b);
            // Create graphic override settings
            OverrideGraphicSettings overrideSettings = new OverrideGraphicSettings();
            // Set specified color
            overrideSettings.SetProjectionLineColor(color);
            overrideSettings.SetCutLineColor(color);
            overrideSettings.SetSurfaceForegroundPatternColor(color);
            overrideSettings.SetSurfaceBackgroundPatternColor(color);

            // Try to set fill pattern
            try
            {
                // Try to get the default fill pattern
                FilteredElementCollector patternCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement));

                // First try to find solid fill pattern
                FillPatternElement solidPattern = patternCollector
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(p => p.GetFillPattern().IsSolidFill);

                if (solidPattern != null)
                {
                    overrideSettings.SetSurfaceForegroundPatternId(solidPattern.Id);
                    overrideSettings.SetSurfaceForegroundPatternVisible(true);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to set fill pattern: {ex.Message}");
            }

            // Apply override settings to each element
            foreach (ElementId id in elementIds)
            {
                doc.ActiveView.SetElementOverrides(id, overrideSettings);
            }
        }

    }
}
