using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Base;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Services;

namespace RevitMCPCommandSet.Commands
{
    public class CreateSurfaceElementCommand : RevitCommandBase
    {
        private CreateSurfaceElementEventHandler _handler => (CreateSurfaceElementEventHandler)Handler;

        /// <summary>
        /// 命令名称
        /// </summary>
        public override string CommandName => "create_surface_based_element";

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="uiApp">Revit UIApplication</param>
        public CreateSurfaceElementCommand(UIApplication uiApp)
            : base(new CreateSurfaceElementEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                List<SurfaceElement> data = new List<SurfaceElement>();
                // 解析参数
                data = parameters["data"].ToObject<List<SurfaceElement>>();
                if (data == null)
                    throw new ArgumentNullException(nameof(data), "AI传入数据为空");

                // 设置面状构件体参数
                _handler.SetParameters(data);

                // 触发外部事件并等待完成
                if (RaiseAndWaitForCompletion(10000))
                {
                    return _handler.Result;
                }
                else
                {
                    throw new TimeoutException("创建面状构件操作超时");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"创建面状构件失败: {ex.Message}");
            }
        }
    }
}
