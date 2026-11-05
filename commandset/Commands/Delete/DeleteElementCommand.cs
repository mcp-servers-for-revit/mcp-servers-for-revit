using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Delete
{
    public class DeleteElementCommand : ExternalEventCommandBase
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
                    // Array-parameter parsen
                    var elementIds = parameters?["elementIds"]?.ToObject<string[]>();
                    if (elementIds == null || elementIds.Length == 0)
                    {
                        throw new ArgumentException("Lijst met element-ID's mag niet leeg zijn");
                    }

                    // Stel de array met te verwijderen element-ID's in
                    _handler.ResetCompletion();
                    _handler.ElementIds = elementIds;

                    // Extern event activeren en wachten op voltooiing
                    if (RaiseAndWaitForCompletion(15000))
                    {
                        if (_handler.IsSuccess)
                        {
                            return new { deleted = true, count = _handler.DeletedCount, warning = _handler.Warning };
                        }
                        else
                        {
                            throw new Exception(_handler.ErrorMessage ?? "Verwijderen van element mislukt");
                        }
                    }
                    else
                    {
                        throw new TimeoutException("Time-out bij verwijderen van element");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Verwijderen van element mislukt: {ex.Message}");
                }
            }
        }
    }
}
