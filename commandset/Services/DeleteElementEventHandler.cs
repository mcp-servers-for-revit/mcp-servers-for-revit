using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;
using RevitMCPCommandSet.Utils;

namespace RevitMCPCommandSet.Services
{
    public class DeleteElementEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // 执行结果
        public bool IsSuccess { get; private set; }

        // 成功删除的元素数量
        public int DeletedCount { get; private set; }

        // Requested ids that don't match an element in the model
        public List<long> SkippedIds { get; private set; } = new List<long>();

        // Everything Revit deleted: the requested elements plus dependents such as hosted doors
        public List<long> DeletedIds { get; private set; } = new List<long>();
        // 状态同步对象
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        // 要删除的元素ID数组
        public long[] ElementIds { get; set; }
        // 实现IWaitableExternalEventHandler接口
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
                IsSuccess = false;
                DeletedCount = 0;
                SkippedIds = new List<long>();
                DeletedIds = new List<long>();
                if (ElementIds == null || ElementIds.Length == 0)
                {
                    return;
                }
                // 创建待删除元素ID集合
                List<ElementId> elementIdsToDelete = new List<ElementId>();
                foreach (long elementIdValue in ElementIds)
                {
                    // 检查元素是否存在
                    if (doc.GetElement(elementIdValue.ToRevitElementId()) != null)
                    {
                        elementIdsToDelete.Add(elementIdValue.ToRevitElementId());
                    }
                    else
                    {
                        SkippedIds.Add(elementIdValue);
                    }
                }

                if (elementIdsToDelete.Count == 0)
                {
                    throw new ArgumentException(
                        $"None of the requested ids match an element in the model: {string.Join(", ", ElementIds)}");
                }

                using (var transaction = new Transaction(doc, "Delete Elements"))
                {
                    transaction.Start();

                    // 批量删除元素
                    ICollection<ElementId> deletedIds = doc.Delete(elementIdsToDelete);
                    DeletedCount = deletedIds.Count;
                    DeletedIds = deletedIds.Select(id => id.GetValue()).ToList();

                    transaction.Commit();
                }
                IsSuccess = true;
            }
            // Exceptions propagate to RevitCommandBase, which reports them to the caller.
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }
        public string GetName()
        {
            return "删除元素";
        }
    }
}
