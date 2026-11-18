using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Utils.Dwg
{
    /// <summary>
    /// Deletes warnings (e.g. "lines are slightly off-axis") during automated
    /// drawing so no modal dialog blocks Revit, and rolls back on hard errors.
    /// </summary>
    public class SilentWarningsPreprocessor : IFailuresPreprocessor
    {
        public List<object> Log { get; } = new List<object>();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hasError = false;

            foreach (var msg in failuresAccessor.GetFailureMessages())
            {
                var severity = msg.GetSeverity();
                string desc = msg.GetDescriptionText();

                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(msg);
                    Log.Add(new Dictionary<string, object> { ["type"] = "Warning", ["text"] = desc });
                }
                else if (severity == FailureSeverity.Error)
                {
                    hasError = true;
                    Log.Add(new Dictionary<string, object> { ["type"] = "Error", ["text"] = desc });
                }
            }

            return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
