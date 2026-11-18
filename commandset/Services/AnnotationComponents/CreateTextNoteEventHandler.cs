using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.AnnotationComponents
{
    public class CreateTextNoteEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<TextNoteData> TextNotes { get; set; }
        public AIResult<List<object>> Result { get; private set; }

        public void SetParameters(List<TextNoteData> textNotes)
        {
            TextNotes = textNotes;
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
                var results = new List<object>();
                int successCount = 0;

                var defaultTextNoteTypeId = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElementId();
                if (defaultTextNoteTypeId == null)
                    throw new InvalidOperationException("No text note type found in project");

                using (var transaction = new Transaction(doc, "Create Text Notes"))
                {
                    transaction.Start();
                    try
                    {

                    foreach (var data in TextNotes)
                    {
                        try
                        {
                            // Get view
                            View view;
                            if (data.ViewId > 0)
                            {
                                view = doc.GetElement(Utils.ElementIdExtensions.FromLong(data.ViewId)) as View;
                                if (view == null)
                                    throw new ArgumentException($"View with ID {data.ViewId} not found");
                            }
                            else
                            {
                                view = doc.ActiveView;
                            }

                            ElementId typeId = data.TextNoteTypeId > 0
                                ? Utils.ElementIdExtensions.FromLong(data.TextNoteTypeId)
                                : defaultTextNoteTypeId;

                            XYZ position = JZPoint.ToXYZ(data.Position);

                            var options = new TextNoteOptions
                            {
                                TypeId = typeId,
                                HorizontalAlignment = ParseAlignment(data.HorizontalAlignment)
                            };

                            var textNote = TextNote.Create(doc, view.Id, position, data.Text, options);

                            // Set width if specified
                            if (data.Width > 0)
                            {
                                textNote.Width = data.Width / 304.8;
                            }

                            successCount++;
                            results.Add(new
                            {
#if REVIT2024_OR_GREATER
                                textNoteId = textNote.Id.Value,
#else
                                textNoteId = textNote.Id.IntegerValue,
#endif
                                text = data.Text,
                                success = true
                            });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new
                            {
                                textNoteId = -1L,
                                text = data.Text,
                                success = false,
                                error = ex.Message
                            });
                        }
                    }

                    transaction.Commit();
                    }
                    catch
                    {
                        if (transaction.GetStatus() == TransactionStatus.Started)
                            transaction.RollBack();
                        throw;
                    }
                }

                Result = new AIResult<List<object>>
                {
                    Success = true,
                    Message = $"Created {successCount} text notes",
                    Response = results
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<List<object>>
                {
                    Success = false,
                    Message = $"Failed to create text notes: {ex.Message}",
                    Response = new List<object>()
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private HorizontalTextAlignment ParseAlignment(string alignment)
        {
            switch (alignment?.ToLower())
            {
                case "center": return HorizontalTextAlignment.Center;
                case "right": return HorizontalTextAlignment.Right;
                default: return HorizontalTextAlignment.Left;
            }
        }

        public string GetName() => "Create Text Notes";
    }

    public class TextNoteData
    {
        public string Text { get; set; }
        public JZPoint Position { get; set; }
        public long ViewId { get; set; } = -1;
        public long TextNoteTypeId { get; set; } = -1;
        public string HorizontalAlignment { get; set; } = "Left";
        public double Width { get; set; } = 0;
    }
}
