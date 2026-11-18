using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class TransferParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long SourceElementId { get; set; }
        public List<long> TargetElementIds { get; set; } = new List<long>();
        public List<string> ParameterNames { get; set; } = new List<string>(); // empty = all writable
        public bool IncludeType { get; set; } = false;
        public bool DryRun { get; set; } = false;

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
                var source = doc.GetElement(new ElementId(SourceElementId));
#else
                var source = doc.GetElement(new ElementId((int)SourceElementId));
#endif
                if (source == null)
                    throw new ArgumentException($"Source element {SourceElementId} not found");

                // Collect source parameter values
                var sourceValues = new Dictionary<string, (StorageType type, object value, bool isType)>();
                foreach (Parameter p in source.Parameters)
                {
                    if (p.IsReadOnly) continue;
                    if (ParameterNames.Count > 0 && !ParameterNames.Contains(p.Definition.Name)) continue;

                    var val = GetParameterValue(p);
                    if (val != null)
                        sourceValues[p.Definition.Name] = (p.StorageType, val, false);
                }

                // Include type parameters if requested
                if (IncludeType)
                {
                    var sourceType = doc.GetElement(source.GetTypeId());
                    if (sourceType != null)
                    {
                        foreach (Parameter p in sourceType.Parameters)
                        {
                            if (p.IsReadOnly) continue;
                            if (ParameterNames.Count > 0 && !ParameterNames.Contains(p.Definition.Name)) continue;
                            if (sourceValues.ContainsKey(p.Definition.Name)) continue;

                            var val = GetParameterValue(p);
                            if (val != null)
                                sourceValues[p.Definition.Name] = (p.StorageType, val, true);
                        }
                    }
                }

                int totalTransferred = 0;
                int targetCount = 0;
                var targetResults = new List<object>();

                using (var transaction = DryRun ? null : new Transaction(doc, "Transfer Parameters"))
                {
                    if (!DryRun) transaction.Start();
                    try
                    {

                    foreach (var targetId in TargetElementIds)
                    {
#if REVIT2024_OR_GREATER
                        var target = doc.GetElement(new ElementId(targetId));
#else
                        var target = doc.GetElement(new ElementId((int)targetId));
#endif
                        if (target == null) continue;

                        int transferred = 0;
                        int skipped = 0;
                        var paramResults = new List<object>();

                        foreach (var kvp in sourceValues)
                        {
                            string paramName = kvp.Key;
                            var (storageType, value, isType) = kvp.Value;

                            Element targetElem = target;
                            if (isType)
                            {
                                targetElem = doc.GetElement(target.GetTypeId());
                                if (targetElem == null) { skipped++; continue; }
                            }

                            var targetParam = targetElem.LookupParameter(paramName);
                            if (targetParam == null || targetParam.IsReadOnly)
                            {
                                skipped++;
                                continue;
                            }

                            try
                            {
                                if (!DryRun)
                                    SetParameterValue(targetParam, storageType, value);

                                transferred++;
                                paramResults.Add(new { parameterName = paramName, success = true, value = value?.ToString() ?? "" });
                            }
                            catch
                            {
                                skipped++;
                            }
                        }

                        totalTransferred += transferred;
                        targetCount++;
                        targetResults.Add(new
                        {
                            targetId,
                            targetName = target.Name,
                            transferred,
                            skipped,
                            parameters = DryRun ? paramResults : null
                        });
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
                        ? $"Dry run: would transfer {sourceValues.Count} parameters to {targetCount} elements"
                        : $"Transferred {totalTransferred} parameter values to {targetCount} elements",
                    Response = new
                    {
                        sourceElementId = SourceElementId,
                        sourceParameterCount = sourceValues.Count,
                        targetCount,
                        totalTransferred,
                        dryRun = DryRun,
                        targets = targetResults
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Transfer parameters failed: {ex.Message}" };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private object GetParameterValue(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString();
                case StorageType.Integer: return p.AsInteger();
                case StorageType.Double: return p.AsDouble();
                case StorageType.ElementId:
#if REVIT2024_OR_GREATER
                    return p.AsElementId().Value;
#else
                    return p.AsElementId().IntegerValue;
#endif
                default: return null;
            }
        }

        private void SetParameterValue(Parameter p, StorageType type, object value)
        {
            switch (type)
            {
                case StorageType.String:
                    p.Set(value as string ?? "");
                    break;
                case StorageType.Integer:
                    p.Set((int)value);
                    break;
                case StorageType.Double:
                    p.Set((double)value);
                    break;
                case StorageType.ElementId:
#if REVIT2024_OR_GREATER
                    p.Set(new ElementId((long)value));
#else
                    p.Set(new ElementId(Convert.ToInt32(value)));
#endif
                    break;
            }
        }

        public string GetName() => "Transfer Parameters";
    }
}
