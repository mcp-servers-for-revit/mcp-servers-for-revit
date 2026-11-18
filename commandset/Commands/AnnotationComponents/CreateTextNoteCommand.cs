using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.AnnotationComponents;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.AnnotationComponents
{
    public class CreateTextNoteCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private CreateTextNoteEventHandler _handler => (CreateTextNoteEventHandler)Handler;

        public override string CommandName => "create_text_note";

        public CreateTextNoteCommand(UIApplication uiApp)
            : base(new CreateTextNoteEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    var textNotes = parameters?["textNotes"]?.ToObject<List<TextNoteData>>();
                    if (textNotes == null || textNotes.Count == 0)
                        throw new ArgumentException("textNotes array is required");

                    _handler.TextNotes = textNotes;

                    if (RaiseAndWaitForCompletion(15000))
                    {
                        return _handler.Result;
                    }
                    else
                    {
                        throw new TimeoutException("Create text notes timed out");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Create text notes failed: {ex.Message}");
                }
            }
        }
    }
}
