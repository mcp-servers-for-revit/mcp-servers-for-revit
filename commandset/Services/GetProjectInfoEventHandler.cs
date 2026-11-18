using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetProjectInfoEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool IncludePhases { get; set; } = true;
        public bool IncludeWorksets { get; set; } = true;
        public bool IncludeLinks { get; set; } = true;
        public bool IncludeLevels { get; set; } = true;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(bool includePhases = true, bool includeWorksets = true, bool includeLinks = true, bool includeLevels = true)
        {
            IncludePhases = includePhases;
            IncludeWorksets = includeWorksets;
            IncludeLinks = includeLinks;
            IncludeLevels = includeLevels;
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
                var projectInfo = doc.ProjectInformation;

                var result = new Dictionary<string, object>();

                // Basic project info
                result["projectName"] = projectInfo?.Name ?? "";
                result["projectNumber"] = projectInfo?.Number ?? "";
                result["projectAddress"] = projectInfo?.Address ?? "";
                result["buildingName"] = projectInfo?.BuildingName ?? "";
                result["author"] = projectInfo?.Author ?? "";
                result["organizationName"] = projectInfo?.OrganizationName ?? "";
                result["organizationDescription"] = projectInfo?.OrganizationDescription ?? "";
                result["issueDate"] = projectInfo?.IssueDate ?? "";
                result["status"] = projectInfo?.Status ?? "";
                result["filePath"] = doc.PathName ?? "";
                result["isWorkshared"] = doc.IsWorkshared;

                // Phases
                if (IncludePhases)
                {
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
                }

                // Worksets
                if (IncludeWorksets && doc.IsWorkshared)
                {
                    var worksets = new List<object>();
                    var wsCollector = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset);

                    foreach (var ws in wsCollector)
                    {
                        worksets.Add(new
                        {
                            id = ws.Id.IntegerValue,
                            name = ws.Name,
                            isOpen = ws.IsOpen,
                            isEditable = ws.IsEditable,
                            owner = ws.Owner
                        });
                    }
                    result["worksets"] = worksets;
                }

                // Revit Links
                if (IncludeLinks)
                {
                    var links = new List<object>();
                    var linkCollector = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitLinkInstance));

                    foreach (RevitLinkInstance link in linkCollector)
                    {
                        var linkType = doc.GetElement(link.GetTypeId()) as RevitLinkType;
                        links.Add(new
                        {
#if REVIT2024_OR_GREATER
                            id = link.Id.Value,
#else
                            id = link.Id.IntegerValue,
#endif
                            name = link.Name,
                            isLoaded = linkType != null && RevitLinkType.IsLoaded(doc, linkType.Id),
                            linkPath = GetLinkPath(linkType)
                        });
                    }
                    result["links"] = links;
                }

                // Levels
                if (IncludeLevels)
                {
                    var levels = new List<object>();
                    var levelCollector = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation);

                    foreach (var level in levelCollector)
                    {
                        levels.Add(new
                        {
#if REVIT2024_OR_GREATER
                            id = level.Id.Value,
#else
                            id = level.Id.IntegerValue,
#endif
                            name = level.Name,
                            elevation = level.Elevation * 304.8 // feet to mm
                        });
                    }
                    result["levels"] = levels;
                }

                Result = new AIResult<object>
                {
                    Success = true,
                    Message = "Project info retrieved successfully",
                    Response = result
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = $"Failed to get project info: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private static string GetLinkPath(RevitLinkType linkType)
        {
            if (linkType == null) return "";
            try
            {
                var externalRef = linkType.GetExternalFileReference();
                if (externalRef == null) return "";
                var modelPath = externalRef.GetAbsolutePath();
                return ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
            }
            catch
            {
                return "";
            }
        }

        public string GetName() => "Get Project Info";
    }
}
