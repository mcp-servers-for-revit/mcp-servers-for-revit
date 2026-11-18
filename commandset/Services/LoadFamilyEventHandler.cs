using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Helpers;
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

        public void SetParameters(string action, string familyPath, string categoryFilter, long sourceTypeId, string newTypeName)
        {
            Action = action ?? "list";
            FamilyPath = familyPath ?? "";
            CategoryFilter = categoryFilter ?? "";
            SourceTypeId = sourceTypeId;
            NewTypeName = newTypeName ?? "";
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

            if (!ConfirmationHelper.Confirm("load family into", 1))
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = "Operation cancelled by user",
                    Response = new { cancelled = true }
                };
                return;
            }

            Family family;
            using (var transaction = new Transaction(doc, "Load Family"))
            {
                transaction.Start();
                try
                {
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
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                        transaction.RollBack();
                    throw;
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
#if REVIT2024_OR_GREATER
                        id = typeId.Value,
#else
                        id = typeId.IntegerValue,
#endif
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
#if REVIT2024_OR_GREATER
                    familyId = family.Id.Value,
#else
                    familyId = family.Id.IntegerValue,
#endif
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
                       (f.FamilyCategory?.Name ?? "").IndexOf(CategoryFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(f => f.FamilyCategory?.Name ?? "")
                .ThenBy(f => f.Name)
                .ToList();

            var result = new List<object>();
            foreach (var family in families)
            {
                var typeCount = family.GetFamilySymbolIds().Count;
                result.Add(new
                {
#if REVIT2024_OR_GREATER
                    id = family.Id.Value,
#else
                    id = family.Id.IntegerValue,
#endif
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

            if (!ConfirmationHelper.Confirm("duplicate type in", 1))
            {
                Result = new AIResult<object>
                {
                    Success = false,
                    Message = "Operation cancelled by user",
                    Response = new { cancelled = true }
                };
                return;
            }

            ElementType newType;
            using (var transaction = new Transaction(doc, "Duplicate Family Type"))
            {
                transaction.Start();
                try
                {
                    newType = sourceType.Duplicate(NewTypeName) as ElementType;
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
                Message = $"Duplicated type '{sourceType.Name}' as '{NewTypeName}'",
                Response = new
                {
#if REVIT2024_OR_GREATER
                    newTypeId = newType.Id.Value,
#else
                    newTypeId = newType.Id.IntegerValue,
#endif
                    newTypeName = newType.Name,
                    familyName = sourceType.FamilyName,
                    category = sourceType.Category?.Name ?? ""
                }
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

        public string GetName() => "Load Family";
    }
}
