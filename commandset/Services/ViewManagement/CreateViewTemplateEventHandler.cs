using System;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class CreateViewTemplateEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public long? SourceViewId { get; set; }
        public string SourceViewName { get; set; }
        public string TemplateName { get; set; }

        public AIResult<object> Result { get; private set; }
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public void SetParameters() { TaskCompleted = false; _resetEvent.Reset(); }
        public bool WaitForCompletion(int timeoutMilliseconds = 30000) { return _resetEvent.WaitOne(timeoutMilliseconds); }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // 1. Find source view
                View sourceView = null;

                if (SourceViewId.HasValue)
                {
                    var elemId = RevitMCPCommandSet.Utils.ElementIdExtensions.FromLong(SourceViewId.Value);
                    sourceView = doc.GetElement(elemId) as View;
                    if (sourceView == null)
                    {
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message = $"View with ID {SourceViewId.Value} not found"
                        };
                        return;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(SourceViewName))
                {
                    sourceView = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate)
                        .FirstOrDefault(v => v.Name.IndexOf(SourceViewName, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (sourceView == null)
                    {
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message = $"No view found matching name '{SourceViewName}'"
                        };
                        return;
                    }
                }
                else
                {
                    // Use active view
                    sourceView = app.ActiveUIDocument.ActiveView;
                    if (sourceView == null)
                    {
                        Result = new AIResult<object>
                        {
                            Success = false,
                            Message = "No active view available"
                        };
                        return;
                    }
                }

                // 2. Check if the view is valid for template creation
                if (!sourceView.IsViewValidForTemplateCreation())
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"View '{sourceView.Name}' is not valid for template creation (e.g., schedules, sheets, or other special views cannot be used)"
                    };
                    return;
                }

                // 3. Check if a template with the same name already exists
                var existingTemplate = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && v.Name.Equals(TemplateName, StringComparison.OrdinalIgnoreCase));

                if (existingTemplate != null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = $"A view template named '{TemplateName}' already exists (ID: {existingTemplate.Id.GetValue()})"
                    };
                    return;
                }

                // 4. Create the view template
                using (var tx = new Transaction(doc, "Create View Template"))
                {
                    tx.Start();

                    var template = sourceView.CreateViewTemplate();

                    // 5. Rename the template
                    template.Name = TemplateName;

                    tx.Commit();

                    Result = new AIResult<object>
                    {
                        Success = true,
                        Message = $"View template '{TemplateName}' created successfully from view '{sourceView.Name}'",
                        Response = new
                        {
                            templateId = template.Id.GetValue(),
                            templateName = template.Name,
                            sourceViewId = sourceView.Id.GetValue(),
                            sourceViewName = sourceView.Name,
                            viewType = sourceView.ViewType.ToString()
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Create view template failed: {ex.Message}"
                };
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        public string GetName() => "Create View Template";
    }
}
