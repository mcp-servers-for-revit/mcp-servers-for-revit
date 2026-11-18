using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class ManageViewTemplatesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Action { get; set; } = "list";
        public List<long> TemplateIds { get; set; } = new();
        public string NewName { get; set; } = "";
        public string FindText { get; set; } = "";
        public string ReplaceText { get; set; } = "";
        public string FilterViewType { get; set; } = "";
        public object Result { get; private set; }

        public void SetParameters(string action, List<long> templateIds, string newName,
            string findText, string replaceText, string filterViewType)
        {
            Action = action;
            TemplateIds = templateIds;
            NewName = newName;
            FindText = findText;
            ReplaceText = replaceText;
            FilterViewType = filterViewType;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                switch (Action.ToLower())
                {
                    case "list":
                        Result = ListTemplates(doc);
                        break;
                    case "duplicate":
                        Result = DuplicateTemplates(doc);
                        break;
                    case "delete":
                        Result = DeleteTemplates(doc);
                        break;
                    case "rename":
                        Result = RenameTemplates(doc);
                        break;
                    case "batch_rename":
                        Result = BatchRenameTemplates(doc);
                        break;
                    default:
                        Result = new { success = false, error = $"Unknown action: {Action}" };
                        break;
                }
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private object ListTemplates(Document doc)
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
                    viewType = v.ViewType.ToString(),
                    discipline = v.Discipline.ToString(),
                    hasAssociatedViews = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Any(av => !av.IsTemplate && av.ViewTemplateId == v.Id)
                })
                .Where(t => string.IsNullOrEmpty(FilterViewType) ||
                    t.viewType.Equals(FilterViewType, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.viewType).ThenBy(t => t.name)
                .ToList();

            return new { success = true, count = templates.Count, templates };
        }

        private object DuplicateTemplates(Document doc)
        {
            var duplicated = new List<object>();
            using (var tx = new Transaction(doc, "Duplicate View Templates"))
            {
                tx.Start();
                foreach (var id in TemplateIds)
                {
#if REVIT2024_OR_GREATER
                    var elemId = new ElementId(id);
#else
                    var elemId = new ElementId((int)id);
#endif
                    var view = doc.GetElement(elemId) as View;
                    if (view == null || !view.IsTemplate) continue;

                    var newId = view.Duplicate(ViewDuplicateOption.Duplicate);
                    var newView = doc.GetElement(newId) as View;
                    if (newView != null && !string.IsNullOrEmpty(NewName))
                    {
                        newView.Name = TemplateIds.Count == 1 ? NewName : $"{NewName} - Copy {duplicated.Count + 1}";
                    }
                    duplicated.Add(new
                    {
#if REVIT2024_OR_GREATER
                        originalId = id, newId = newId.Value,
#else
                        originalId = id, newId = newId.IntegerValue,
#endif
                        name = newView?.Name ?? ""
                    });
                }
                tx.Commit();
            }
            return new { success = true, duplicated = duplicated.Count, templates = duplicated };
        }

        private object DeleteTemplates(Document doc)
        {
            int deleted = 0;
            using (var tx = new Transaction(doc, "Delete View Templates"))
            {
                tx.Start();
                foreach (var id in TemplateIds)
                {
#if REVIT2024_OR_GREATER
                    var elemId = new ElementId(id);
#else
                    var elemId = new ElementId((int)id);
#endif
                    var view = doc.GetElement(elemId) as View;
                    if (view == null || !view.IsTemplate) continue;
                    doc.Delete(elemId);
                    deleted++;
                }
                tx.Commit();
            }
            return new { success = true, deleted };
        }

        private object RenameTemplates(Document doc)
        {
            if (TemplateIds.Count != 1)
                return new { success = false, error = "Rename requires exactly one template ID" };

#if REVIT2024_OR_GREATER
            var elemId = new ElementId(TemplateIds[0]);
#else
            var elemId = new ElementId((int)TemplateIds[0]);
#endif
            var view = doc.GetElement(elemId) as View;
            if (view == null || !view.IsTemplate)
                return new { success = false, error = "Template not found" };

            using (var tx = new Transaction(doc, "Rename View Template"))
            {
                tx.Start();
                string oldName = view.Name;
                view.Name = NewName;
                tx.Commit();
                return new { success = true, oldName, newName = NewName };
            }
        }

        private object BatchRenameTemplates(Document doc)
        {
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && v.Name.Contains(FindText))
                .ToList();

            if (TemplateIds.Count > 0)
            {
                var idSet = new HashSet<long>(TemplateIds);
                templates = templates.Where(v =>
                {
#if REVIT2024_OR_GREATER
                    return idSet.Contains(v.Id.Value);
#else
                    return idSet.Contains(v.Id.IntegerValue);
#endif
                }).ToList();
            }

            int renamed = 0;
            using (var tx = new Transaction(doc, "Batch Rename View Templates"))
            {
                tx.Start();
                foreach (var v in templates)
                {
                    v.Name = v.Name.Replace(FindText, ReplaceText);
                    renamed++;
                }
                tx.Commit();
            }
            return new { success = true, renamed };
        }

        public string GetName() => "Manage View Templates";
    }
}
