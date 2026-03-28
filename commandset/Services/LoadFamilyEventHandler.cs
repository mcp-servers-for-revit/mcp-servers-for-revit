using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class LoadFamilyEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Action { get; set; } = "list";
        public string FamilyPath { get; set; } = "";
        public string CategoryFilter { get; set; } = "";
        public long SourceTypeId { get; set; } = 0;
        public string NewTypeName { get; set; } = "";
        public AIResult<object> Result { get; private set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                switch (Action.ToLower())
                {
                    case "load":
                        ExecuteLoad(doc);
                        break;
                    case "list":
                        ExecuteList(doc);
                        break;
                    case "duplicate_type":
                        ExecuteDuplicateType(doc);
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
                    Message = $"Load family failed: {ex.Message}"
                };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private void ExecuteLoad(Document doc)
        {
            if (string.IsNullOrEmpty(FamilyPath))
                throw new ArgumentException("familyPath is required for 'load' action");

            if (!System.IO.File.Exists(FamilyPath))
                throw new System.IO.FileNotFoundException($"Family file not found: {FamilyPath}");

            Family family;
            using (var transaction = new Transaction(doc, "Load Family"))
            {
                transaction.Start();
                bool loaded = doc.LoadFamily(FamilyPath, out family);
                transaction.Commit();

                if (!loaded || family == null)
                {
                    Result = new AIResult<object>
                    {
                        Success = false,
                        Message = "Family could not be loaded (may already exist in the project)"
                    };
                    return;
                }
            }

            var types = new List<object>();
            foreach (var typeId in family.GetFamilySymbolIds())
            {
                var symbol = doc.GetElement(typeId) as FamilySymbol;
                if (symbol != null)
                {
                    types.Add(new
                    {
                        id = typeId.Value,
                        name = symbol.Name
                    });
                }
            }

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Successfully loaded family '{family.Name}' with {types.Count} types",
                Response = new
                {
                    familyId = family.Id.Value,
                    familyName = family.Name,
                    category = family.FamilyCategory?.Name ?? "",
                    types
                }
            };
        }

        private void ExecuteList(Document doc)
        {
            var families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => string.IsNullOrEmpty(CategoryFilter) ||
                       (f.FamilyCategory?.Name ?? "").Contains(CategoryFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.FamilyCategory?.Name ?? "")
                .ThenBy(f => f.Name)
                .ToList();

            var result = new List<object>();
            foreach (var family in families)
            {
                var typeCount = family.GetFamilySymbolIds().Count;
                result.Add(new
                {
                    id = family.Id.Value,
                    name = family.Name,
                    category = family.FamilyCategory?.Name ?? "",
                    typeCount,
                    isEditable = family.IsEditable
                });
            }

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Found {result.Count} families" + (string.IsNullOrEmpty(CategoryFilter) ? "" : $" matching '{CategoryFilter}'"),
                Response = result
            };
        }

        private void ExecuteDuplicateType(Document doc)
        {
            if (SourceTypeId == 0)
                throw new ArgumentException("sourceTypeId is required for 'duplicate_type' action");
            if (string.IsNullOrEmpty(NewTypeName))
                throw new ArgumentException("newTypeName is required for 'duplicate_type' action");

            var sourceId = ToElementId(SourceTypeId);
            var sourceType = doc.GetElement(sourceId) as FamilySymbol;
            if (sourceType == null)
                throw new ArgumentException($"Family type with ID {SourceTypeId} not found");

            ElementType newType;
            using (var transaction = new Transaction(doc, "Duplicate Family Type"))
            {
                transaction.Start();
                newType = sourceType.Duplicate(NewTypeName) as ElementType;
                transaction.Commit();
            }

            Result = new AIResult<object>
            {
                Success = true,
                Message = $"Duplicated type '{sourceType.Name}' as '{NewTypeName}'",
                Response = new
                {
                    newTypeId = newType.Id.Value,
                    newTypeName = newType.Name,
                    familyName = sourceType.FamilyName,
                    category = sourceType.Category?.Name ?? ""
                }
            };
        }

        private ElementId ToElementId(long id)
        {
            return new ElementId(id);
        }

        public string GetName() => "Load Family";
    }
}
