using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetCurrentViewInfoCommand : RevitCommandBase
    {
        private GetCurrentViewInfoEventHandler _handler => (GetCurrentViewInfoEventHandler)Handler;

        public override string CommandName => "get_current_view_info";

        public GetCurrentViewInfoCommand(UIApplication uiApp)
            : base(new GetCurrentViewInfoEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            // 触发外部事件并等待完成
            if (RaiseAndWaitForCompletion(10000)) // 10秒超时
            {
                return _handler.ResultInfo;
            }
            else
            {
                throw new TimeoutException("获取信息超时");
            }
        }
    }
}
