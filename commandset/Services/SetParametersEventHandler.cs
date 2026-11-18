using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class SetParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Aan te passen elementen + parameters
        public List<ElementParameterInput> Elements { get; set; }

        // Uitvoeringsresultaat, per element
        public List<ElementParameterOutput> Result { get; private set; }

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
            Result = new List<ElementParameterOutput>();
            try
            {
                var doc = app.ActiveUIDocument.Document;
                if (Elements == null || Elements.Count == 0)
                {
                    return;
                }

                using var transaction = new Transaction(doc, "Set parameters");
                transaction.Start();
                foreach (var input in Elements)
                {
                    var element = ParameterReadUtils.ResolveElement(doc, input.ElementId);
                    long.TryParse(input.ElementId, out var id);
                    var result = new ElementParameterOutput { ElementId = id };
                    foreach (var change in input.Parameters)
                    {
                        var entry = new ParameterResultOutput { ParameterName = change.Name };
                        try
                        {
                            if (element == null) throw new ArgumentException($"Element {input.ElementId} not found");
                            var parameter = element.LookupParameter(change.Name);
                            if (parameter == null || parameter.IsReadOnly)
                                throw new ArgumentException($"Parameter '{change.Name}' is missing or read-only");
                            entry.Success = WriteValue(parameter, change.Value);
                            entry.Message = entry.Success ? "Updated" : "Revit rejected the value";
                        }
                        catch (Exception ex) { entry.Message = ex.Message; }
                        result.Parameters.Add(entry);
                    }
                    Result.Add(result);
                }
                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit did not commit the parameter changes.");
            }
            catch (Exception ex)
            {
                ErrorMessage = "Aanpassen van parameters mislukt: " + ex.Message;
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private static bool WriteValue(Parameter parameter, JToken value)
        {
            switch (parameter.StorageType)
            {
                case StorageType.String when value.Type == JTokenType.String:
                    return parameter.Set(value.Value<string>());
                case StorageType.Integer when value.Type == JTokenType.Boolean && parameter.Definition.IsYesNoDataType():
                    return parameter.Set(value.Value<bool>() ? 1 : 0);
                case StorageType.Integer when value.Type == JTokenType.Integer:
                    return parameter.Set(value.Value<int>());
                case StorageType.ElementId when value.Type == JTokenType.Integer:
                    return parameter.Set(Utils.ElementIdExtensions.FromLong(value.Value<long>()));
                case StorageType.Double when value.Type == JTokenType.Float || value.Type == JTokenType.Integer:
                    double number = value.Value<double>();
                    if (ParameterReadUtils.HasDisplayUnit(parameter))
#if REVIT2022_OR_GREATER
                        number = UnitUtils.ConvertToInternalUnits(number, parameter.GetUnitTypeId());
#else
                        number = UnitUtils.ConvertToInternalUnits(number, parameter.DisplayUnitType);
#endif
                    return parameter.Set(number);
                default:
                    throw new ArgumentException($"Expected a value compatible with {parameter.StorageType}");
            }
        }

        public string GetName()
        {
            return "Parameters aanpassen";
        }
    }
}
