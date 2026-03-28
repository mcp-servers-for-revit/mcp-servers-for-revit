using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ChangeElementTypeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> ElementIds { get; set; } = new List<long>();
        public long TargetTypeId { get; set; } = 0;
        public string TargetTypeName { get; set; } = "";
        public string TargetFamilyName { get; set; } = "";
        public AIResult<object> Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Resolve target type
                ElementId targetTypeElemId = ResolveTargetType(doc);
                if (targetTypeElemId == null || targetTypeElemId == ElementId.InvalidElementId)
                {
                    Result = new AIResult<object> { Success = false, Message = "Target type not found" };
                    return;
                }

                var results = new List<object>();
                int successCount = 0;

                using (var transaction = new Transaction(doc, "Change Element Type"))
                {
                    transaction.Start();

                    foreach (var id in ElementIds)
                    {
                        var elemId = ToElementId(id);
                        var element = doc.GetElement(elemId);
                        if (element == null) continue;

                        try
                        {
                            element.ChangeTypeId(targetTypeElemId);
                            successCount++;
                            results.Add(new
                            {
                                elementId = id,
                                success = true,
                                message = "Type changed successfully"
                            });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new
                            {
                                elementId = id,
                                success = false,
                                message = ex.Message
                            });
                        }
                    }

                    transaction.Commit();
                }

                var targetType = doc.GetElement(targetTypeElemId);
                Result = new AIResult<object>
                {
                    Success = successCount > 0,
                    Message = $"Changed type for {successCount}/{ElementIds.Count} elements to '{targetType?.Name}'",
                    Response = new
                    {
                        targetTypeName = targetType?.Name,
                        totalProcessed = results.Count,
                        successCount,
                        results
                    }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Change type failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private ElementId ResolveTargetType(Document doc)
        {
            if (TargetTypeId > 0)
            {
                var typeId = ToElementId(TargetTypeId);
                if (doc.GetElement(typeId) != null) return typeId;
            }

            if (!string.IsNullOrEmpty(TargetTypeName))
            {
                var collector = new FilteredElementCollector(doc).WhereElementIsElementType();

                if (!string.IsNullOrEmpty(TargetFamilyName))
                {
                    var match = collector.Cast<ElementType>()
                        .FirstOrDefault(t =>
                            t.Name.Equals(TargetTypeName, StringComparison.OrdinalIgnoreCase) &&
                            t.FamilyName.Equals(TargetFamilyName, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match.Id;
                }

                var byName = collector.Cast<ElementType>()
                    .FirstOrDefault(t => t.Name.Equals(TargetTypeName, StringComparison.OrdinalIgnoreCase));
                if (byName != null) return byName.Id;
            }

            return ElementId.InvalidElementId;
        }

        private ElementId ToElementId(long id)
        {
            return new ElementId(id);
        }

        public string GetName() => "Change Element Type";
    }
}
