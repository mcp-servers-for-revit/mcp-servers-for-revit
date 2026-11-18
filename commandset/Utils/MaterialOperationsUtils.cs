using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models;
using System.Globalization;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Het eigenlijke Revit-werk van manage_materials: NAAKT-opties geven, materialen opsommen,
    /// aanmaken en aanpassen. Werkt op een Document en kent geen ExternalEvent, zodat het ook zonder
    /// plugin getest kan worden (zie tests/commandset/MaterialOperationsTests.cs).
    ///
    /// Create en update werken in twee fases: eerst worden alle items gecontroleerd (NAAKT-naam, naam
    /// al in gebruik, kleuren, vulpatronen, sjabloon) en pas daarna, als het geen dryRun is, doorgevoerd.
    /// Zo geeft een dryRun exact hetzelfde resultaat als de echte aanroep. Bij het doorvoeren draait elk
    /// item in een eigen SubTransaction: een mislukt item wordt teruggedraaid, de rest gaat door.
    /// </summary>
    public static class MaterialOperationsUtils
    {
        private static readonly string[] ListFilters = { "all", "valid", "invalid", "non-naakt" };

        public static ManageMaterialsResult Execute(Document doc, ManageMaterialsRequest request)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (request == null) throw new ArgumentNullException(nameof(request));

            switch ((request.Action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "options":
                    return Options(doc, request);
                case "list":
                    return List(doc, request);
                case "create":
                    return Create(doc, request);
                case "update":
                    return Update(doc, request);
                default:
                    throw new ArgumentException(
                        $"Onbekende actie '{request.Action}'. Kies uit: options, list, create, update");
            }
        }

        // -----------------------------------------------------------------------------------------
        // options
        // -----------------------------------------------------------------------------------------

        private static ManageMaterialsResult Options(Document doc, ManageMaterialsRequest request)
        {
            NaaktOptionsResult options = NaaktCatalogUtils.GetOptions(request.MainGroup);

            var result = new ManageMaterialsResult
            {
                Action = "options",
                MainGroups = options.MainGroups,
                MainGroup = options.MainGroup,
                Characteristics = options.Characteristics,
                Applications = options.Applications
            };

            if (request.IncludeFillPatterns)
            {
                result.FillPatterns = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .Select(f => f.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return result;
        }

        // -----------------------------------------------------------------------------------------
        // list
        // -----------------------------------------------------------------------------------------

        private static ManageMaterialsResult List(Document doc, ManageMaterialsRequest request)
        {
            string filter = (request.Filter ?? "all").Trim().ToLowerInvariant();
            if (!ListFilters.Contains(filter))
            {
                throw new ArgumentException($"Onbekend filter '{request.Filter}'. Kies uit: {string.Join(", ", ListFilters)}");
            }

            var matches = new List<KeyValuePair<Material, NaaktCheckResult>>();
            foreach (Material material in GetMaterials(doc))
            {
                if (!string.IsNullOrWhiteSpace(request.NameContains) &&
                    material.Name.IndexOf(request.NameContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                NaaktCheckResult check = NaaktCatalogUtils.Check(material.Name);
                bool include;
                switch (filter)
                {
                    case "valid":
                        include = check.Valid;
                        break;
                    case "invalid":
                        include = !check.Valid;
                        break;
                    case "non-naakt":
                        include = !check.Valid && !NaaktCatalogUtils.StartsWithMainGroup(material.Name);
                        break;
                    default:
                        include = true;
                        break;
                }

                if (include)
                {
                    matches.Add(new KeyValuePair<Material, NaaktCheckResult>(material, check));
                }
            }

            IEnumerable<KeyValuePair<Material, NaaktCheckResult>> page = matches;
            if (request.Offset.HasValue && request.Offset.Value > 0) page = page.Skip(request.Offset.Value);
            if (request.Limit.HasValue && request.Limit.Value >= 0) page = page.Take(request.Limit.Value);

            List<MaterialInfo> infos = page.Select(m => ToInfo(doc, m.Key, m.Value)).ToList();

            return new ManageMaterialsResult
            {
                Action = "list",
                Total = matches.Count,
                Count = infos.Count,
                Materials = infos
            };
        }

        private static MaterialInfo ToInfo(Document doc, Material material, NaaktCheckResult check)
        {
            return new MaterialInfo
            {
                Id = material.Id.GetValue().ToString(CultureInfo.InvariantCulture),
                Name = material.Name,
                Naakt = check,
                MaterialClass = string.IsNullOrEmpty(material.MaterialClass) ? null : material.MaterialClass,
                Color = FormatColor(material.Color),
                SurfaceForeground = PatternInfo(doc, material.SurfaceForegroundPatternId, material.SurfaceForegroundPatternColor),
                SurfaceBackground = PatternInfo(doc, material.SurfaceBackgroundPatternId, material.SurfaceBackgroundPatternColor),
                CutForeground = PatternInfo(doc, material.CutForegroundPatternId, material.CutForegroundPatternColor),
                CutBackground = PatternInfo(doc, material.CutBackgroundPatternId, material.CutBackgroundPatternColor),
                AppearanceAsset = ElementName(doc, material.AppearanceAssetId),
                StructuralAsset = ElementName(doc, material.StructuralAssetId),
                ThermalAsset = ElementName(doc, material.ThermalAssetId)
            };
        }

        private static MaterialPatternInfo PatternInfo(Document doc, ElementId patternId, Color color)
        {
            string name = ElementName(doc, patternId);
            string colorText = FormatColor(color);
            if (name == null && colorText == null) return null;
            return new MaterialPatternInfo { Pattern = name, Color = colorText };
        }

        private static string ElementName(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return null;
            return doc.GetElement(id)?.Name;
        }

        private static string FormatColor(Color color)
        {
            if (color == null || !color.IsValid) return null;
            return $"{color.Red},{color.Green},{color.Blue}";
        }

        // -----------------------------------------------------------------------------------------
        // create
        // -----------------------------------------------------------------------------------------

        private class PlannedItem
        {
            public MaterialItemResult Item;
            public Material Material;       // alleen update
            public string NewName;          // create: altijd; update: alleen als de naam wijzigt
            public ResolvedProperties Props;
        }

        private static ManageMaterialsResult Create(Document doc, ManageMaterialsRequest request)
        {
            List<MaterialInput> inputs = request.Materials;
            if (inputs == null || inputs.Count == 0)
            {
                throw new ArgumentException("Voor 'create' is een niet-lege lijst 'materials' nodig");
            }

            var lookups = new Lookups(doc);
            var takenNames = new HashSet<string>(lookups.MaterialsByName.Keys, StringComparer.OrdinalIgnoreCase);
            var results = new List<MaterialItemResult>();
            var planned = new List<PlannedItem>();

            for (int i = 0; i < inputs.Count; i++)
            {
                MaterialInput input = inputs[i];
                var item = new MaterialItemResult { Index = i, Status = "failed" };
                results.Add(item);

                if (input == null)
                {
                    item.Message = "Leeg item";
                    continue;
                }

                NaaktCheckResult check = NaaktCatalogUtils.BuildAndCheck(
                    input.MainGroup, input.Characteristic, input.Application, input.Custom);
                item.NewName = check.Name;

                if (!check.Valid)
                {
                    FillNaaktFailure(item, check);
                    continue;
                }

                if (takenNames.Contains(check.Name))
                {
                    item.Message = "Er bestaat al een materiaal met deze naam";
                    continue;
                }

                var errors = new List<string>();
                ResolvedProperties props = ResolveProperties(doc, input, lookups, errors);
                if (errors.Count > 0)
                {
                    item.Message = string.Join("; ", errors);
                    continue;
                }

                takenNames.Add(check.Name);
                item.Status = "created";
                item.Success = true;
                item.Changes = new List<string> { "name" };
                item.Changes.AddRange(props.Describe());
                planned.Add(new PlannedItem { Item = item, NewName = check.Name, Props = props });
            }

            if (!request.DryRun && planned.Count > 0)
            {
                ApplyInTransaction(doc, "Materialen aanmaken (NAAKT)", planned, plan =>
                {
                    // Met sjabloon: Revit dupliceert het hele materiaal (alle assets, identiteitsgegevens,
                    // weergave, ...). De assets moeten gedeeld blijven met het sjabloon; mocht Duplicate
                    // toch kopieën maken, dan worden die hier vervangen en opgeruimd.
                    var templateWarnings = new List<string>();
                    Material material;
                    if (plan.Props.Template != null)
                    {
                        material = plan.Props.Template.Duplicate(plan.NewName);
                        ShareAssets(material, plan.Props.Template, deleteReplaced: true, templateWarnings);
                    }
                    else
                    {
                        material = (Material)doc.GetElement(Material.Create(doc, plan.NewName));
                    }

                    plan.Props.ApplyTo(material, copyTemplate: false, out List<string> warnings);
                    plan.Item.Id = material.Id.GetValue().ToString(CultureInfo.InvariantCulture);
                    AddWarnings(plan.Item, templateWarnings.Concat(warnings).ToList());
                });
            }

            return BuildResult("create", request.DryRun, results);
        }

        // -----------------------------------------------------------------------------------------
        // update
        // -----------------------------------------------------------------------------------------

        private static ManageMaterialsResult Update(Document doc, ManageMaterialsRequest request)
        {
            List<MaterialInput> inputs = request.Updates;
            if (inputs == null || inputs.Count == 0)
            {
                throw new ArgumentException("Voor 'update' is een niet-lege lijst 'updates' nodig");
            }

            var lookups = new Lookups(doc);

            // naam -> id van alle bestaande materialen; wordt tijdens het plannen bijgewerkt zodat een
            // reeks hernoemingen in één aanroep elkaar niet laat botsen (en een dryRun hetzelfde zegt).
            var nameToId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (Material m in GetMaterials(doc))
            {
                if (!nameToId.ContainsKey(m.Name)) nameToId[m.Name] = m.Id.GetValue();
            }

            var results = new List<MaterialItemResult>();
            var planned = new List<PlannedItem>();

            for (int i = 0; i < inputs.Count; i++)
            {
                MaterialInput input = inputs[i];
                var item = new MaterialItemResult { Index = i, Status = "failed" };
                results.Add(item);

                if (input == null)
                {
                    item.Message = "Leeg item";
                    continue;
                }

                item.Id = input.Id;
                if (!ParameterReadUtils.TryParseElementId(input.Id, out long idValue))
                {
                    item.Message = $"Ongeldig id '{input.Id}'";
                    continue;
                }

                var material = doc.GetElement(ElementIdExtensions.FromLong(idValue)) as Material;
                if (material == null)
                {
                    item.Message = $"Materiaal met id '{input.Id}' niet gevonden";
                    continue;
                }

                item.OldName = material.Name;
                item.NewName = material.Name;

                bool renaming = input.MainGroup != null || input.Characteristic != null ||
                                input.Application != null || input.Custom != null;

                var errors = new List<string>();
                ResolvedProperties props = ResolveProperties(doc, input, lookups, errors);

                string newName = null;
                if (renaming)
                {
                    // Niet opgegeven delen komen uit de huidige naam (voor zover die er drie heeft).
                    NaaktCatalogUtils.TrySplit(material.Name, out string curMain, out string curChar,
                        out string curApp, out string curCustom);

                    NaaktCheckResult check = NaaktCatalogUtils.BuildAndCheck(
                        input.MainGroup ?? curMain,
                        input.Characteristic ?? curChar,
                        input.Application ?? curApp,
                        input.Custom ?? curCustom);

                    if (!check.Valid)
                    {
                        FillNaaktFailure(item, check);
                        if (errors.Count > 0) item.Message += "; " + string.Join("; ", errors);
                        continue;
                    }

                    newName = check.Name;
                    item.NewName = newName;

                    if (nameToId.TryGetValue(newName, out long otherId) && otherId != idValue)
                    {
                        item.Message = $"Er bestaat al een materiaal met de naam '{newName}' (id {otherId})";
                        continue;
                    }
                }

                if (errors.Count > 0)
                {
                    item.Message = string.Join("; ", errors);
                    continue;
                }

                bool nameChanges = newName != null && !string.Equals(newName, material.Name, StringComparison.Ordinal);
                var changes = new List<string>();
                if (nameChanges) changes.Add("name");
                changes.AddRange(props.Describe());

                if (!renaming && !props.HasAny)
                {
                    item.Message = "Geen wijzigingen opgegeven";
                    continue;
                }

                item.Success = true;
                item.Changes = changes;
                item.Status = changes.Count > 0 ? "updated" : "unchanged";
                if (changes.Count == 0) item.Message = "Naam is al correct";

                if (nameChanges)
                {
                    nameToId.Remove(material.Name);
                    nameToId[newName] = idValue;
                }

                if (changes.Count > 0)
                {
                    planned.Add(new PlannedItem
                    {
                        Item = item,
                        Material = material,
                        NewName = nameChanges ? newName : null,
                        Props = props
                    });
                }
            }

            if (!request.DryRun && planned.Count > 0)
            {
                ApplyInTransaction(doc, "Materialen aanpassen (NAAKT)", planned, plan =>
                {
                    if (plan.NewName != null) plan.Material.Name = plan.NewName;
                    plan.Props.ApplyTo(plan.Material, copyTemplate: true, out List<string> warnings);
                    AddWarnings(plan.Item, warnings);
                });
            }

            return BuildResult("update", request.DryRun, results);
        }

        // -----------------------------------------------------------------------------------------
        // Gedeelde hulpmethodes voor create en update
        // -----------------------------------------------------------------------------------------

        // Voert de geplande items door: één Transaction, per item een SubTransaction die bij een fout
        // wordt teruggedraaid (het item krijgt dan status failed), waarna de rest doorgaat.
        private static void ApplyInTransaction(Document doc, string transactionName, List<PlannedItem> planned,
            Action<PlannedItem> apply)
        {
            using (var transaction = new Transaction(doc, transactionName))
            {
                transaction.Start();

                foreach (PlannedItem plan in planned)
                {
                    using (var sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            apply(plan);
                            if (sub.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit did not commit this material change.");
                        }
                        catch (Exception ex)
                        {
                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            plan.Item.Success = false;
                            plan.Item.Status = "failed";
                            plan.Item.Id = null;
                            plan.Item.Message = ex.Message;
                        }
                    }
                }

                if (transaction.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit did not commit the material changes.");
            }
        }

        private static ManageMaterialsResult BuildResult(string action, bool dryRun, List<MaterialItemResult> results)
        {
            return new ManageMaterialsResult
            {
                Action = action,
                DryRun = dryRun ? true : (bool?)null,
                Message = dryRun ? "Dry run: er is niets aangepast in het model" : null,
                Succeeded = results.Count(r => r.Success),
                Failed = results.Count(r => !r.Success),
                Results = results
            };
        }

        private static void FillNaaktFailure(MaterialItemResult item, NaaktCheckResult check)
        {
            item.Message = "Geen geldige NAAKT-naam";
            item.Issues = check.Issues;
            item.Suggestion = check.Suggestion;
        }

        private static void AddWarnings(MaterialItemResult item, List<string> warnings)
        {
            if (warnings == null || warnings.Count == 0) return;
            string text = "Let op: " + string.Join("; ", warnings);
            item.Message = string.IsNullOrEmpty(item.Message) ? text : item.Message + "; " + text;
        }

        private static List<Material> GetMaterials(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Opzoeklijsten die per aanroep één keer worden opgebouwd.
        private class Lookups
        {
            private readonly Document _doc;

            public Dictionary<string, ElementId> Patterns { get; }
            public Dictionary<string, Material> MaterialsByName { get; }

            public Lookups(Document doc)
            {
                _doc = doc;

                Patterns = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
                foreach (FillPatternElement pattern in new FilteredElementCollector(doc)
                             .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
                {
                    if (!Patterns.ContainsKey(pattern.Name)) Patterns[pattern.Name] = pattern.Id;
                }

                MaterialsByName = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
                foreach (Material material in GetMaterials(doc))
                {
                    if (!MaterialsByName.ContainsKey(material.Name)) MaterialsByName[material.Name] = material;
                }
            }

            // Sjabloon op ElementId of op naam.
            public Material FindTemplate(string template)
            {
                if (ParameterReadUtils.TryParseElementId(template, out long id))
                {
                    var byId = _doc.GetElement(ElementIdExtensions.FromLong(id)) as Material;
                    if (byId != null) return byId;
                }

                MaterialsByName.TryGetValue(template.Trim(), out Material byName);
                return byName;
            }
        }

        private class ResolvedPattern
        {
            public ElementId PatternId;
            public Color Color;
        }

        // De gecontroleerde en opgezochte eigenschappen van een item (kleur, vulpatronen, sjabloon).
        private class ResolvedProperties
        {
            public Color Color;
            public Material Template;
            public ResolvedPattern SurfaceForeground;
            public ResolvedPattern SurfaceBackground;
            public ResolvedPattern CutForeground;
            public ResolvedPattern CutBackground;

            public bool HasAny =>
                Color != null || Template != null || SurfaceForeground != null || SurfaceBackground != null ||
                CutForeground != null || CutBackground != null;

            public List<string> Describe()
            {
                var changes = new List<string>();
                if (Template != null) changes.Add("template");
                if (Color != null) changes.Add("color");
                if (SurfaceForeground != null) changes.Add("surfaceForeground");
                if (SurfaceBackground != null) changes.Add("surfaceBackground");
                if (CutForeground != null) changes.Add("cutForeground");
                if (CutBackground != null) changes.Add("cutBackground");
                return changes;
            }

            // Eerst het sjabloon, daarna de expliciet opgegeven waardes (die winnen). copyTemplate=false
            // als het materiaal al een duplicaat van het sjabloon is (create).
            public void ApplyTo(Material material, bool copyTemplate, out List<string> warnings)
            {
                warnings = new List<string>();

                if (copyTemplate && Template != null) CopyFromTemplate(material, Template, warnings);
                if (Color != null) material.Color = Color;

                Apply(SurfaceForeground, id => material.SurfaceForegroundPatternId = id, c => material.SurfaceForegroundPatternColor = c);
                Apply(SurfaceBackground, id => material.SurfaceBackgroundPatternId = id, c => material.SurfaceBackgroundPatternColor = c);
                Apply(CutForeground, id => material.CutForegroundPatternId = id, c => material.CutForegroundPatternColor = c);
                Apply(CutBackground, id => material.CutBackgroundPatternId = id, c => material.CutBackgroundPatternColor = c);
            }

            private static void Apply(ResolvedPattern pattern, Action<ElementId> setId, Action<Color> setColor)
            {
                if (pattern == null) return;
                if (pattern.PatternId != null) setId(pattern.PatternId);
                if (pattern.Color != null) setColor(pattern.Color);
            }
        }

        private static ResolvedProperties ResolveProperties(Document doc, MaterialInput input, Lookups lookups,
            List<string> errors)
        {
            var props = new ResolvedProperties();

            if (!string.IsNullOrWhiteSpace(input.Color))
            {
                if (TryParseColor(input.Color, out Color color, out string error)) props.Color = color;
                else errors.Add(error);
            }

            props.SurfaceForeground = ResolvePattern("surfaceForeground", input.SurfaceForeground, lookups, errors);
            props.SurfaceBackground = ResolvePattern("surfaceBackground", input.SurfaceBackground, lookups, errors);
            props.CutForeground = ResolvePattern("cutForeground", input.CutForeground, lookups, errors);
            props.CutBackground = ResolvePattern("cutBackground", input.CutBackground, lookups, errors);

            if (!string.IsNullOrWhiteSpace(input.Template))
            {
                props.Template = lookups.FindTemplate(input.Template);
                if (props.Template == null) errors.Add($"Sjabloonmateriaal '{input.Template}' niet gevonden");
            }

            return props;
        }

        private static ResolvedPattern ResolvePattern(string label, MaterialPatternInput input, Lookups lookups,
            List<string> errors)
        {
            if (input == null) return null;
            if (string.IsNullOrWhiteSpace(input.Pattern) && string.IsNullOrWhiteSpace(input.Color)) return null;

            var resolved = new ResolvedPattern();

            if (!string.IsNullOrWhiteSpace(input.Pattern))
            {
                if (lookups.Patterns.TryGetValue(input.Pattern.Trim(), out ElementId patternId))
                {
                    resolved.PatternId = patternId;
                }
                else
                {
                    errors.Add($"{label}: vulpatroon '{input.Pattern}' niet gevonden in dit project " +
                               "(zie action 'options' met includeFillPatterns)");
                }
            }

            if (!string.IsNullOrWhiteSpace(input.Color))
            {
                if (TryParseColor(input.Color, out Color color, out string error)) resolved.Color = color;
                else errors.Add($"{label}: {error}");
            }

            return resolved;
        }

        private static bool TryParseColor(string text, out Color color, out string error)
        {
            color = null;
            error = $"Ongeldige kleur '{text}' (verwacht R,G,B met waardes 0-255)";

            string[] parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToArray();
            if (parts.Length != 3) return false;

            var values = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]) ||
                    values[i] < 0 || values[i] > 255)
                {
                    return false;
                }
            }

            color = new Color((byte)values[0], (byte)values[1], (byte)values[2]);
            error = null;
            return true;
        }

        // Neemt bij update van een sjabloonmateriaal over: klasse, de drie assets (gedeeld) en
        // 'render-weergave voor schaduw', plus kleur en arcering. Bij create is dit niet nodig: daar wordt
        // het sjabloon in zijn geheel gedupliceerd. Eén eigenschap die niet over te nemen is, laat de rest
        // ongemoeid.
        private static void CopyFromTemplate(Material target, Material template, List<string> warnings)
        {
            TryCopy("render-weergave", () => target.UseRenderAppearanceForShading = template.UseRenderAppearanceForShading, warnings);

            if (!string.IsNullOrEmpty(template.MaterialClass))
            {
                TryCopy("materiaalklasse", () => target.MaterialClass = template.MaterialClass, warnings);
            }

            ShareAssets(target, template, deleteReplaced: false, warnings);

            if (template.Color != null && template.Color.IsValid)
            {
                TryCopy("kleur", () => target.Color = template.Color, warnings);
            }

            CopyPattern("surfaceForeground", template.SurfaceForegroundPatternId, template.SurfaceForegroundPatternColor,
                id => target.SurfaceForegroundPatternId = id, c => target.SurfaceForegroundPatternColor = c, warnings);
            CopyPattern("surfaceBackground", template.SurfaceBackgroundPatternId, template.SurfaceBackgroundPatternColor,
                id => target.SurfaceBackgroundPatternId = id, c => target.SurfaceBackgroundPatternColor = c, warnings);
            CopyPattern("cutForeground", template.CutForegroundPatternId, template.CutForegroundPatternColor,
                id => target.CutForegroundPatternId = id, c => target.CutForegroundPatternColor = c, warnings);
            CopyPattern("cutBackground", template.CutBackgroundPatternId, template.CutBackgroundPatternColor,
                id => target.CutBackgroundPatternId = id, c => target.CutBackgroundPatternColor = c, warnings);
        }

        private static void CopyPattern(string label, ElementId patternId, Color color, Action<ElementId> setId,
            Action<Color> setColor, List<string> warnings)
        {
            if (IsValid(patternId)) TryCopy(label, () => setId(patternId), warnings);
            if (color != null && color.IsValid) TryCopy(label + " kleur", () => setColor(color), warnings);
        }

        // Laat target dezelfde assets (appearance, physical/structural, thermal) gebruiken als source: gedeeld,
        // nooit gekopieerd. Physical en thermal gaan via SetMaterialAspectByPropertySet, de officiële route
        // voor property sets. deleteReplaced=true ruimt de assets op die target daardoor niet meer gebruikt;
        // alleen bedoeld voor een vers gedupliceerd materiaal (create), waar dat eventuele kopieën van Revit
        // zelf zijn. Bij update blijven vervangen assets staan, die kunnen elders nog bedoeld zijn.
        private static void ShareAssets(Material target, Material source, bool deleteReplaced, List<string> warnings)
        {
            Document doc = target.Document;
            var replaced = new List<ElementId>();

            if (IsValid(source.AppearanceAssetId) && target.AppearanceAssetId != source.AppearanceAssetId)
            {
                ElementId old = target.AppearanceAssetId;
                TryCopy("appearance asset", () => target.AppearanceAssetId = source.AppearanceAssetId, warnings);
                if (target.AppearanceAssetId == source.AppearanceAssetId && IsValid(old)) replaced.Add(old);
            }

            if (IsValid(source.StructuralAssetId) && target.StructuralAssetId != source.StructuralAssetId)
            {
                ElementId old = target.StructuralAssetId;
                TryCopy("physical asset", () =>
                    target.SetMaterialAspectByPropertySet(MaterialAspect.Structural, source.StructuralAssetId), warnings);
                if (target.StructuralAssetId == source.StructuralAssetId && IsValid(old)) replaced.Add(old);
            }

            if (IsValid(source.ThermalAssetId) && target.ThermalAssetId != source.ThermalAssetId)
            {
                ElementId old = target.ThermalAssetId;
                TryCopy("thermal asset", () =>
                    target.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, source.ThermalAssetId), warnings);
                if (target.ThermalAssetId == source.ThermalAssetId && IsValid(old)) replaced.Add(old);
            }

            if (!deleteReplaced || replaced.Count == 0) return;

            // Alleen verwijderen wat geen enkel materiaal meer gebruikt.
            var inUse = new HashSet<ElementId>(GetMaterials(doc).SelectMany(m =>
                new[] { m.AppearanceAssetId, m.StructuralAssetId, m.ThermalAssetId }));
            foreach (ElementId id in replaced.Where(id => !inUse.Contains(id)))
            {
                try
                {
                    doc.Delete(id);
                }
                catch (Exception ex)
                {
                    warnings.Add($"overbodige assetkopie {id.GetValue()} niet verwijderd ({ex.Message})");
                }
            }
        }

        private static bool IsValid(ElementId id)
        {
            return id != null && id != ElementId.InvalidElementId;
        }

        private static void TryCopy(string label, Action copy, List<string> warnings)
        {
            try
            {
                copy();
            }
            catch (Exception ex)
            {
                warnings.Add($"{label} niet overgenomen van het sjabloon ({ex.Message})");
            }
        }
    }
}
