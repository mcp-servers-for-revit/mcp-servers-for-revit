using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Common;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class RenumberElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> ElementIds { get; set; } = new List<long>();
        public string TargetCategory { get; set; } = "";
        public string ParameterName { get; set; } = "";
        public int StartNumber { get; set; } = 1;
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";
        public int Increment { get; set; } = 1;
        public string SortBy { get; set; } = "location";
        public bool DryRun { get; set; } = true;
        public AIResult<object> Result { get; private set; }

        public void SetParameters(List<long> elementIds, string targetCategory, string parameterName, int startNumber, string prefix, string suffix, int increment, string sortBy, bool dryRun)
        {
            ElementIds = elementIds ?? new List<long>();
            TargetCategory = targetCategory ?? "";
            ParameterName = parameterName ?? "";
            StartNumber = startNumber;
            Prefix = prefix ?? "";
            Suffix = suffix ?? "";
            Increment = increment;
            SortBy = sortBy ?? "location";
            DryRun = dryRun;
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
                var elements = GetTargetElements(doc);

                if (elements.Count == 0)
                {
                    Result = new AIResult<object> { Success = false, Message = "No elements found to renumber" };
                    return;
                }

                // Sort elements
                elements = SortElements(elements);

                var renumberResults = new List<object>();
                int currentNumber = StartNumber;

                using (var transaction = DryRun ? null : new Transaction(doc, "Renumber Elements"))
                {
                    transaction?.Start();

                    foreach (var elem in elements)
                    {
                        string newValue = $"{Prefix}{currentNumber}{Suffix}";
                        string oldValue = GetCurrentNumber(elem);
                        bool success = true;
                        string message = "";

                        if (!DryRun)
                        {
                            try
                            {
                                SetElementNumber(elem, newValue);
                            }
                            catch (Exception ex)
                            {
                                success = false;
                                message = ex.Message;
                            }
                        }

                        renumberResults.Add(new
                        {
#if REVIT2024_OR_GREATER
                            id = elem.Id.Value,
#else
                            id = elem.Id.IntegerValue,
#endif
                            oldValue,
                            newValue,
                            success,
                            message
                        });

                        currentNumber += Increment;
                    }

                    transaction?.Commit();
                }

                int successCount = renumberResults.Count;
                Result = new AIResult<object>
                {
                    Success = true,
                    Message = DryRun
                        ? $"Preview: {successCount} elements would be renumbered (dry run)"
                        : $"Renumbered {successCount} elements",
                    Response = new { dryRun = DryRun, totalProcessed = renumberResults.Count, renames = renumberResults }
                };
            }
            catch (Exception ex)
            {
                Result = new AIResult<object> { Success = false, Message = $"Renumber failed: {ex.Message}" };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private List<Element> GetTargetElements(Document doc)
        {
            if (ElementIds.Count > 0)
            {
                return ElementIds
                    .Select(id => doc.GetElement(ToElementId(id)))
                    .Where(e => e != null)
                    .ToList();
            }

            switch (TargetCategory)
            {
                case "Rooms":
                    return new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .ToList();
                case "Doors":
                    return new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .WhereElementIsNotElementType()
                        .ToList();
                case "Windows":
                    return new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Windows)
                        .WhereElementIsNotElementType()
                        .ToList();
                case "Parking":
                    return new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Parking)
                        .WhereElementIsNotElementType()
                        .ToList();
                default:
                    return new List<Element>();
            }
        }

        private List<Element> SortElements(List<Element> elements)
        {
            if (SortBy == "location")
            {
                return elements.OrderBy(e =>
                {
                    if (e.Location is LocationPoint lp) return lp.Point.X + lp.Point.Y * 10000;
                    if (e.Location is LocationCurve lc) return lc.Curve.GetEndPoint(0).X + lc.Curve.GetEndPoint(0).Y * 10000;
                    return 0.0;
                }).ToList();
            }

            if (SortBy == "name")
            {
                return elements.OrderBy(e => e.Name).ToList();
            }

            return elements;
        }

        private string GetCurrentNumber(Element elem)
        {
            if (!string.IsNullOrEmpty(ParameterName))
            {
                var param = elem.LookupParameter(ParameterName);
                return param?.AsString() ?? param?.AsValueString() ?? "";
            }

            if (elem is Room room) return room.Number;
            var numberParam = elem.get_Parameter(BuiltInParameter.DOOR_NUMBER)
                              ?? elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            return numberParam?.AsString() ?? "";
        }

        private void SetElementNumber(Element elem, string value)
        {
            if (!string.IsNullOrEmpty(ParameterName))
            {
                var param = elem.LookupParameter(ParameterName);
                if (param != null && !param.IsReadOnly) { param.Set(value); return; }
            }

            if (elem is Room room) { room.Number = value; return; }

            var numberParam = elem.get_Parameter(BuiltInParameter.DOOR_NUMBER)
                              ?? elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (numberParam != null && !numberParam.IsReadOnly)
                numberParam.Set(value);
            else
                throw new Exception($"Cannot find writable number parameter on element {elem.Id}");
        }

        private ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId((int)id);
#endif
        }

        public string GetName() => "Renumber Elements";
    }
}
