using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetPhasesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool IncludePhaseFilters { get; set; } = true;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(bool includePhaseFilters = true)
        {
            IncludePhaseFilters = includePhaseFilters;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var result = new Dictionary<string, object>();

                // Phases
                var phases = new List<object>();
                foreach (Phase phase in doc.Phases)
                {
                    phases.Add(new
                    {
#if REVIT2024_OR_GREATER
                        id = phase.Id.Value,
#else
                        id = phase.Id.IntegerValue,
#endif
                        name = phase.Name
                    });
                }
                result["phases"] = phases;

                // Phase filters
                if (IncludePhaseFilters)
                {
                    var phaseFilters = new List<object>();
                    var filterCollector = new FilteredElementCollector(doc)
                        .OfClass(typeof(PhaseFilter));

                    foreach (PhaseFilter phaseFilter in filterCollector)
                    {
                        phaseFilters.Add(new
                        {
#if REVIT2024_OR_GREATER
                            id = phaseFilter.Id.Value,
#else
                            id = phaseFilter.Id.IntegerValue,
#endif
                            name = phaseFilter.Name
                        });
                    }
                    result["phaseFilters"] = phaseFilters;
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = $"Retrieved {phases.Count} phases" + (IncludePhaseFilters ? $" and {((List<object>)result["phaseFilters"]).Count} phase filters" : ""),
                    Response = result
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to get phases: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Get Phases";
    }
}
