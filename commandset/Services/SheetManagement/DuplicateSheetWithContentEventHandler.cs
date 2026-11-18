using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.SheetManagement
{
    public class DuplicateSheetWithContentEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long SheetId { get; set; }
        public int Copies { get; set; } = 1;
        public bool DuplicateViews { get; set; } = true;
        public bool KeepLegends { get; set; } = true;
        public bool KeepSchedules { get; set; } = true;
        public bool CopyRevisions { get; set; } = false;
        public string SheetNumberPrefix { get; set; } = "";
        public string SheetNumberSuffix { get; set; } = "";

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 30000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

#if REVIT2024_OR_GREATER
                var sourceSheet = doc.GetElement(new ElementId(SheetId)) as ViewSheet;
#else
                var sourceSheet = doc.GetElement(new ElementId((int)SheetId)) as ViewSheet;
#endif
                if (sourceSheet == null)
                    throw new ArgumentException($"Sheet with ID {SheetId} not found");

                // Get title block from source sheet
                var titleBlocks = new FilteredElementCollector(doc, sourceSheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .ToElements();
                var sourceTitleBlock = titleBlocks.FirstOrDefault() as FamilyInstance;

                // Get all viewports on source sheet
                var viewportIds = sourceSheet.GetAllViewports();
                var viewportInfos = new List<(Viewport vp, View view, XYZ center)>();
                foreach (var vpId in viewportIds)
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp == null) continue;
                    var view = doc.GetElement(vp.ViewId) as View;
                    if (view == null) continue;
                    viewportInfos.Add((vp, view, vp.GetBoxCenter()));
                }

                var createdSheets = new List<object>();

                using (var tg = new TransactionGroup(doc, "Duplicate Sheet With Content"))
                {
                    tg.Start();

                    for (int i = 0; i < Copies; i++)
                    {
                        using (var t = new Transaction(doc, $"Create Sheet Copy {i + 1}"))
                        {
                            t.Start();
                            try
                            {

                            // Create new sheet with same title block type
                            ElementId titleBlockTypeId = sourceTitleBlock?.GetTypeId() ?? ElementId.InvalidElementId;
                            var newSheet = ViewSheet.Create(doc, titleBlockTypeId);

                            // Set sheet number
                            string baseNumber = sourceSheet.SheetNumber;
                            string newNumber = $"{SheetNumberPrefix}{baseNumber}{SheetNumberSuffix}";
                            if (Copies > 1) newNumber += $"-{i + 1:D2}";

                            try { newSheet.SheetNumber = newNumber; }
                            catch { newSheet.SheetNumber = $"{newNumber}_{Guid.NewGuid().ToString().Substring(0, 4)}"; }

                            newSheet.Name = sourceSheet.Name;

                            // Copy revisions if requested
                            if (CopyRevisions)
                            {
                                var revisionIds = sourceSheet.GetAdditionalRevisionIds();
                                if (revisionIds.Count > 0)
                                    newSheet.SetAdditionalRevisionIds(revisionIds);
                            }

                            var placedViewports = new List<object>();

                            foreach (var (vp, view, center) in viewportInfos)
                            {
                                bool isLegend = view.ViewType == ViewType.Legend;
                                bool isSchedule = view.ViewType == ViewType.Schedule || view.ViewType == ViewType.PanelSchedule;

                                if (isLegend && !KeepLegends) continue;
                                if (isSchedule && !KeepSchedules) continue;

                                try
                                {
                                    if (isLegend)
                                    {
                                        // Legends can be placed on multiple sheets without duplicating
                                        var newVp = Viewport.Create(doc, newSheet.Id, view.Id, center);
                                        placedViewports.Add(new
                                        {
#if REVIT2024_OR_GREATER
                                            viewId = view.Id.Value,
#else
                                            viewId = view.Id.IntegerValue,
#endif
                                            viewName = view.Name,
                                            type = "legend",
                                            duplicated = false
                                        });
                                    }
                                    else if (isSchedule)
                                    {
                                        // Schedules: create ScheduleSheetInstance
                                        var ssi = ScheduleSheetInstance.Create(doc, newSheet.Id, view.Id, center);
                                        placedViewports.Add(new
                                        {
#if REVIT2024_OR_GREATER
                                            viewId = view.Id.Value,
#else
                                            viewId = view.Id.IntegerValue,
#endif
                                            viewName = view.Name,
                                            type = "schedule",
                                            duplicated = false
                                        });
                                    }
                                    else if (DuplicateViews)
                                    {
                                        // Duplicate view with detailing
                                        var newViewId = view.Duplicate(ViewDuplicateOption.WithDetailing);
                                        var newView = doc.GetElement(newViewId) as View;
                                        if (newView != null)
                                        {
                                            try { newView.Name = $"{view.Name} - {newSheet.SheetNumber}"; } catch { }
                                            var newVp = Viewport.Create(doc, newSheet.Id, newViewId, center);
                                            placedViewports.Add(new
                                            {
#if REVIT2024_OR_GREATER
                                                viewId = newViewId.Value,
#else
                                                viewId = newViewId.IntegerValue,
#endif
                                                viewName = newView.Name,
                                                type = view.ViewType.ToString(),
                                                duplicated = true
                                            });
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    placedViewports.Add(new
                                    {
                                        viewId = (long)0,
                                        viewName = view.Name,
                                        type = "error",
                                        duplicated = false,
                                        error = ex.Message
                                    });
                                }
                            }

                            t.Commit();

                            createdSheets.Add(new
                            {
#if REVIT2024_OR_GREATER
                                sheetId = newSheet.Id.Value,
#else
                                sheetId = newSheet.Id.IntegerValue,
#endif
                                sheetNumber = newSheet.SheetNumber,
                                sheetName = newSheet.Name,
                                viewports = placedViewports
                            });
                            }
                            catch
                            {
                                if (t.GetStatus() == TransactionStatus.Started)
                                    t.RollBack();
                                throw;
                            }
                        }
                    }

                    tg.Assimilate();
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Created {createdSheets.Count} sheet copies from '{sourceSheet.SheetNumber} - {sourceSheet.Name}'",
                    Response = new
                    {
                        sourceSheetNumber = sourceSheet.SheetNumber,
                        sourceSheetName = sourceSheet.Name,
                        copies = createdSheets.Count,
                        sheets = createdSheets
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Duplicate sheet failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Duplicate Sheet With Content";
    }
}
