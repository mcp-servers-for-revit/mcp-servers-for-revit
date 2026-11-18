using System.IO;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using RevitMCPCommandSet.Models;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// NAAKT-regels voor materiaalnamen, gebaseerd op de gepubliceerde NAA.K.T.-catalogus.
    ///
    /// Een NAAKT-naam heeft de vorm: hoofdgroep_kenmerk_toepassing[_eigen]
    /// - de eerste drie delen moeten exact (kleine letters) in de lijst van de hoofdgroep staan;
    /// - het laatste deel (eigen) is vrij en mag zelf ook underscores bevatten.
    /// </summary>
    public static class NaaktCatalogUtils
    {
        private const char Separator = '_';

        private sealed class Hoofdgroep
        {
            public string Naam { get; set; }
            public List<string> Kenmerk { get; set; }
            public List<string> Toepassing { get; set; }
        }

        // Read the group's two enumerations from the published, unmodified IDS catalog.
        private static readonly Lazy<List<Hoofdgroep>> _groups = new Lazy<List<Hoofdgroep>>(LoadGroups);

        private static List<Hoofdgroep> LoadGroups()
        {
            using var stream = typeof(NaaktCatalogUtils).Assembly.GetManifestResourceStream("NAAKT.ids");
            var catalog = XDocument.Load(stream);
            XNamespace ids = "http://standards.buildingsmart.org/IDS";
            XNamespace xs = "http://www.w3.org/2001/XMLSchema";
            var groups = new List<Hoofdgroep>();
            foreach (var rule in catalog.Descendants(ids + "requirements").Descendants(xs + "pattern"))
            {
                var pattern = (string)rule.Attribute("value");
                if (pattern.StartsWith("(")) continue; // The first rule enumerates the main groups.
                var fields = Regex.Match(pattern, @"^([^_]+)_\(([^)]+)\)_\(([^)]+)\)\.\*$");
                if (!fields.Success) throw new InvalidDataException("Invalid NAA.K.T. group rule: " + pattern);
                groups.Add(new Hoofdgroep {
                    Naam = fields.Groups[1].Value,
                    Kenmerk = fields.Groups[2].Value.Split('|').ToList(),
                    Toepassing = fields.Groups[3].Value.Split('|').ToList()
                });
            }
            return groups;
        }

        private static readonly string[] NeutralTerms = { "ntb", "generiek" };

        private static List<Hoofdgroep> Groups => _groups.Value;

        /// <summary>Namen van alle hoofdgroepen, in de volgorde van de lijst.</summary>
        public static List<string> GetMainGroupNames()
        {
            return Groups.Select(g => g.Naam).ToList();
        }

        /// <summary>
        /// Zonder hoofdgroep: de lijst met hoofdgroepen. Met hoofdgroep: de kenmerken en toepassingen
        /// die daarbij horen. Een onbekende hoofdgroep geeft een ArgumentException.
        /// </summary>
        public static NaaktOptionsResult GetOptions(string mainGroup)
        {
            var result = new NaaktOptionsResult();

            if (string.IsNullOrWhiteSpace(mainGroup))
            {
                result.MainGroups = GetMainGroupNames();
                return result;
            }

            Hoofdgroep group = FindGroup(mainGroup.Trim(), ignoreCase: true);
            if (group == null)
            {
                string closest = Closest(GetMainGroupNames(), mainGroup.Trim().ToLowerInvariant());
                throw new ArgumentException(
                    $"Hoofdgroep '{mainGroup}' bestaat niet" +
                    (closest != null ? $" (bedoelde je '{closest}'?)" : "") +
                    $". Beschikbaar: {string.Join(", ", GetMainGroupNames())}");
            }

            result.MainGroup = group.Naam;
            result.Characteristics = group.Kenmerk.ToList();
            result.Applications = group.Toepassing.ToList();
            return result;
        }

        /// <summary>True als het deel vóór de eerste underscore (hoofdletterongevoelig) een hoofdgroep is.</summary>
        public static bool StartsWithMainGroup(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string first = name.Trim().Split(Separator)[0];
            return FindGroup(first, ignoreCase: true) != null;
        }

        /// <summary>
        /// Splitst een bestaande naam in de drie vaste delen + de vrije staart. Geeft false als de naam
        /// geen drie niet-lege delen heeft; de delen die er wel zijn worden dan toch ingevuld.
        /// </summary>
        public static bool TrySplit(string name, out string mainGroup, out string characteristic,
            out string application, out string custom)
        {
            mainGroup = characteristic = application = custom = null;
            if (string.IsNullOrWhiteSpace(name)) return false;

            string[] segments = name.Trim().Split(Separator);
            if (segments.Length > 0) mainGroup = segments[0];
            if (segments.Length > 1) characteristic = segments[1];
            if (segments.Length > 2) application = segments[2];
            if (segments.Length > 3) custom = string.Join(Separator.ToString(), segments.Skip(3));

            return segments.Length >= 3
                   && !string.IsNullOrEmpty(mainGroup)
                   && !string.IsNullOrEmpty(characteristic)
                   && !string.IsNullOrEmpty(application);
        }

        /// <summary>
        /// Controleert een bestaande naam tegen de NAAKT-regels. Bij een ongeldige naam komen er
        /// meldingen en, als het eenduidig te herstellen is, een voorstel.
        /// </summary>
        public static NaaktCheckResult Check(string name)
        {
            var result = new NaaktCheckResult { Name = name };

            if (string.IsNullOrWhiteSpace(name))
            {
                result.Issues.Add("Naam is leeg");
                return result;
            }

            if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
            {
                result.Issues.Add("Naam begint of eindigt met een spatie");
            }

            string[] segments = name.Trim().Split(Separator);
            result.MainGroup = segments[0];
            if (segments.Length > 1) result.Characteristic = segments[1];
            if (segments.Length > 2) result.Application = segments[2];
            if (segments.Length > 3) result.Custom = string.Join(Separator.ToString(), segments.Skip(3));

            if (segments.Length < 3)
            {
                result.Issues.Add("Naam heeft minder dan drie delen (hoofdgroep_kenmerk_toepassing)");
            }

            // Hoofdgroep: eerst exact, dan hoofdletterongevoelig om een notatiefout te kunnen melden.
            Hoofdgroep group = FindGroup(segments[0], ignoreCase: false);
            if (group == null)
            {
                group = FindGroup(segments[0], ignoreCase: true);
                result.Issues.Add(group != null
                    ? $"Hoofdgroep '{segments[0]}' moet in kleine letters: '{group.Naam}'"
                    : $"Hoofdgroep '{segments[0]}' bestaat niet");
            }

            if (group != null)
            {
                if (segments.Length > 1)
                {
                    CheckTerm(result, "Kenmerk", segments[1], group.Kenmerk, group.Naam);
                }

                if (segments.Length > 2)
                {
                    CheckTerm(result, "Toepassing", segments[2], group.Toepassing, group.Naam);
                }
            }

            // Lege delen, bijvoorbeeld 'a__b' of een afsluitende underscore, zijn nooit bedoeld.
            if (segments.Skip(3).Any(string.IsNullOrEmpty))
            {
                result.Issues.Add("De naam bevat een leeg deel (dubbele of afsluitende underscore)");
            }

            result.Valid = result.Issues.Count == 0;
            if (!result.Valid)
            {
                result.Suggestion = Suggest(name);
                if (string.Equals(result.Suggestion, name, StringComparison.Ordinal))
                {
                    result.Suggestion = null;
                }
            }

            return result;
        }

        /// <summary>
        /// Bouwt een NAAKT-naam uit de losse delen en controleert die. Ontbrekende delen en onbekende
        /// waardes komen terug als meldingen. De drie vaste delen worden naar kleine letters omgezet.
        /// </summary>
        public static NaaktCheckResult BuildAndCheck(string mainGroup, string characteristic, string application,
            string custom)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(mainGroup)) missing.Add("mainGroup");
            if (string.IsNullOrWhiteSpace(characteristic)) missing.Add("characteristic");
            if (string.IsNullOrWhiteSpace(application)) missing.Add("application");

            if (missing.Count > 0)
            {
                var incomplete = new NaaktCheckResult
                {
                    MainGroup = mainGroup,
                    Characteristic = characteristic,
                    Application = application,
                    Custom = custom
                };
                incomplete.Issues.Add($"Ontbrekend: {string.Join(", ", missing)}. " +
                                      "Een NAAKT-naam bestaat altijd uit hoofdgroep, kenmerk en toepassing");
                return incomplete;
            }

            var parts = new List<string>
            {
                mainGroup.Trim().ToLowerInvariant(),
                characteristic.Trim().ToLowerInvariant(),
                application.Trim().ToLowerInvariant()
            };

            if (!string.IsNullOrWhiteSpace(custom))
            {
                parts.Add(custom.Trim());
            }

            return Check(string.Join(Separator.ToString(), parts));
        }

        // Controleert één van de drie vaste delen tegen de lijst van de hoofdgroep.
        private static void CheckTerm(NaaktCheckResult result, string label, string value, List<string> allowed,
            string groupName)
        {
            if (allowed.Contains(value)) return;

            if (string.IsNullOrEmpty(value))
            {
                result.Issues.Add($"{label} is leeg");
                return;
            }

            string caseInsensitive = allowed.FirstOrDefault(a =>
                string.Equals(a, value, StringComparison.OrdinalIgnoreCase));

            result.Issues.Add(caseInsensitive != null
                ? $"{label} '{value}' moet in kleine letters: '{caseInsensitive}'"
                : $"{label} '{value}' bestaat niet bij hoofdgroep '{groupName}'");
        }

        // Probeert een ongeldige naam eenduidig te herstellen. Geeft null als dat niet lukt.
        private static string Suggest(string name)
        {
            string[] tokens = name.Trim()
                .Split(new[] { Separator, ' ', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .ToArray();

            if (tokens.Length < 3) return null;

            // Hoofdgroep
            Hoofdgroep group = FindGroup(tokens[0], ignoreCase: true);
            if (group == null)
            {
                string closestGroup = Closest(GetMainGroupNames(), tokens[0]);
                group = closestGroup != null ? FindGroup(closestGroup, ignoreCase: true) : null;
            }

            // Onbekende hoofdgroep: het kenmerk wijst vaak eenduidig de hoofdgroep aan (baksteen -> steenachtig).
            if (group == null && !NeutralTerms.Contains(tokens[1]))
            {
                List<Hoofdgroep> owners = Groups.Where(g => g.Kenmerk.Contains(tokens[1])).ToList();
                if (owners.Count == 1) group = owners[0];
            }

            if (group == null) return null;

            // Kenmerk: hoort het kenmerk bij een andere hoofdgroep, dan nemen we die over.
            string characteristic = tokens[1];
            if (!group.Kenmerk.Contains(characteristic))
            {
                List<Hoofdgroep> owners = NeutralTerms.Contains(characteristic)
                    ? new List<Hoofdgroep>()
                    : Groups.Where(g => g.Kenmerk.Contains(characteristic)).ToList();

                if (owners.Count == 1)
                {
                    group = owners[0];
                }
                else
                {
                    characteristic = Closest(group.Kenmerk, characteristic);
                    if (characteristic == null) return null;
                }
            }

            // Toepassing
            string application = tokens[2];
            if (!group.Toepassing.Contains(application))
            {
                application = Closest(group.Toepassing, application);
                if (application == null) return null;
            }

            var parts = new List<string> { group.Naam, characteristic, application };
            parts.AddRange(tokens.Skip(3));
            return string.Join(Separator.ToString(), parts);
        }

        private static Hoofdgroep FindGroup(string name, bool ignoreCase)
        {
            if (name == null) return null;
            StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return Groups.FirstOrDefault(g => string.Equals(g.Naam, name, comparison));
        }

        /// <summary>
        /// Zoekt de eenduidig dichtstbijzijnde waarde: gelijk op hoofdletters na, anders een waarde die de
        /// ingevoerde tekst bevat (of andersom), anders de kleinste bewerkingsafstand (maximaal 2).
        /// Geeft null als er niets of meerdere gelijkwaardige kandidaten zijn.
        /// </summary>
        private static string Closest(IEnumerable<string> candidates, string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            List<string> list = candidates.ToList();

            string exactIgnoreCase = list.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));
            if (exactIgnoreCase != null) return exactIgnoreCase;

            // Neutrale termen (ntb/generiek) nooit als 'bijna gelijk' voorstellen.
            List<string> searchable = list.Where(c => !NeutralTerms.Contains(c)).ToList();

            List<string> contained = searchable
                .Where(c => c.Length >= 3 && value.Length >= 3 &&
                            (c.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             value.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            if (contained.Count == 1) return contained[0];

            var scored = searchable
                .Select(c => new { Value = c, Distance = Levenshtein(c, value) })
                .Where(x => x.Distance <= 2)
                .OrderBy(x => x.Distance)
                .ToList();

            if (scored.Count == 0) return null;
            if (scored.Count > 1 && scored[0].Distance == scored[1].Distance) return null;
            return scored[0].Value;
        }

        private static int Levenshtein(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[a.Length, b.Length];
        }
    }
}
