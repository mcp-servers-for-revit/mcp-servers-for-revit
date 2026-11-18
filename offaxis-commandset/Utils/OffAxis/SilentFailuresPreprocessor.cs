using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Utils.OffAxis
{
    /// <summary>
    /// Headless failures preprocessor that deletes warnings and triggers rollback on errors,
    /// preventing modal dialog popups during automated fixes.
    /// </summary>
    public class SilentFailuresPreprocessor : IFailuresPreprocessor
    {
        public List<object> Log { get; } = new List<object>();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var messages = failuresAccessor.GetFailureMessages();
            bool hasError = false;

            foreach (var msg in messages)
            {
                var severity = msg.GetSeverity();
                string desc = msg.GetDescriptionText();

                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(msg);
                    Log.Add(new { Type = "Warning", Text = desc });
                }
                else if (severity == FailureSeverity.Error)
                {
                    hasError = true;
                    Log.Add(new { Type = "Error", Text = desc });
                }
            }

            if (hasError)
            {
                return FailureProcessingResult.ProceedWithRollBack;
            }

            return FailureProcessingResult.Continue;
        }
    }
}
