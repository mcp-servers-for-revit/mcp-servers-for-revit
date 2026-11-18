using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands
{
    /// <summary>
    /// Beheert materialen volgens de NAAKT-codering: opties opvragen, materialen opsommen, aanmaken en
    /// aanpassen. Eén commando met een 'action'-veld; de uitwerking zit in MaterialOperationsUtils.
    /// </summary>
    public class ManageMaterialsCommand : ExternalEventCommandBase
    {
        private static readonly string[] Actions = { "options", "list", "create", "update" };

        private ManageMaterialsEventHandler _handler => (ManageMaterialsEventHandler)Handler;

        public override string CommandName => "manage_materials";

        public ManageMaterialsCommand(UIApplication uiApp)
            : base(new ManageMaterialsEventHandler(), uiApp)
        {
        }

        private readonly object _executionLock = new object();

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var request = parameters?.ToObject<ManageMaterialsRequest>();
                    string action = request?.Action?.Trim().ToLowerInvariant();
                    if (action == null || !Actions.Contains(action))
                    {
                        throw new ArgumentException($"Ongeldige of ontbrekende actie. Kies uit: {string.Join(", ", Actions)}");
                    }

                    request.Action = action;

                    if (action == "create" && (request.Materials == null || request.Materials.Count == 0))
                    {
                        throw new ArgumentException("Voor 'create' is een niet-lege lijst 'materials' nodig");
                    }

                    if (action == "update" && (request.Updates == null || request.Updates.Count == 0))
                    {
                        throw new ArgumentException("Voor 'update' is een niet-lege lijst 'updates' nodig");
                    }

                    _handler.ResetCompletion();
                    _handler.Request = request;

                    // Extern event activeren en wachten op voltooiing
                    if (!RaiseAndWaitForCompletion(120000)) // 120 seconden time-out
                    {
                        throw new TimeoutException("Time-out bij het beheren van materialen");
                    }

                    if (_handler.Error != null)
                    {
                        throw new Exception(_handler.Error);
                    }

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Materialen beheren mislukt: {ex.Message}");
                }
            }
        }
    }
}
