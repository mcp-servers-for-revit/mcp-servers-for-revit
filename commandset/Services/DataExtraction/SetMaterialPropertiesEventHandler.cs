using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class SetMaterialRequest
    {
        public long MaterialId { get; set; }
        public string Comments { get; set; }
        public string Description { get; set; }
        public string Manufacturer { get; set; }
        public string Model { get; set; }
        public string Url { get; set; }
        public double? Cost { get; set; }
        public string Mark { get; set; }
        public string Keynote { get; set; }
        public string Name { get; set; }
    }

    public class SetMaterialPropertiesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public List<SetMaterialRequest> Requests { get; set; } = new List<SetMaterialRequest>();
        public bool DryRun { get; set; } = true;

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

                if (Requests == null || Requests.Count == 0)
                    throw new ArgumentException("requests array is required and must not be empty");

                int updated = 0;
                int skipped = 0;
                int errors = 0;
                var results = new List<object>();

                using (var transaction = DryRun ? null : new Transaction(doc, "Set Material Properties"))
                {
                    if (!DryRun) transaction.Start();
                    try
                    {
                        foreach (var req in Requests)
                        {
                            try
                            {
                                var eid = RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(req.MaterialId);
                                var mat = doc.GetElement(eid) as Material;
                                if (mat == null)
                                {
                                    errors++;
                                    results.Add(new { materialId = req.MaterialId, success = false, error = "Material not found" });
                                    continue;
                                }

                                var changes = new List<string>();

                                // Name (direct property)
                                if (!string.IsNullOrEmpty(req.Name) && mat.Name != req.Name)
                                {
                                    if (!DryRun) mat.Name = req.Name;
                                    changes.Add("Name");
                                }

                                // Comments
                                if (req.Comments != null)
                                    TrySetStringParam(mat, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, req.Comments, DryRun, changes, "Comments");

                                // Description
                                if (req.Description != null)
                                    TrySetStringParam(mat, BuiltInParameter.ALL_MODEL_DESCRIPTION, req.Description, DryRun, changes, "Description");

                                // Manufacturer
                                if (req.Manufacturer != null)
                                    TrySetStringParam(mat, BuiltInParameter.ALL_MODEL_MANUFACTURER, req.Manufacturer, DryRun, changes, "Manufacturer");

                                // Model
                                if (req.Model != null)
                                    TrySetStringParam(mat, BuiltInParameter.ALL_MODEL_MODEL, req.Model, DryRun, changes, "Model");

                                // URL
                                if (req.Url != null)
                                    TrySetStringParam(mat, BuiltInParameter.ALL_MODEL_URL, req.Url, DryRun, changes, "URL");

                                // Mark
                                if (req.Mark != null)
                                    TrySetStringParam(mat, BuiltInParameter.ALL_MODEL_MARK, req.Mark, DryRun, changes, "Mark");

                                // Keynote
                                if (req.Keynote != null)
                                    TrySetStringParam(mat, BuiltInParameter.KEYNOTE_PARAM, req.Keynote, DryRun, changes, "Keynote");

                                // Cost (Double parameter)
                                if (req.Cost.HasValue)
                                {
                                    var costParam = mat.get_Parameter(BuiltInParameter.ALL_MODEL_COST);
                                    if (costParam != null && !costParam.IsReadOnly)
                                    {
                                        if (!DryRun) costParam.Set(req.Cost.Value);
                                        changes.Add("Cost");
                                    }
                                }

                                if (changes.Count > 0)
                                {
                                    updated++;
                                    results.Add(new
                                    {
                                        materialId = req.MaterialId,
                                        name = mat.Name,
                                        success = true,
                                        changedFields = changes
                                    });
                                }
                                else
                                {
                                    skipped++;
                                    results.Add(new
                                    {
                                        materialId = req.MaterialId,
                                        name = mat.Name,
                                        success = true,
                                        changedFields = changes,
                                        note = "No changes needed"
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                errors++;
                                results.Add(new { materialId = req.MaterialId, success = false, error = ex.Message });
                            }
                        }

                        if (!DryRun) transaction.Commit();
                    }
                    catch
                    {
                        if (!DryRun && transaction?.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = DryRun
                        ? $"Dry run: {updated} materials would be updated, {skipped} unchanged, {errors} errors"
                        : $"Updated {updated} materials, {skipped} unchanged, {errors} errors",
                    Response = new
                    {
                        dryRun = DryRun,
                        updated,
                        skipped,
                        errors,
                        totalProcessed = Requests.Count,
                        materials = results
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Set material properties failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private void TrySetStringParam(Element elem, BuiltInParameter bip, string value, bool dryRun, List<string> changes, string fieldName)
        {
            var param = elem.get_Parameter(bip);
            if (param != null && !param.IsReadOnly)
            {
                if (!dryRun) param.Set(value);
                changes.Add(fieldName);
            }
        }

        public string GetName() => "Set Material Properties";
    }
}
