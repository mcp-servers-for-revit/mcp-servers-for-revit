using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetCurrentViewElementsCommand : RevitCommandBase
    {
        private GetCurrentViewElementsEventHandler _handler => (GetCurrentViewElementsEventHandler)Handler;

        public override string CommandName => "get_current_view_elements";

        public GetCurrentViewElementsCommand(UIApplication uiApp)
            : base(new GetCurrentViewElementsEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                // 解析参数
                List<string> modelCategoryList = parameters?["modelCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                List<string> annotationCategoryList = parameters?["annotationCategoryList"]?.ToObject<List<string>>() ?? new List<string>();
                bool includeHidden = parameters?["includeHidden"]?.Value<bool>() ?? false;
                int limit = parameters?["limit"]?.Value<int>() ?? 100;

                // 设置查询参数
                _handler.SetQueryParameters(modelCategoryList, annotationCategoryList, includeHidden, limit);

                // 触发外部事件并等待完成
                if (RaiseAndWaitForCompletion(60000)) // 60秒超时
                {
                    return _handler.ResultInfo;
                }
                else
                {
                    throw new TimeoutException("获取视图元素超时");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"获取视图元素失败: {ex.Message}");
            }
        }
    }
}
