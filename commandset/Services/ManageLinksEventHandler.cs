using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ManageLinksEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Action { get; set; } = "list";
        public long LinkId { get; set; } = 0;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(string action, long linkId = 0)
        {
            Action = action;
            LinkId = linkId;
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

                switch (Action.ToLower())
                {
                    case "list":
                        ExecuteList(doc);
                        break;
                    case "reload":
                        ExecuteReload(doc);
                        break;
                    case "unload":
                        ExecuteUnload(doc);
                        break;
                    default:
                        throw new ArgumentException($"Unknown action: {Action}");
                }
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Manage links failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void ExecuteList(Document doc)
        {
            var linkTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkType))
                .Cast<RevitLinkType>()
                .ToList();

            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            var links = new List<object>();
            foreach (var linkType in linkTypes)
            {
                var instances = linkInstances
                    .Where(i => i.GetTypeId() == linkType.Id)
                    .ToList();

                var externalRef = linkType.GetExternalFileReference();
                string path = externalRef != null ? ModelPathUtils.ConvertModelPathToUserVisiblePath(externalRef.GetAbsolutePath()) : "";
                string status = externalRef != null ? externalRef.GetLinkedFileStatus().ToString() : "Unknown";

                links.Add(new
                {
#if REVIT2024_OR_GREATER
                    linkTypeId = linkType.Id.Value,
#else
                    linkTypeId = linkType.Id.IntegerValue,
#endif
                    name = linkType.Name,
                    path,
                    status,
                    isLoaded = RevitLinkType.IsLoaded(doc, linkType.Id),
                    instanceCount = instances.Count,
                    instances = instances.Select(i => new
                    {
#if REVIT2024_OR_GREATER
                        id = i.Id.Value,
#else
                        id = i.Id.IntegerValue,
#endif
                        name = i.Name
                    }).ToList()
                });
            }

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Found {links.Count} linked models",
                Response = links
            };
        }

        private void ExecuteReload(Document doc)
        {
            if (LinkId == 0)
                throw new ArgumentException("linkId is required for reload action");

            var linkTypeId = ToElementId(LinkId);
            var linkType = doc.GetElement(linkTypeId) as RevitLinkType;
            if (linkType == null)
                throw new ArgumentException($"Link type with ID {LinkId} not found");

            linkType.Reload();

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Successfully reloaded link '{linkType.Name}'"
            };
        }

        private void ExecuteUnload(Document doc)
        {
            if (LinkId == 0)
                throw new ArgumentException("linkId is required for unload action");

            var linkTypeId = ToElementId(LinkId);
            var linkType = doc.GetElement(linkTypeId) as RevitLinkType;
            if (linkType == null)
                throw new ArgumentException($"Link type with ID {LinkId} not found");

            linkType.Unload(null);

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Successfully unloaded link '{linkType.Name}'"
            };
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Manage Links";
    }
}
