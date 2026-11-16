# Vergelijking: PNG uit Revit halen voor AI-analyse

> Opgesteld: 14 november 2026. Vergeleken repos:
> - **Constructelligence**: [constructelligence-lab/revit-mcp](https://github.com/constructelligence-lab/revit-mcp), commit `bf9745a` (9 november 2026). Zie ook [CONSTRUCTELLIGENCE-VERGELIJKING.md](CONSTRUCTELLIGENCE-VERGELIJKING.md).
> - **Python**: [mcp-servers-for-revit/mcp-server-for-revit-python](https://github.com/mcp-servers-for-revit/mcp-server-for-revit-python), commit `6ca46b3` (14 oktober 2026), pyRevit-extensie.
>
> Doel: een basis om later een eigen tool te bouwen die een view als PNG uit Revit haalt en **als afbeelding** aan het taalmodel teruggeeft. Er is nog geen code aangepast. Deze fork heeft nog geen afbeeldingen-tool; er zijn wel `get_current_view_info` en `get_current_view_elements`.

## 1. Kern: allebei dezelfde Revit-API

Beide repos gebruiken `Document.ExportImage(ImageExportOptions)` met dezelfde instellingen:

```csharp
var options = new ImageExportOptions
{
    ExportRange = ExportRange.SetOfViews,     // nodig om een specifieke (niet-actieve) view te exporteren
    FilePath = Path.Combine(folder, "export"), // prefix; Revit verzint zelf de rest van de bestandsnaam
    HLRandWFViewsFileType = ImageFileType.PNG,
    ShadowViewsFileType = ImageFileType.PNG,
    ZoomType = ZoomFitType.FitToPage,
    PixelSize = width,                         // breedte in pixels
    ImageResolution = ImageResolution.DPI_150, // alleen Python en render_scene zetten dit
};
options.SetViewsAndSheets(new List<ElementId> { view.Id });
doc.ExportImage(options);
```

Het verschil zit in wat eromheen gebouwd is: welke view, hoe je het bestand terugvindt, opruimen, en vooral **of de afbeelding bij het taalmodel aankomt**.

## 2. De drie varianten

| | **Python** `get_revit_view` | **Constructelligence** `export_view_image` | **Constructelligence** `render_scene` / `render_gallery` |
|---|---|---|---|
| Bestanden | `revit_mcp/views.py` (route `/get_view/<naam>`), `tools/view_tools.py`, `main.py` (`revit_image`) | `addin/Handlers/ReadOps.cs` (`ExportViewImage`), `server/tools.mjs` | `addin/Handlers/RenderOps.cs`, `server/camera.mjs`, `server/server.mjs` (`imageBlock`/`withImages`) |
| Taal | IronPython via pyRevit Routes (HTTP) | C# add-in + Node-server | C# + Node (camerapresets in `camera.mjs`) |
| Welke view | Op naam, verplicht | Op naam, of de actieve view als je niets opgeeft | Een nieuwe, tijdelijke 3D-view (preset of `eye`/`target`) |
| Afbeelding naar het taalmodel | ✅ base64 via de bridge, als MCP `Image` | ❌ **alleen een pad** + `exists`; het model ziet niets | ✅ de server leest het bestand en stuurt een image-blok (max 6 MB) |
| Resolutie | Vast 1024 px | `width` 200–4000 (standaard 1600) | `width` 400–4000 (standaard 1920), png of jpg |
| Tijdelijke bestanden | Eigen map per verzoek (`tempfile.mkdtemp`), altijd opgeruimd in `finally` | Gedeelde map `%TEMP%\revit-mcp`, **nooit opgeruimd** | Idem, nooit opgeruimd |
| Bestand terugvinden | Nieuwste PNG in een eigen lege map (robuust) | Mapinhoud vóór en na vergelijken in een gedeelde map | Prefix + tijdstempel |
| Zoeken op naam | Genormaliseerd, hoofdlettergevoelig | Hoofdletterongevoelig, templates overgeslagen | n.v.t. |
| Fout bij onbekende view | 404 met de eerste 20 beschikbare views | `not_found` + een hint om `list_views` te gebruiken | n.v.t. |
| Templates en interne views weigeren | Ja (`IsTemplate`, `ViewType.Internal`) | Alleen `!IsTemplate` | n.v.t. |
| Model wijzigt? | Nee | Nee | Nee: alles binnen een `TransactionGroup` met `RollBack()`, ook de undo-stack blijft schoon |
| Omvang | ~180 regels (route + tool) | ~45 regels | ~330 regels C# + cameralogica in Node |
| Tests | Unit-test op het opruimen van de temp-map (`tests/unit/test_view_export_privacy.py`) | – | Server- en jobtests, geen live Revit-test |
| Licentie | MIT | MIT | MIT |

## 3. Voor- en nadelen

### Python
- ➕ Doet precies wat nodig is: het taalmodel krijgt de afbeelding.
- ➕ 1024 px is een goede standaard. Claude verkleint grotere afbeeldingen toch (lange zijde rond 1568 px), dus groter kost alleen meer tokens en bytes.
- ➕ Netjes opgeruimd. Een eigen map per verzoek voorkomt dat je per ongeluk een oude PNG oppakt.
- ➕ Behulpzame foutmelding: de lijst met beschikbare views helpt het model om het zelf te corrigeren.
- ➖ IronPython/pyRevit: je neemt het **ontwerp** over, niet de code. De relevante logica is ±40 regels.
- ➖ Geen `width`-parameter, geen terugval naar de actieve view, en zoeken is hoofdlettergevoelig.

### Constructelligence `export_view_image`
- ➕ C# met dezelfde API als jouw `commandset`; R24 (net48) kan ook.
- ➕ Handige parameters: actieve view als standaard, `width`, hoofdletterongevoelig zoeken.
- ➖ In deze vorm onbruikbaar voor AI-analyse: er komt alleen een pad terug.
- ➖ De temp-map groeit onbeperkt.
- ➖ Een vóór/na-vergelijking in een gedeelde map gaat mis als twee exports tegelijk in die map schrijven.

### Constructelligence `render_scene`
- ➕ Een echte extra mogelijkheid: het model kan "rondkijken" vanuit zelfgekozen standpunten. Het kan itereren door categorieën te verbergen, een section box te zetten of de visuele stijl te wisselen, zonder het model te vervuilen.
- ➕ Een tijdelijke view met `TransactionGroup` + rollback is een sterk patroon. Het is ook bruikbaar voor een "exporteer met tijdelijke overrides"-variant van de basistool.
- ➖ Leunt op Node-cameralogica (`scene_info` + presets). Dat moet naar TypeScript worden geport en is aanzienlijk meer werk.
- ➖ Het Blender-deel (`render_photoreal`/`render_turntable`) is volgens hun eigen `docs/rendering.md` "not verified in a live session yet". Laat dat liggen.

## 4. Aansluiting op deze fork

| Punt | Gevolg |
|---|---|
| Patroon | Een nieuw `Command` + `EventHandler`-paar in `commandset/Commands` en `commandset/Services`, plus een tool in `server/src/tools/` (wordt automatisch gevonden door `register.ts`). Registreren in `commandset/command.json` en `commandRegistry.json`. |
| Pad of bytes terugsturen | **Pad (aanbevolen):** de plugin schrijft de PNG en geeft het pad terug; de TS-server leest het bestand, maakt er een image-blok van en verwijdert het bestand. Er gaan maar een paar honderd bytes over de TCP-socket. Dit kan omdat Revit en de MCP-server altijd op dezelfde machine draaien (alleen de builds komen van het netwerk). **Bytes (base64 via de bridge):** onafhankelijk van een gedeeld bestandssysteem, maar gaat door het transport zonder framing (zie [§8 van de constructelligence-vergelijking](CONSTRUCTELLIGENCE-VERGELIJKING.md#8-aanvulling-robuustheid-en-timeouts-bij-veel-data-drie-repos)). Antwoorden worden wel goed samengevoegd, alleen trager naarmate ze groter worden. |
| Transactie | `ExportImage` mag niet binnen een open `Transaction` draaien. Bij een `render_scene`-achtige variant: view aanmaken in een `Transaction`, die committen, exporteren binnen de `TransactionGroup`, en daarna de groep terugdraaien (zoals `RenderOps.cs` doet). |
| Timeout | Een zware view kan enkele seconden duren. Neem 60–120 s bij `RaiseAndWaitForCompletion`, niet de 10–15 s die veel bestaande commands gebruiken. |
| Conventies | Reset vóór de raise, geen `TaskDialog` in de handler, Nederlandse foutmeldingen en commentaar (zie `CLAUDE.md`). |
| ElementId | `view.Id.Value` werkt in R24 en R26 (beide `REVIT2024_OR_GREATER`). |

## 5. Voorstel voor een eigen tool

### Fase 1: `get_view_image` (basis)

Combinatie van beide:

| Overnemen van | Wat |
|---|---|
| Constructelligence | C#-opzet; `viewName` optioneel met de actieve view als standaard; `width`; hoofdletterongevoelig zoeken |
| Python | Een eigen temp-map per verzoek, opruimen in `finally`; templates en `ViewType.Internal` weigeren; een lijst met beschikbare views in de foutmelding; de afbeelding echt teruggeven als MCP-image |

Voorstel voor de parameters:

| Parameter | Type | Standaard | Opmerking |
|---|---|---|---|
| `viewName` | string, optioneel | actieve view | Hoofdletterongevoelig; eventueel ook `viewId` accepteren |
| `width` | int | 1024 | Bereik 256–2000. Groter heeft voor het taalmodel geen zin |
| `format` | `png` \| `jpg` | `png` | JPG is kleiner bij gerenderde 3D-views; PNG is beter voor lijnwerk |

Het antwoord van de plugin, als je voor de pad-variant kiest:

```json
{ "path": "C:\\...\\Temp\\RevitMCP-<guid>\\export - Floor Plan - Peil 0.png",
  "viewName": "Peil 0", "viewType": "FloorPlan", "viewId": 12345, "width": 1024 }
```

De TS-server maakt er vervolgens dit van:

```ts
return { content: [
  { type: "text", text: JSON.stringify({ viewName, viewType, viewId, width }) },
  { type: "image", mimeType: "image/png", data: fs.readFileSync(path).toString("base64") },
] };
// daarna: fs.rmSync(dirname(path), { recursive: true, force: true })
```

Met een groottelimiet, zoals constructelligence die heeft (6 MB): is het bestand te groot, geef dan alleen tekst terug met de melding dat een kleinere `width` nodig is.

Aandachtspunten:
- **Bestandsnaam:** Revit plakt het viewtype en de viewnaam achter de prefix. Zoek daarom in de eigen, lege map naar `*.png`/`*.jpg` en raad de naam niet (`ImageExportOptions.GetFileName` bestaat ook).
- **Views die niet geëxporteerd kunnen worden:** templates, interne views en mogelijk schedules. Vang dat af met een duidelijke Nederlandse foutmelding.
- **Sheets** kunnen ook via `SetViewsAndSheets`. Eventueel later toevoegen.

### Fase 2 (optioneel): `render_view_3d`

Een lichte versie van `render_scene`: een tijdelijke `View3D` met `eye`/`target` (of de bestaande 3D-view als basis), plus `style`, `hideCategories` en `sectionBox`. Alles binnen een `TransactionGroup` met rollback. Presets, zon en mood weglaten; het taalmodel kan `eye`/`target` zelf bepalen uit een bounding box, en die kun je meegeven met een kleine `scene_info`-achtige tool. Pas de moeite waard als het taalmodel echt 3D-situaties moet beoordelen (botsingen, aansluitingen, opbouw).

Overnemen uit `RenderOps.cs`: `ApplyStyle`, `HideCategories`, `ApplySectionBox`, de omrekening naar interne eenheden (`Vector` met `UnitUtils.ConvertToInternalUnits`) en de `finally { group.RollBack(); }`-opbouw.

## 6. Naslag

| Bestand | Inhoud |
|---|---|
| Python: `revit_mcp/views.py` | Route `/get_view/<view_name>`: zoeken, controleren, exporteren, base64, opruimen |
| Python: `main.py` (`revit_image`) | HTTP-call, base64 terug naar `Image(data, format="png")`, 60 s timeout |
| Python: `tests/unit/test_view_export_privacy.py` | Test dat de temp-map vers is en altijd wordt opgeruimd |
| Constructelligence: `addin/Handlers/ReadOps.cs` (`ExportViewImage`) | Basisexport, actieve view als standaard, `width` |
| Constructelligence: `addin/Handlers/RenderOps.cs` | `SceneInfo`, `RenderScene`, stijl, zon, achtergrond, verbergen, section box |
| Constructelligence: `server/server.mjs` (`imageBlock`, `withImages`) | Bestand lezen, groottelimiet, image-blok achter de tekst |
| Constructelligence: `server/camera.mjs` | Presets en camerapositie uit de bounding box |
| Constructelligence: `docs/rendering.md` | Toelichting op de render-varianten en hun beperkingen |

Opnieuw ophalen:

```bash
git clone https://github.com/constructelligence-lab/revit-mcp.git
git clone https://github.com/mcp-servers-for-revit/mcp-server-for-revit-python.git
```
