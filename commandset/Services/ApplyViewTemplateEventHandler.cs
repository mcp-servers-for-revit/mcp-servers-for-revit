using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ApplyViewTemplateEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> ViewIds { get; set; } = new List<long>();
        public long TemplateId { get; set; } = 0;
        public string TemplateName { get; set; } = "";
        public string Action { get; set; } = "apply";
        public AIResult<object> Result { get; private set; }

        public void SetParameters(List<long> viewIds, long templateId, string templateName, string action)
        {
            ViewIds = viewIds ?? new List<long>();
            TemplateId = templateId;
            TemplateName = templateName ?? "";
            Action = action ?? "apply";
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

                if (Action == "list")
                {
                    ListTemplates(doc);
                    return;
                }

                // Resolve template
                ElementId templateElemId = ResolveTemplate(doc);

                if (Action == "remove")
                {
                    RemoveTemplates(doc);
                    return;
                }

                if (templateElemId == null || templateElemId == ElementId.InvalidElementId)
                {
                    Result = new AIResult<object> { Success = false, Message = "View template not found" };
                    return;
                }

                ApplyTemplate(doc, templateElemId);
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"View template operation failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void ListTemplates(Document doc)
        {
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .Select(v => new
                {
#if REVIT2024_OR_GREATER
                    id = v.Id.Value,
#else
                    id = v.Id.IntegerValue,
#endif
                    name = v.Name,
                    viewType = v.ViewType.ToString()
                })
                .ToList();

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Found {templates.Count} view templates",
                Response = templates
            };
        }

        private void ApplyTemplate(Document doc, ElementId templateId)
        {
            var results = new List<object>();
            int successCount = 0;

            using (var transaction = new Transaction(doc, "Apply View Template"))
            {
                transaction.Start();
                try
                {
                    foreach (var viewId in ViewIds)
                    {
                        var view = doc.GetElement(ToElementId(viewId)) as View;
                        if (view == null || view.IsTemplate) continue;

                        try
                        {
                            view.ViewTemplateId = templateId;
                            successCount++;
                            results.Add(new
                            {
                                viewId,
                                viewName = view.Name,
                                success = true
                            });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { viewId, viewName = view.Name, success = false, message = ex.Message });
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

            var template = doc.GetElement(templateId);
            Result = new AIResult<object>
            {
                Success = successCount > 0,
                Message = $"Applied template '{template?.Name}' to {successCount}/{ViewIds.Count} views",
                Response = new { templateName = template?.Name, successCount, results }
            };
        }

        private void RemoveTemplates(Document doc)
        {
            int successCount = 0;

            using (var transaction = new Transaction(doc, "Remove View Template"))
            {
                transaction.Start();
                try
                {
                    foreach (var viewId in ViewIds)
                    {
                        var view = doc.GetElement(ToElementId(viewId)) as View;
                        if (view == null || view.IsTemplate) continue;

                        view.ViewTemplateId = ElementId.InvalidElementId;
                        successCount++;
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

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Removed template from {successCount} views"
            };
        }

        private ElementId ResolveTemplate(Document doc)
        {
            if (TemplateId > 0)
            {
                var id = ToElementId(TemplateId);
                var view = doc.GetElement(id) as View;
                if (view != null && view.IsTemplate) return id;
            }

            if (!string.IsNullOrEmpty(TemplateName))
            {
                var template = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && v.Name.Equals(TemplateName, StringComparison.OrdinalIgnoreCase));
                if (template != null) return template.Id;
            }

            return ElementId.InvalidElementId;
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Apply View Template";
    }
}
