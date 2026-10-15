using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Delete
{
    public class DeleteElementCommand : RevitCommandBase
    {
        private static readonly object _executionLock = new object();
        private DeleteElementEventHandler _handler => (DeleteElementEventHandler)Handler;

        public override string CommandName => "delete_element";

        public DeleteElementCommand(UIApplication uiApp)
            : base(new DeleteElementEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    // 解析数组参数
                    // Numeric strings from older clients still convert.
                    var elementIds = parameters?["elementIds"]?.ToObject<long[]>();
                    if (elementIds == null || elementIds.Length == 0)
                    {
                        throw new ArgumentException("elementIds must contain at least one element id");
                    }

                    // 设置要删除的元素ID数组
                    _handler.ElementIds = elementIds;

                    // 触发外部事件并等待完成
                    RaiseAndWaitForCompletion(15000);

                    // deletedCount/deletedIds include dependents Revit removed too, such as doors hosted in a deleted wall.
                    int deletedRequested = elementIds.Length - _handler.SkippedIds.Count;
                    string message = $"Deleted {deletedRequested} of {elementIds.Length} requested element(s)";
                    if (_handler.DeletedCount > deletedRequested)
                    {
                        message += $", plus {_handler.DeletedCount - deletedRequested} dependent element(s)";
                    }
                    if (_handler.SkippedIds.Count > 0)
                    {
                        message += $". Not found: {string.Join(", ", _handler.SkippedIds)}";
                    }

                    return new AIResult<object>
                    {
                        Success = _handler.IsSuccess,
                        Message = message,
                        Response = new
                        {
                            requestedCount = elementIds.Length,
                            deletedCount = _handler.DeletedCount,
                            deletedIds = _handler.DeletedIds,
                            skippedIds = _handler.SkippedIds
                        }
                    };
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to delete elements: {ex.Message}", ex);
                }
            }
        }
    }
}
