# Vergelijking met LuDattilo/revit-mcp-server

> Opgesteld: 5 november 2026. Vergeleken met de online repo [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server), release **v2.2.1** (commit `2c33b84`). Jouw kant: `main` op commit `233bd5b`.
> Doel van dit document: later een keuze maken hoe verder te gaan (zie [§5 Keuzeopties](#5-keuzeopties)). Er is nog geen code aangepast.

## 1. Uitgangssituatie

- **Gemeenschappelijke basis:** beide repos zijn afgesplitst van upstream `mcp-servers-for-revit` op v1.0.0 (commit `a61a0d8`, 26 februari 2026).
- **Online, sinds de splitsing:** 120 commits (maart en april 2026), van v1.1.0 tot v2.2.1.
- **Jouw fork, sinds de splitsing:** 22 commits. Daarvan zijn er 6 van upstream (PR #9, #10, #18, #19, #20) en 16 eigen.
- De upstream-PR's die jij al hebt (`transactionMode` voor `send_code_to_revit` en de z.any-schemafix voor VS Code) heeft LuDattilo zelf ook doorgevoerd. Die hoef je dus niet over te nemen.

### Wat jij zelf hebt toegevoegd (en wat in elke optie behouden moet blijven)

| Onderdeel | Waar |
|---|---|
| Centrale netwerkdeployment (`NetworkRootPath`, Commands en Logs op `O:\`) | `plugin/RevitMCPPlugin.dll.config`, `PathManager` |
| Sync van de MCP-server vanaf de netwerkschijf | `plugin/Core/McpServerSyncService.cs` |
| Laden van assemblies via `Assembly.Load(byte[])` plus een `AssemblyResolve`-handler (nodig voor laden vanaf een netwerkpad op net48) | `plugin/Core/CommandManager.cs` |
| Logbestanden per gebruiker | `plugin/Utils/Logger.cs` |
| Integratie in het ModuleFabrique-ribbontab, met een toggle-icoon | `plugin/Core/Application.cs` |
| Alleen R24/R26-configuraties, met Debug/Release-deploytargets | `.csproj`'s, `.sln` |
| `npm run build:debug` / `build:release` met kopie naar lokaal of netwerk | `server/package.json` |
| Commentaar en meldingen in het Nederlands | hele codebase |
| Eigen tools: `get_parameters`, `set_parameters`, `get_available_parameters`, `manage_materials` | commandset + server |
| `disabledTools` in `register.ts`, geen standaardlimiet bij element-queries, `includeDetails` voor `ai_element_filter` | server + commandset |

## 2. Tools die online wel zijn en hier niet

Online heeft 138 tools, jij hebt er 31 (plus de lege stubs `search_modules`/`use_module`, die online zijn verwijderd). Er zijn **111 tools die jij niet hebt**:

| Categorie | Tools |
|---|---|
| **Parameters** | `get_element_parameters`, `set_element_parameters`, `bulk_modify_parameter_values`, `clear_parameter_values`, `add_prefix_suffix`, `transfer_parameters`, `filter_by_parameter_value`, `sync_csv_parameters`, `add_shared_parameter`, `get_shared_parameters`, `export_shared_parameter_file`, `manage_project_parameters`, `match_element_properties` |
| **Materialen / opbouw** | `get_materials`, `get_material_properties`, `create_material`, `set_material_properties`, `set_material_appearance`, `set_material_assets`, `get_compound_structure`, `set_compound_structure` |
| **Schedules** | `create_schedule`, `modify_schedule`, `duplicate_schedule`, `delete_schedule`, `get_schedule_data`, `export_schedule`, `list_schedulable_fields`, `create_material_takeoff_schedule`, `create_room_finish_schedule`, `create_door_schedule_by_room`, `create_window_schedule_by_room`, `create_sheet_list_schedule`, `create_view_list_schedule` |
| **Views** | `create_view`, `duplicate_view`, `rename_views`, `apply_view_template`, `create_view_template`, `manage_view_templates`, `create_view_filter`, `override_graphics`, `set_view_crop`, `navigate_view`, `batch_modify_view_range`, `section_box_from_selection`, `create_views_from_rooms`, `create_callout_from_rooms`, `create_elevations_from_rooms`, `manage_unplaced_views` |
| **Sheets** | `create_sheet`, `batch_create_sheets`, `create_placeholder_sheets`, `duplicate_sheet_with_views`, `duplicate_sheet_with_content`, `place_viewport`, `align_viewports`, `create_revision` |
| **Worksets / fasen** | `get_worksets`, `create_workset`, `rename_workset`, `delete_workset`, `set_active_workset`, `set_element_workset`, `get_elements_by_workset`, `get_phases`, `set_element_phase` |
| **QA/QC en audit** | `check_model_health`, `get_warnings`, `audit_families`, `check_family_health`, `purge_unused`, `find_untagged_elements`, `find_undimensioned_elements`, `wipe_empty_tags`, `lines_per_view_count`, `cad_link_cleanup`, `clash_detection`, `list_family_sizes` |
| **Families** | `load_family`, `rename_families`, `export_families`, `change_element_type` |
| **Elementen / modelleren** | `copy_elements`, `create_array`, `create_floor`, `create_filled_region`, `create_text_note`, `create_color_legend`, `renumber_elements`, `batch_rename`, `measure_between_elements`, `get_elements_in_spatial_volume`, `get_room_openings` |
| **Selectie** | `save_selection`, `load_selection`, `delete_selection` |
| **Export / import** | `export_to_excel`, `import_from_excel` (ClosedXML), `export_elements_data`, `batch_export`, `import_table` |
| **Project / links** | `get_project_info`, `manage_links`, `get_linked_elements` |
| **Workflows (meerdere stappen in één tool)** | `workflow_model_audit`, `workflow_clash_review`, `workflow_data_roundtrip`, `workflow_room_documentation`, `workflow_sheet_set` |
| **Specifiek Italiaans** | `calculate_rai` (verhouding raam- tot vloeroppervlak volgens Italiaanse norm, waarschijnlijk niet relevant) |

**Overlap met eigen tools:**
- Jouw `get_parameters`, `set_parameters` en `get_available_parameters` overlappen met hun `get_element_parameters` en `set_element_parameters`.
- Jouw `manage_materials` overlapt met hun losse materiaal-tools.

Daar gaat het om kiezen of samenvoegen, niet om beide houden.

## 3. Bugfixes die ook in jouw code zitten

Deze zijn tegen de huidige code van deze repo gecontroleerd. De problemen zitten er nog in.

| # | Probleem hier | Fix online | Ernst |
|---|---|---|---|
| **B1** ✅ | **Race bij `_resetEvent.Reset()`**: in alle handlers in `commandset/Services/` staat `Reset()` in `WaitForCompletion()`, en die wordt pas ná `Raise()` aangeroepen. Als Revit de handler al heeft afgerond vóór die `Reset()`, gaat het signaal verloren en volgt een onterechte timeout. | `Reset()` verplaatst naar `SetParameters()`, dus vóór de raise (`ec5a378`). | Hoog |
| **B2** ✅ | **`TaskDialog.Show` in handlers**: 18 handlers tonen bij een fout een modaal dialoog in Revit, onder andere `DeleteElement`, `CreateRoom`, `TagWalls`, `TagRooms`, `CreateGrid` en ook de eigen `Get/SetParametersEventHandler`. Het MCP-verzoek blokkeert dan tot iemand op OK klikt en loopt vaak in een timeout. | Vervangen door een `ErrorMessage`-property die in de response terugkomt. | Hoog |
| **B3** | **Geen rollback bij een exception in transacties** (CreateRoom, TagWalls, TagRooms, DeleteElement e.a.). | `try/catch` met `tran.RollBack()` als de status nog `Started` is. | Middel |
| **B4** | **Socket-framing**: de server probeert `JSON.parse` op de hele buffer, de plugin leest één `Read` van maximaal 8 KB. Grote of gesplitste berichten kunnen daardoor mislukken. | Berichten afgesloten met een newline aan beide kanten, plus een `StringBuilder`-buffer in de plugin (`SocketService.cs`, `SocketClient.ts`). **Let op:** dit wijzigt het protocol, dus plugin en server moeten tegelijk worden uitgerold. | Middel/hoog |
| **B5** | **Timers blijven hangen in `SocketClient`**: de timeout-timer wordt na een response niet opgeruimd, en `disconnect()` doet `socket.end()` zonder openstaande callbacks af te handelen. | `clearTimeout`, `cleanupAllPending()` en `socket.destroy()`. | Middel |
| **B6** ✅ | **`TcpListener(IPAddress.Any, …)`** in `SocketService.cs`: poort 8080 is bereikbaar vanaf het netwerk, waardoor iedereen op het netwerk code in Revit kan laten uitvoeren via `send_code_to_revit`. | `IPAddress.Loopback`. | **Hoog (beveiliging)** |
| **B7** | **`ExternalEventManager`**: een `NullReferenceException` als er geen document open is, en de singleton is niet thread-safe. | Duidelijke foutmelding plus double-checked lock. | Laag/middel |
| **B8** | **`TransactionUtils` materiaal toewijzen**: zet het materiaal-Id op `ALL_MODEL_INSTANCE_COMMENTS`, wat duidelijk fout is. | `MATERIAL_ID_PARAM` / `STRUCTURAL_MATERIAL_PARAM`. | Laag (alleen als het gebruikt wordt) |
| **B9** | **Element-Id's geparsed als `int`** (o.a. `DeleteElement`, `TagWalls`). Op R24/R26 kunnen Id's groter zijn dan `int`. | `long.TryParse` plus een `ElementIdExtensions.FromLong()`-helper. | Middel |
| **B10** | **Categorieën matchen op gelokaliseerde naam**: in een Nederlandse Revit werken categorienamen als "Walls" niet altijd (bijv. in `ColorSplash`). | Een gedeelde `CategoryResolver`: eerst `BuiltInCategory`, dan de Engelse naam, dan de lokale naam (`8036f00`). | Middel (relevant voor NL-installaties) |
| **B11** | **`ai_element_filter`**: double-waarden komen in interne voet-eenheden terug en het getalformaat hangt af van de cultuurinstelling. | `InvariantCulture` plus omrekening van voet naar mm (`1f54516`). Kan overlappen met de lopende wijzigingen in dat bestand. | Middel |
| **B12** | **`ExecuteCode` (Roslyn)**: CS1704 door dubbele assembly-referenties, bijvoorbeeld als BIM360/ACC geladen is. | Referenties ontdubbeld op naam (`99fb3b7`). | Middel als jullie ACC gebruiken |
| **B13** | **`TagRooms`**: ruimtes met oppervlakte 0 worden stilletjes overgeslagen. | Ze worden nu gerapporteerd in `skipped`. | Laag |
| **B14** | **`CommandManager`**: `GetTypes()` werd per command opnieuw aangeroepen. | Assemblies gecached (`30c15dc`). De `CommandManager` hier is sterk aangepast, dus dit vraagt handwerk. | Laag (alleen opstarttijd) |

## 4. Overige verbeteringen online (geen bugfix)

- **Verbindingen:** maximaal drie nieuwe pogingen bij een verbindingsfout (exponentiële backoff), een timeout per tool (30, 120 of 300 s) en duidelijkere foutmeldingen.
- **Veiligheid:** destructieve tools staan standaard op `dryRun=true` (ook `delete_element`) en vragen bevestiging via een Revit-dialoog (`ConfirmationHelper`).
- **Minder tokens:** een response-compactor (lege velden weg, arrays inkorten), compacte JSON (ongeveer 30% minder), een `fields`-parameter op uitgebreide tools en kortere toolbeschrijvingen.
  - Let op: hier zijn juist bewust de standaardlimieten weggehaald, terwijl online een standaardlimiet van 100 is ingevoerd.
- **Database:** `better-sqlite3` vervangen door `sql.js` (WebAssembly), dus geen native build meer nodig.
- **Plugin-UI:** het Settings-venster heeft zoeken, categoriegroepen en import van een CommandSet. Er is ook een eigen Claude-chatpaneel in Revit (Anthropic API).
- **Diagnose:** `McpLogger` met 7 dagen rotatie, `HealthChecker` die gedownloade DLL's deblokkeert en poorten controleert, een tokenlogger en automatische poortkeuze (8080–8089 via `mcp-port.txt`).
- **Build:** de server wordt gebundeld met esbuild, Revit 2027 wordt ondersteund en er zijn installatiescripts (`INSTALLA.bat`, `install.ps1`).

### Online wijzigingen die botsen met de netwerkopzet hier

Dit is belangrijk bij elke optie hieronder.

- **Tools zijn niet meer los toe te voegen.** `register.ts` importeert online elke tool statisch, en esbuild bundelt de hele server tot één bestand. Een tool toevoegen door alleen een `.js` in `tools/` op de netwerkschijf te zetten werkt daar niet meer.
- **De online `CommandManager` gebruikt `Assembly.LoadFrom`.** Laden vanaf de netwerkschijf op net48 (R24) werkt daarmee niet.
- **Het poortbestand en de `HealthChecker`** gaan uit van een lokale installatie: `%APPDATA%\Autodesk\Revit\Addins\<jaar>\revit_mcp_plugin\` met een gebundelde `node.exe`.
- **Online is alles in het Engels.** De Nederlandse vertaling gaat bij elke grote merge verloren of geeft conflicten.
- **De online `.csproj`'s richten zich op R23 t/m R27, met eigen deploy-scripts.**

## 5. Keuzeopties

### Optie A — Wijzigingen binnenhalen in deze fork

Deze repo blijft de basis. Bugfixes en gewenste tools worden handmatig overgezet, met vertaling naar het Nederlands en aanpassing aan R24/R26.

- **Voordelen**
  - Alle eigen werk en de netwerkopzet blijven onaangetast.
  - Volledige controle: alleen nemen wat nuttig is. Het chatpaneel, installers, esbuild en `calculate_rai` kunnen weg blijven.
  - Kan stap voor stap: eerst B6, B1 en B2, later tools per categorie.
- **Nadelen**
  - Veel handwerk. Directe `git cherry-pick` gaat nauwelijks, omdat online in één commit (`4b2f819`) de hele codebase naar het Engels is vertaald en hier naar het Nederlands, waardoor vrijwel elk bestand conflicteert.
  - Elke toekomstige online release moet opnieuw handmatig worden bekeken. De twee lopen steeds verder uit elkaar.
- **Inschatting**
  - Bugfixes B1 t/m B14: een paar uur tot een dag.
  - Tools: per categorie een halve tot een hele dag, inclusief testen op R24 en R26.
- **Geschikt als:** je maar een beperkt deel van de online tools wilt en het niet erg vindt om later niet meer mee te kunnen synchen.

### Optie B — Opnieuw beginnen vanaf de online versie

Nieuwe basis = online v2.2.1. De eigen wijzigingen (zie §1) worden er één keer opnieuw op aangebracht, daarna wordt verder ontwikkeld zonder te synchen.

- **Voordelen**
  - Direct alle 138 tools en alle bugfixes.
  - Eenmalige klus. Daarna één samenhangende codebase.
- **Nadelen**
  - Alle eigen aanpassingen moeten opnieuw. De netwerkopzet botst met online-features (esbuild-bundel, `LoadFrom`, poortbestand, `HealthChecker`), dus het is aanpassen en niet alleen terugzetten.
  - De Nederlandse vertaling moet opnieuw, of je laat de NL-conventie vallen. Dat betekent een aanpassing in `CLAUDE.md`.
  - Je erft veel waar je niet om hebt gevraagd: het chatpaneel, installers, R23/R27-configuraties en `calculate_rai`. Dat moet je opruimen of accepteren.
  - Daarna loop je opnieuw uit de pas met online. Dezelfde situatie als nu, alleen later.
- **Inschatting:** enkele dagen, plus testen van de hele keten (netwerkschijf, R24 vanaf het netwerk, sync).
- **Geschikt als:** je de meeste online tools wilt en online daarna niet meer hoeft te volgen.

### Optie C — Online als basis die je blijft synchen, met een eigen schil erover

De online repo wordt een `upstream`-remote die regelmatig wordt gemerged. De eigen code komt zoveel mogelijk in aparte bestanden en projecten, zodat merges zelden conflicteren.

Mogelijke opzet:
- **Eigen commands:** een apart commandset-project, bijv. `ModuleFabriqueCommandSet`, met de eigen tools zoals `get_parameters`, `set_parameters` en `manage_materials`. De plugin laadt dat project net als de standaard-commandset dynamisch, via een eigen `command.json`.
- **Eigen server-tools:** losse bestanden in `server/src/tools/`. Online staat elke tool expliciet in `register.ts`, dus daar komt per tool één regel bij. Dat is een klein maar terugkerend merge-punt. Alternatief: een eigen `register_extra.ts` die vanuit `register.ts` wordt aangeroepen.
- **Plugin-aanpassingen**, die niet in een schil passen: netwerkpaden, `Assembly.Load(byte[])` met de resolver, `McpServerSyncService`, het ModuleFabrique-ribbontab en logbestanden per gebruiker. Die worden als kleine, goed afgebakende patches op de online code onderhouden, het liefst achter config-instellingen. Je zou ze ook als PR aan LuDattilo kunnen aanbieden (bijv. "optioneel `NetworkRootPath`" en "`Assembly.Load(byte[])` voor netwerkpaden"), zodat ze vanzelf upstream komen.
- **Deployment:** de esbuild-bundel accepteren, dus de netwerk-sync kopieert de gebundelde build in plaats van losse tool-bestanden. Eigen build-targets (`build:debug`/`build:release`) als aparte npm-scripts naast die van online.
- **Taal:** upstream-code blijft Engels, eigen code in het Nederlands. De conventie in `CLAUDE.md` moet dan worden aangepast naar "alleen eigen code in het Nederlands".

Voor- en nadelen:
- **Voordelen**
  - Toekomstige online tools en fixes komen met een `git merge upstream/main` binnen.
  - Eigen werk blijft duidelijk gescheiden en herkenbaar.
  - Op de lange termijn het minste onderhoud, zolang online actief blijft.
- **Nadelen**
  - De grootste eenmalige herstructurering: een eigen commandset-project opzetten, plugin-patches minimaliseren en de deployment aanpassen aan esbuild.
  - Je bent afhankelijk van de koers van online. Grote refactors daar (zoals de vertaling of esbuild) kunnen je patches opnieuw breken.
  - Plugin-patches blijven een terugkerend merge-punt, tenzij ze upstream worden geaccepteerd.
  - Online is in april 2026 gestopt (laatste commit 9 april). Het is onzeker of er nog veel nieuws komt. Controleer de activiteit voordat je hiervoor kiest.
- **Inschatting:** enkele dagen eenmalig, daarna per online release een korte merge en test.
- **Geschikt als:** online actief blijft en je zijn ontwikkeling wilt blijven volgen.

### Snelle vergelijking

| | A: binnenhalen | B: opnieuw beginnen | C: basis + schil |
|---|---|---|---|
| Eenmalig werk | laag tot middel (stapsgewijs) | middel tot hoog | hoog |
| Werk per toekomstige online release | hoog (handmatig) | n.v.t. (geen sync) | laag |
| Eigen werk behouden | volledig | opnieuw aanbrengen | apart onderhouden |
| Nederlandse conventie | blijft | opnieuw of loslaten | alleen eigen code |
| Ballast (chatpaneel, installers, R23/R27) | geen | ja | ja |
| Risico voor de netwerkopzet | laag | middel | middel |

### Los van de keuze: altijd doen

> **Status:** B1, B2 en B6 zijn op 5 november 2026 doorgevoerd (nog niet gecommit; nog niet getest in Revit).

B6 (`IPAddress.Loopback`), B1 (Reset-race) en B2 (TaskDialogs) zijn kleine fixes met veel effect. Die zijn in elke optie zinvol, en bij A en C kunnen ze direct.

## 6. Naslag

- Een clone van de online repo stond tijdens deze analyse in een tijdelijke map. Opnieuw ophalen: `git clone https://github.com/LuDattilo/revit-mcp-server.git`.
- Je kunt de online repo ook als remote toevoegen, zodat je `git log` en `git diff a61a0d8 upstream/main` kunt gebruiken:
  ```bash
  git remote add upstream-ludattilo https://github.com/LuDattilo/revit-mcp-server.git
  git fetch upstream-ludattilo
  ```
- Belangrijke online commits:

| Commit | Inhoud |
|---|---|
| `4b2f819` | Vertaling naar het Engels en overstap naar de Anthropic API |
| `ec5a378` | Stabiliteitsfixes voor R23 t/m R26, Reset- en TaskDialog-fixes, 15 tools |
| `1f54516` | 7 bugs uit een stresstest |
| `8036f00` | `CategoryResolver` |
| `42233b5` / `ccbfff1` | Socket- en timeoutfixes |
| `71e5a9e` | `dryRun`-standaarden, fix voor globale toestand in `compactTool` |
| `f61ce81` | esbuild-bundel |
| `2c33b84` (v2.2.1) | Stale-result-race, timeouts, `DeleteWorkset` |
