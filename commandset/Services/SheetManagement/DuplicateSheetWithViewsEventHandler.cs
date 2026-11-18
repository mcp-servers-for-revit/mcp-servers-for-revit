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
    public class DuplicateSheetWithViewsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long SheetId { get; set; }
        public int Copies { get; set; } = 1;
        public bool DuplicateViews { get; set; } = true;
        public bool KeepLegends { get; set; } = true;
        public bool KeepSchedules { get; set; } = true;
        public string NewSheetNumberPrefix { get; set; } = "";
        public string ViewDuplicateOptionName { get; set; } = "DuplicateWithDetailing";

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 30000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        private ViewDuplicateOption ParseViewDuplicateOption()
        {
            switch (ViewDuplicateOptionName)
            {
                case "Duplicate":
                    return ViewDuplicateOption.Duplicate;
                case "DuplicateAsDependent":
                    return ViewDuplicateOption.AsDependent;
                case "DuplicateWithDetailing":
                default:
                    return ViewDuplicateOption.WithDetailing;
            }
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var duplicateOption = ParseViewDuplicateOption();

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

                // Collect schedule sheet instances on source sheet
                var scheduleInstances = new FilteredElementCollector(doc, sourceSheet.Id)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>()
                    .ToList();

                var createdSheets = new List<object>();

                using (var tg = new TransactionGroup(doc, "Duplicate Sheet With Views"))
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

                                // Generate sheet number with prefix and auto-increment
                                string baseNumber = sourceSheet.SheetNumber;
                                string newNumber = string.IsNullOrEmpty(NewSheetNumberPrefix)
                                    ? baseNumber
                                    : $"{NewSheetNumberPrefix}{baseNumber}";
                                if (Copies > 1) newNumber += $"-{i + 1:D2}";

                                try { newSheet.SheetNumber = newNumber; }
                                catch { newSheet.SheetNumber = $"{newNumber}_{Guid.NewGuid().ToString().Substring(0, 4)}"; }

                                newSheet.Name = sourceSheet.Name;

                                // Copy title block parameter values from source to new sheet
                                if (sourceTitleBlock != null)
                                {
                                    var newTitleBlocks = new FilteredElementCollector(doc, newSheet.Id)
                                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                        .ToElements();
                                    var newTitleBlock = newTitleBlocks.FirstOrDefault() as FamilyInstance;
                                    if (newTitleBlock != null)
                                    {
                                        foreach (Parameter srcParam in sourceTitleBlock.Parameters)
                                        {
                                            if (srcParam.IsReadOnly) continue;
                                            var destParam = newTitleBlock.LookupParameter(srcParam.Definition.Name);
                                            if (destParam == null || destParam.IsReadOnly) continue;
                                            try
                                            {
                                                switch (srcParam.StorageType)
                                                {
                                                    case StorageType.String:
                                                        destParam.Set(srcParam.AsString() ?? "");
                                                        break;
                                                    case StorageType.Integer:
                                                        destParam.Set(srcParam.AsInteger());
                                                        break;
                                                    case StorageType.Double:
                                                        destParam.Set(srcParam.AsDouble());
                                                        break;
                                                    case StorageType.ElementId:
                                                        destParam.Set(srcParam.AsElementId());
                                                        break;
                                                }
                                            }
                                            catch { /* Skip parameters that can't be copied */ }
                                        }
                                    }
                                }

                                var placedViewports = new List<object>();
                                int viewportCount = 0;

                                // Process viewports (regular views and legends)
                                foreach (var (vp, view, center) in viewportInfos)
                                {
                                    bool isLegend = view.ViewType == ViewType.Legend;

                                    if (isLegend && !KeepLegends) continue;

                                    try
                                    {
                                        if (isLegend)
                                        {
                                            // Legends can be placed on multiple sheets without duplicating
                                            Viewport.Create(doc, newSheet.Id, view.Id, center);
                                            viewportCount++;
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
                                        else if (DuplicateViews)
                                        {
                                            // Duplicate the view using the specified option
                                            var newViewId = view.Duplicate(duplicateOption);
                                            var newView = doc.GetElement(newViewId) as View;
                                            if (newView != null)
                                            {
                                                try { newView.Name = $"{view.Name} - {newSheet.SheetNumber}"; } catch { }
                                                Viewport.Create(doc, newSheet.Id, newViewId, center);
                                                viewportCount++;
                                                placedViewports.Add(new
                                                {
#if REVIT2024_OR_GREATER
                                                    viewId = newViewId.Value,
#else
                                                    viewId = newViewId.IntegerValue,
#endif
                                                    viewName = newView.Name,
                                                    type = view.ViewType.ToString(),
                                                    duplicated = true,
                                                    duplicateOption = ViewDuplicateOptionName
                                                });
                                            }
                                        }
                                        // If !duplicateViews and not a legend, skip (can't place same view on two sheets)
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

                                // Process schedules
                                if (KeepSchedules)
                                {
                                    foreach (var ssi in scheduleInstances)
                                    {
                                        try
                                        {
                                            var scheduleId = ssi.ScheduleId;
                                            var scheduleView = doc.GetElement(scheduleId) as ViewSchedule;
                                            if (scheduleView == null) continue;

                                            var point = ssi.Point;
                                            ScheduleSheetInstance.Create(doc, newSheet.Id, scheduleId, point);
                                            viewportCount++;
                                            placedViewports.Add(new
                                            {
#if REVIT2024_OR_GREATER
                                                viewId = scheduleId.Value,
#else
                                                viewId = scheduleId.IntegerValue,
#endif
                                                viewName = scheduleView.Name,
                                                type = "schedule",
                                                duplicated = false
                                            });
                                        }
                                        catch (Exception ex)
                                        {
                                            placedViewports.Add(new
                                            {
                                                viewId = (long)0,
                                                viewName = "schedule",
                                                type = "error",
                                                duplicated = false,
                                                error = ex.Message
                                            });
                                        }
                                    }
                                }

                                t.Commit();

                                createdSheets.Add(new
                                {
#if REVIT2024_OR_GREATER
                                    newSheetId = newSheet.Id.Value,
#else
                                    newSheetId = newSheet.Id.IntegerValue,
#endif
                                    sheetNumber = newSheet.SheetNumber,
                                    sheetName = newSheet.Name,
                                    viewportCount = viewportCount
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
                Result = new AIResult<object> { Success = false, Message = $"Duplicate sheet with views failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Duplicate Sheet With Views";
    }
}
