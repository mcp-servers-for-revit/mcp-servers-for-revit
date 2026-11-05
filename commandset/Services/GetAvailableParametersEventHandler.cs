using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetAvailableParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Op te vragen elementen
        public List<string> ElementIds { get; set; }

        // Uitvoeringsresultaat
        public AvailableParametersResult Result { get; private set; }

        // Object voor statussynchronisatie
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Foutmelding bij mislukken (wordt teruggegeven in de response i.p.v. een dialoog te tonen)
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// Zet de wachtstatus terug. Moet vóór het raisen van het externe event worden aangeroepen,
        /// zodat een snel afgeronde handler zijn signaal niet kwijtraakt.
        /// </summary>
        public void ResetCompletion()
        {
            TaskCompleted = false;
            ErrorMessage = null;
            _resetEvent.Reset();
        }

        // Implementatie van de IWaitableExternalEventHandler-interface
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            Result = new AvailableParametersResult();
            try
            {
                var doc = app.ActiveUIDocument.Document;
                if (ElementIds == null || ElementIds.Count == 0)
                {
                    return;
                }

                // Type-parameters verzamelen we per uniek type, niet per element
                var typesById = new Dictionary<long, TypeAvailableParametersOutput>();

                foreach (var elementIdString in ElementIds)
                {
                    ParameterReadUtils.TryParseElementId(elementIdString, out long requestedId);
                    Element element = ParameterReadUtils.ResolveElement(doc, elementIdString);
                    if (element == null)
                    {
                        Result.Elements.Add(new ElementAvailableParametersOutput
                        {
                            ElementId = requestedId,
                            Found = false,
                            Message = $"Element met ID '{elementIdString}' niet gevonden"
                        });
                        continue;
                    }

                    var elementOutput = new ElementAvailableParametersOutput
                    {
                        ElementId = element.Id.GetValue(),
                        Found = true,
                        Name = element.Name,
                        Category = element.Category?.Name
                    };

                    // Is het opgegeven element zelf een type, dan zijn al zijn parameters type-parameters
                    ElementType elementType = element as ElementType ?? ParameterReadUtils.GetElementType(doc, element);
                    if (!(element is ElementType))
                    {
                        elementOutput.InstanceParameters = ParameterReadUtils.GetVisibleParameters(element)
                            .Select(ParameterReadUtils.DescribeParameter)
                            .ToList();
                    }

                    if (elementType != null)
                    {
                        long typeId = elementType.Id.GetValue();
                        elementOutput.TypeId = typeId;

                        if (!typesById.ContainsKey(typeId))
                        {
                            typesById[typeId] = new TypeAvailableParametersOutput
                            {
                                TypeId = typeId,
                                FamilyName = elementType.FamilyName,
                                TypeName = elementType.Name,
                                Category = elementType.Category?.Name,
                                TypeParameters = ParameterReadUtils.GetVisibleParameters(elementType)
                                    .Select(ParameterReadUtils.DescribeParameter)
                                    .ToList()
                            };
                        }
                    }

                    Result.Elements.Add(elementOutput);
                }

                Result.Types = typesById.Values.ToList();
            }
            catch (Exception ex)
            {
                ErrorMessage = "Ophalen van beschikbare parameters mislukt: " + ex.Message;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "Beschikbare parameters ophalen";
        }
    }
}
