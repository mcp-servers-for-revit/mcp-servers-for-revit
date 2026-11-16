# Vergelijking met constructelligence-lab/revit-mcp

> Opgesteld: 13 november 2026. Vergeleken met de online repo [constructelligence-lab/revit-mcp](https://github.com/constructelligence-lab/revit-mcp), versie **v0.3.0** (commit `bf9745a`, 9 november 2026). Jouw kant: `main` op commit `bf45e5d`, plus de lopende (nog niet gecommitte) wijzigingen.
> Doel van dit document: de verschillen in **software-architectuur** en **tool-opbouw** op een rij zetten, met wat aan welke kant beter of slechter is, zodat je kunt kiezen wat je overneemt (zie [§6 Keuzeopties](#6-keuzeopties)). Er is nog geen code aangepast.

## 1. Uitgangssituatie

Anders dan bij [LuDattilo](UPSTREAM-VERGELIJKING.md) is dit **geen fork van dezelfde basis**. Het is een eigen, los geschreven project zonder gemeenschappelijke git-geschiedenis. Mergen of cherry-picken kan dus niet; alleen ideeën en code handmatig overnemen.

| | Jouw fork | Online (constructelligence) |
|---|---|---|
| Herkomst | Fork van `mcp-servers-for-revit` (feb 2026) | Nieuw project, eerste commit 28 sep 2026 |
| Omvang | ~24.000 regels C#/TS (plugin, commandset, server) | ~2.400 regels C# + ~1.700 regels JS (+ 240 regels Blender-Python) |
| Commits | ruim 20 eigen + upstream-historie | 12 |
| Tools | 33 tool-bestanden, waarvan 22 actief (11 uitgeschakeld in `register.ts`) | 28 tools, 5 prompts, 3 resources |
| Revit-versies | 2024, 2026 | 2024, 2025, 2026, 2027 |
| Karakter | **Modelleren en wijzigen**: elementen maken, wijzigen, verwijderen, taggen, willekeurige C# uitvoeren | **Lezen, renderen, exporteren**: standaard read-only, twee kleine schrijf-tools |
| In productie gebruikt | Ja, door het team via de netwerkschijf | **Nee**: de README zegt zelf dat de add-in "has not yet been run against a live Revit by its author". Alleen gecompileerd in CI. |
| Taal | Nederlands | Engels |

Het laatste punt is belangrijk voor alles hieronder: de architectuur online is netjes ontworpen en goed getest *zonder* Revit, maar niet bewezen *in* Revit. Behandel de online code als een goed referentieontwerp, niet als beproefde code.

## 2. Architectuur naast elkaar

```
Jij:     MCP-client ─stdio─ server/ (TS, 1 bestand per tool) ─TCP 8080, JSON-RPC, geen framing, geen auth─ plugin/ (C#) ─laadt dynamisch─ commandset/ (C#, Command + Handler per tool)
Online:  MCP-client ─stdio─ server.mjs (1 catalogus) ─HTTP 127.0.0.1:8712, bearer-token, JSON-envelope─ addin/ (C#, 1 assembly, 1 dispatcher, ops als functies)
```

### 2.1 Opdeling in componenten

| | Jouw fork | Online |
|---|---|---|
| Lagen in Revit | Twee assemblies: `plugin` (host, socket, UI) + `commandset` (de Revit-werk), dynamisch geladen via reflectie op `IRevitCommand` | Eén assembly `RevitMcp.dll`: bridge, dispatcher en handlers samen |
| Externe afhankelijkheid | `RevitMCPSDK` (NuGet, bron niet in repo) voor alle basisklassen | Geen SDK. Alleen `Nice3point.Revit.Api.*` als compile-time referentie |
| Uitbreiden | Nieuwe commandset-DLL op de netwerkschijf zetten, aan/uit per command via Settings-venster | Code aanpassen en opnieuw bouwen |

- **Voordeel jij:** de plugin/commandset-scheiding plus `command.json`/`commandRegistry.json` maakt teambrede uitrol en aan/uitzetten zonder herbouw mogelijk. Dat heeft online niet, en het past precies bij jullie netwerkopzet.
- **Nadeel jij:** de prijs is complexiteit en een fragiele afhankelijkheid. De `RevitMCPSDK`-typeidentiteit (zie de valkuil in `CLAUDE.md`) laat een command stil wegvallen; de `Assembly.Load(byte[])` + `AssemblyResolve`-constructie is nodig om dat dynamisch laden vanaf het netwerk werkend te houden. Online heeft geen van die problemen omdat er niets dynamisch geladen wordt.
- **Voordeel online:** veel kleiner en in één keer te overzien. Alles wat een netwerkverzoek in Revit-code omzet staat in één `switch` (`RevitOps.Run`).

### 2.2 Transport

| | Jouw fork | Online |
|---|---|---|
| Protocol | Raw TCP, JSON-RPC 2.0, geen scheidingsteken | HTTP (`HttpListener`), twee routes: `GET /health`, `POST /op` |
| Framing | `JSON.parse` op de hele buffer; plugin leest één `Read` van max. 8 KB (B4 uit de LuDattilo-vergelijking, nog open) | HTTP regelt de berichtgrenzen; body max. 1 MB |
| Binding | `IPAddress.Loopback` (sinds B6) | `127.0.0.1` |
| Authenticatie | Geen | Bearer-token, bij eerste start gegenereerd in `~/.revit-mcp/token`, vergeleken in constante tijd |
| Gelijktijdigheid | Eén TCP-verbinding, server-side mutex (`connectionMutex`) | Elke call een eigen HTTP-request; Revit-thread serialiseert via een wachtrij |
| Foutvorm | Exception-tekst in JSON-RPC-error, op de server een tekstregel `"... failed: ..."` | Vaste envelope `{ ok, result }` / `{ ok:false, error:{ code, message } }` met ~15 gedocumenteerde foutcodes |
| Contract | Impliciet (methodenaam = commandnaam) | Uitgeschreven in `docs/protocol.md`, getest tegen de catalogus |

**Online is hier duidelijk beter.** HTTP lost het framing-probleem (B4) zonder eigen protocolwerk op, het token voorkomt dat een ander lokaal proces of een andere gebruiker op een gedeelde machine (RDS/Citrix) code in Revit laat draaien via `send_code_to_revit`, en vaste foutcodes laten de client (en het taalmodel) gericht reageren. Het enige pluspunt van jouw TCP-variant is dat hij al werkt en dat team-installaties erop afgestemd zijn; omschakelen raakt plugin én server tegelijk.

### 2.3 Revit-thread (ExternalEvent)

| | Jouw fork | Online |
|---|---|---|
| Patroon | Per tool een eigen `IExternalEventHandler` + `ManualResetEvent`, gecached per sleutel in `ExternalEventManager` | Eén `RevitDispatcher`: een `ConcurrentQueue` met closures, één `ExternalEvent`, `TaskCompletionSource` per verzoek |
| Boilerplate per tool | Command-klasse + Handler-klasse (~80–200 regels samen), elk met eigen `Reset`/`Wait`/`Signal`-logica | Eén functie `Func<UIApplication, object>` |
| Bekende valkuilen | Reset-race (B1, opgelost), `TaskDialog` in handlers (B2, opgelost), per command een `_executionLock` | Geen per-tool synchronisatie, dus die klasse fouten kan niet ontstaan |
| **Timeout bij een openstaand dialoog** | Het command gooit na bijv. 15 s een `TimeoutException`, maar het `ExternalEvent` blijft staan. Zodra het dialoog sluit **voert Revit de handler alsnog uit**. Bij `delete_element` betekent dat: de client hoort "mislukt", en een minuut later worden de elementen tóch verwijderd. (De beschrijving van `set_parameters` erkent dit al en noemt opnieuw proberen veilig; voor verwijderen en aanmaken is het dat niet.) | **Abandoned-request-regel**: een verzoek dat al een timeout gaf wordt gemarkeerd en overgeslagen als het aan de beurt komt. "Het is niet gebeurd" blijft waar. |

**Online is hier beter, en het timeout-punt is een echte bug aan jouw kant** (vooral voor schrijvende tools). De centrale dispatcher is ook een betere basis dan twintig losse handlers: B1 en B2 bestonden juist omdat elke handler dezelfde synchronisatie zelf opnieuw implementeert. Je kunt dit overnemen zonder de rest om te gooien: een `abandoned`-vlag in de handler (gezet bij timeout, gecontroleerd aan het begin van `Execute`) lost de bug op binnen het huidige patroon.

### 2.4 Veiligheid en schrijfrechten

| | Jouw fork | Online |
|---|---|---|
| Standaard | Alles mag: maken, wijzigen, verwijderen, willekeurige C# (`send_code_to_revit`) | Read-only |
| Schrijven aanzetten | n.v.t. (alleen tools aan/uit per command) | **Twee schakelaars**: `allowWrites` in de add-in én `--allow-writes` op de server. Eén alleen is niet genoeg. |
| Preview | Geen `dryRun` | `apply: false` is standaard; antwoord toont oude en nieuwe waarde |
| Transacties | Eén per command, rollback ontbreekt nog in een aantal handlers (B3) | Eén per write, met `RollBack()` bij een exception; renders en exports in een `TransactionGroup` die altijd wordt teruggedraaid |
| Typeparameters | Kunnen gewijzigd worden | Geweigerd met uitleg ("dit verandert elke instantie") |
| MCP-annotaties | Geen | `readOnlyHint`, `destructiveHint`, `idempotentHint` per tool, zodat de client zelf om bevestiging kan vragen |
| Dreigingsmodel | Niet beschreven | `docs/security.md`, inclusief wat *niet* beschermd is |

Dit is een **ontwerpkeuze, geen pure kwaliteitsvraag**. Online kiest bewust voor "kan bijna niets veranderen" (geen create/delete/move, staat expliciet onder *Not planned*). Dat past niet bij jullie gebruik, waar het modelleren juist het doel is. Wat wel overdraagbaar is zonder die keuze te volgen:
- `dryRun`/`apply`-parameter op destructieve tools (`delete_element`, `operate_element`, `set_parameters`), standaard op preview;
- MCP-annotaties per tool, zodat Claude Desktop/Code zelf om bevestiging vraagt bij `destructiveHint`;
- `TransactionGroup` + rollback voor tijdelijke views;
- een team-instelling om `send_code_to_revit` alleen voor bepaalde gebruikers aan te zetten.

### 2.5 Configuratie en deployment

| | Jouw fork | Online |
|---|---|---|
| Configuratie | `RevitMCPPlugin.dll.config` (centraal pad, sync-paden), `commandRegistry.json` op het netwerk | `%APPDATA%\revit-mcp\settings.json` per gebruiker (`port`, `allowWrites`, `token`, `autoStart`) + omgevingsvariabelen |
| Uitrol | Release-builds naar `O:\`, `McpServerSyncService` spiegelt de server bij elke Revit-start naar de lokale map | Per gebruiker `dotnet build` en handmatig kopiëren, geen installer |
| Logs | Per gebruiker op de netwerkschijf | Lokaal `bridge.log` |
| Ribbon | Toggle-knop in het ModuleFabrique-tab met statusicoon | Eigen tab `revit-mcp`, knoppen "Bridge status" en "Settings folder" |
| Server-runtime | TypeScript, `tsc`, losse bestanden | Plain ESM-JavaScript (`.mjs`), geen build-stap; 2 dependencies |

**Jij bent hier duidelijk beter** voor een team: centraal beheer, automatische sync en geen handwerk per werkplek. Online is gemaakt voor één gebruiker op één werkstation. Eén online idee is wel nuttig: de **statusdialoog** (adres, schrijfrechten, aantal verzoeken in de wachtrij) is bij storingen informatiever dan alleen een gekleurd icoon.

### 2.6 Testen en kwaliteitsborging

| | Jouw fork | Online |
|---|---|---|
| Tests | TUnit-integratietests (enkele klassen) die een draaiende Revit 2026 nodig hebben | 43 Node-tests tegen een **mock-model** (`mock.mjs`): catalogus, bridge-faalgevallen, end-to-end MCP over stdio, schrijfpoort |
| CI | `release.yml` geërfd en verouderd | Server op Node 20/22, add-in gebouwd tegen Revit 2025/2026/2027 en net48 |
| Documentatie-drift | n.v.t. | Toolstabel in README gegenereerd uit de catalogus; CI faalt bij afwijking |
| Echte Revit | Ja, dagelijks gebruikt | Nooit getest |

- **Online beter** in het borgen van de server-kant: alle tool-schema's, foutpaden en de schrijfpoort zijn testbaar zonder Revit. Bij jou is de TS-server feitelijk ongetest.
- **Jij beter** in wat er echt toe doet: jouw code draait in Revit. De online mock bewijst niet dat de Revit-API-aanroepen kloppen, alleen dat ze compileren.
- Overdraagbaar idee: een CI-job die `plugin` en `commandset` bouwt tegen `Nice3point.Revit.Api` (geen Revit-licentie nodig) zou SDK- en API-breuken vroeg vangen.

### 2.7 Samenvatting architectuur

| Aspect | Beter | Waarom |
|---|---|---|
| Teamdeployment, centraal beheer | **Jij** | Netwerkschijf, sync, centrale registry; online heeft niets vergelijkbaars |
| Uitbreidbaarheid zonder herbouw | **Jij** | Dynamisch geladen commandsets |
| Eenvoud, onderhoudbaarheid | **Online** | 1 assembly, 1 dispatcher, 1 catalogus; geen externe SDK |
| Transport en protocol | **Online** | HTTP, auth-token, foutcodes, gedocumenteerd contract |
| Thread-marshalling | **Online** | Centrale wachtrij, abandoned-request-regel |
| Veiligheid | **Online** | Read-only standaard, preview, annotaties (maar past niet 1-op-1 bij jullie doel) |
| Testbaarheid zonder Revit | **Online** | Mock + 43 tests + CI |
| Bewezen in productie | **Jij** | Online is nooit in Revit gedraaid |

## 3. Tool-opbouw

### 3.1 Wat kost het om één tool toe te voegen

**Jij — vier plekken:**
1. `server/src/tools/<naam>.ts`: zod-schema, beschrijving, `withRevitConnection`, try/catch, tekst-response (~40–150 regels, grotendeels kopie).
2. `commandset/Commands/<Naam>Command.cs`: parameters parsen, `ResetCompletion`, `RaiseAndWaitForCompletion`, resultaat teruggeven.
3. `commandset/Services/<Naam>EventHandler.cs`: de Revit-code plus `ManualResetEvent`-plumbing.
4. `commandset/command.json`: manifestregel (en aanzetten in `commandRegistry.json`).

**Online — drie plekken, waarvan twee een paar regels:**
1. Eén object in `server/tools.mjs`: `name`, `op`, `write`, `description`, `params` (zod).
2. Eén regel in de `switch` in `RevitOps.Run` en één functie in `addin/Handlers/*.cs` die een anoniem object teruggeeft.
3. Eén regel in `docs/protocol.md` (de test faalt anders) en eventueel een mock-antwoord.

De server-registratie is bij online volledig generiek: één lus over `TOOLS` doet schema, annotaties, schrijfpoort, foutafhandeling en (voor beelden) image-blokken. Bij jou zit dezelfde try/catch- en response-code gekopieerd in elk toolbestand.

- **Online beter** voor ontwikkelsnelheid en consistentie: alle tools gedragen zich gegarandeerd hetzelfde bij fouten.
- **Jij beter** voor jullie distributie: een los `.js`-bestand in de netwerk-`tools/`-map is direct een nieuwe teamtool. Met een centrale catalogus zou elke nieuwe tool een wijziging in één gedeeld bestand zijn. Dat is geen groot bezwaar (de sync kopieert dat bestand ook), maar het verlies van "los bestand erin = tool erbij" is reëel.
- **Tussenvorm:** je auto-discovery houden, maar de herhaalde code in een helper stoppen (bijv. `defineRevitTool({ name, method, description, schema, destructive })`) die registratie, fout-envelope, `isError` en annotaties regelt. Dan wordt elk toolbestand ~10 regels en blijft drop-in werken.

### 3.2 Granulariteit en ontwerp van de tools

| | Jouw fork | Online |
|---|---|---|
| Stijl | Mix van **generieke** tools (`ai_element_filter`, `operate_element`, `modify_element`, `send_code_to_revit`) en specifieke creatietools | **Eén vraag, één tool** (`list_rooms`, `wall_layers`, `model_warnings`), plus een paar tools met een `kind`/`format`-keuze (`project_structure`, `export_model`) |
| Ontsnappingsroute | `send_code_to_revit`: alles kan | Bestaat niet, bewust |
| Samengestelde tools | Niet; elke tool = één command | Ja, server-side: `render_gallery` doet `scene.info` + n× `scene.render`, `render_photoreal` doet export + Blender. Die logica is in JS getest zonder Revit. |
| Paging/limieten | Bewust **geen** standaardlimiet | `limit` standaard 50, max 500, `offset`, en altijd `total_matched`, `scanned`, `truncated` in het antwoord |
| Eenheden | Wisselend: `set_parameters` werkt in weergave-eenheden, `ai_element_filter` geeft nog interne voet terug (B11) | Altijd omgerekend naar de documenteenheid; m²/m³ bij hoeveelheden |
| Ids | Deels als `int` geparsed (B9) | `long` overal |

Beoordeling:
- **Generiek vs. specifiek.** Jouw generieke tools zijn krachtiger, maar het taalmodel moet meer zelf bedenken en kan meer fout doen. Online specifieke tools geven voorspelbaardere antwoorden en kleinere responses. Voor *lezen* is de online aanpak beter; voor *modelleren* heb je de generieke kracht nodig. Beide naast elkaar is prima.
- **Limieten.** Dat je de limieten bewust weghaalde heeft een reden, maar de online combinatie is beter: een standaardlimiet **plus** `truncated: true` en `total_matched`, zodat het model weet dat er meer is en zelf kan pagineren. Geen limiet riskeert responses die het contextvenster vullen; een stille limiet riskeert onvolledige antwoorden. De online variant voorkomt allebei.
- **Samengestelde tools in de server** zijn een goed patroon: orkestratie in testbare JS, Revit-ops klein houden.

### 3.3 Beschrijvingen en sturing van het taalmodel

| | Jouw fork | Online |
|---|---|---|
| Toolbeschrijvingen | Wisselend. De geërfde tools: kort, functioneel, Nederlands ("Verkrijgt gedetailleerde informatie over …"). De eigen tools (`set_parameters`, `get_parameters`): uitgebreid, Engels, met eenheden en herstelinstructies — al op het online niveau. | Overal langer, gericht op **wanneer** en **in welke volgorde**: "Call this first when anything else fails", "ids come from query_elements", "Use it before set_parameter" |
| Server-`instructions` | Geen | Ja: begin met `revit_status`, verzin nooit ids, vraag om een ja vóór `apply: true` |
| MCP-prompts | Geen | 5 workflow-prompts (`model_health_check`, `sheet_qa`, `parameter_audit`, …) met stappenplan en huisregels |
| MCP-resources | Geen | 3 live resources (`revit://status`, `revit://model/overview`, `revit://warnings`) |
| Beelden | n.v.t. | Renders komen terug als **image-content**, zodat het model het resultaat kan zien en bijsturen |

**Online duidelijk beter.** Het gebruikt de MCP-mogelijkheden (instructions, prompts, resources, annotaties, images) die jouw server laat liggen, en de beschrijvingen sturen het model consequent naar de juiste volgorde. Bij jou is dat niveau er alleen in de eigen tools; de geërfde tools trekken het gemiddelde omlaag. Dit is het goedkoopst over te nemen deel van de hele vergelijking: alleen server-side, geen Revit-wijziging, geen protocolwijziging. Prompts zijn ook een natuurlijke plek voor jullie eigen werkwijzen (bijv. een ModuleFabrique-modelcheck).

Over de taal: de beschrijvingen zijn nu gemengd (geërfd Nederlands, eigen Engels). Kies één lijn. Engels voor de **beschrijvingen voor het model** ligt het meest voor de hand (zo heb je je eigen tools al geschreven), met Nederlands voor code-commentaar, logs en gebruikersmeldingen. Dat is een keuze voor `CLAUDE.md`, geen fout.

### 3.4 Foutafhandeling in tools

| | Jouw fork | Online |
|---|---|---|
| Foutvlag | Tekst `"... failed: ..."` als **gewone** content; `isError: true` alleen in de uitgeschakelde opslag-tools | Altijd `isError: true` |
| Foutcode | Geen | `code` (bijv. `not_found`, `timeout`, `writes_disabled`) |
| Hint voor herstel | Geen | Per code een `hint` ("A modal dialog in Revit blocks the add-in; close it and retry.") |
| Foutmeldingen uit Revit | Exception-tekst | Melding met de volgende stap ("query_elements returns the ids that exist") |

**Online beter.** Zonder `isError` ziet de client een mislukte call als een geslaagde met een raar antwoord. Dit is een kleine, mechanische fix (met de helper uit §3.1 in één keer voor alle tools).

### 3.5 Tools: wat online heeft en jij niet

Online-tools die bij jou ontbreken (of alleen via `send_code_to_revit` kunnen) en waarschijnlijk nuttig zijn voor jullie:

| Categorie | Online tool | Opmerking |
|---|---|---|
| Overzicht | `revit_status`, `model_overview`, `list_levels`, `list_categories` | Goede startpunten voor het model; `analyze_model_statistics` bij jou staat uit |
| Lezen | `element_details`, `list_views`, `list_sheets`, `list_families`, `list_schedules`, `read_schedule` | `read_schedule` is waardevol voor hoeveelheden |
| Model-QA | `model_warnings`, `check_clashes` | `check_clashes` gebruikt `ElementIntersectsElementFilter`, geen links |
| Ruimtes/opbouw | `list_rooms` (met contouren), `wall_layers`, `geometry_quantities`, `list_materials` | Overlapt deels met `manage_materials` en `get_material_quantities` (uit) |
| Structuur | `project_structure` (grids, links, fasen, ontwerpopties, worksets, revisies) | Eén tool met `kind` |
| Export | `export_model` (PDF, DWG, IFC4, glTF), `export_view_image` | Alles in `%TEMP%`, met `TransactionGroup`-rollback |
| Beelden | `scene_info`, `render_scene`, `render_gallery`, `render_photoreal`, `render_turntable` | Blender/Cycles lokaal; los van de rest bruikbaar. Grootste niet-geteste deel. |
| Schrijven | `create_schedule` | Met preview |

Omgekeerd heeft online **niets** van jouw creatie- en wijzigtools (`create_*`, `delete_element`, `modify_element`, `operate_element`, `color_elements`, `tag_*`, `create_dimensions`, `send_code_to_revit`, `set_parameters` op meerdere elementen, typeparameters). Dat staat daar expliciet niet op de planning.

## 4. Totaaloordeel

| Onderdeel | Beter | Overnemen? |
|---|---|---|
| Netwerkdeployment, centrale registry | Jij | n.v.t. |
| Dynamisch laden van commandsets | Jij (voor jullie situatie) | n.v.t. |
| Modelleer- en wijzigtools | Jij | n.v.t. |
| Abandoned-request bij timeout | Online | **Ja, hoge prioriteit** (bug bij schrijvende tools) |
| `isError` + foutcodes + hints | Online | **Ja**, klein en mechanisch |
| Tool-annotaties (`readOnlyHint`/`destructiveHint`) | Online | **Ja**, klein |
| Server-`instructions`, prompts, resources | Online | **Ja**, alleen server-side |
| Standaardlimiet + `truncated`/`total_matched` + `offset` | Online | Ja, heroverweeg de weggehaalde limieten |
| `dryRun`/preview op destructieve tools | Online | Ja, op `delete_element` en bulkwijzigingen |
| Eenheden naar documenteenheid, `long`-ids | Online | Ja (sluit aan op B9/B11) |
| Leestools (`read_schedule`, `list_rooms`, `wall_layers`, `model_warnings`, `project_structure`, …) | Online | Selectief, als eigen commands |
| HTTP + bearer-token i.p.v. raw TCP | Online | Overwegen; raakt plugin en server tegelijk (zelfde uitrolprobleem als B4) |
| Centrale dispatcher i.p.v. handler per tool | Online | Op termijn; groot, raakt `RevitMCPSDK`-basisklassen |
| Mock-model + server-tests + CI-build zonder Revit | Online | Nuttig, middelgroot |
| Renderen via Blender | Online | Alleen als jullie dit willen; los project, ongetest |
| Read-only als standaard | Online (voor hun doel) | Nee, past niet bij jullie gebruik |

## 5. Risico's van de online code

- **Nooit in Revit gedraaid.** Alleen gecompileerd. Vooral de render- en exportpaden (`TransactionGroup`, `ImageExportOptions`, `CustomExporter` voor glTF) kunnen in de praktijk anders uitpakken. Elke overgenomen Revit-functie moet je zelf testen op R24 en R26.
- **Jong en klein project.** 12 commits, 1 ster, één auteur. Geen basis om op te bouwen en te blijven synchen.
- **Eén gebruiker per werkstation.** Settings en token in het gebruikersprofiel; geen centrale configuratie.
- **Token naast een netwerkdeployment.** Als je de token-aanpak overneemt: het tokenbestand moet per gebruiker lokaal blijven (niet op `O:\`), anders kan iedereen met toegang tot de share het lezen.

## 6. Keuzeopties

### Optie A — Ideeën en tools overnemen in deze fork (aanbevolen)

Deze repo blijft de basis. Je neemt patronen en losse tools over, vertaald naar het Command/Handler-patroon en (voor commentaar) naar het Nederlands.

Volgorde naar verhouding kosten/baten:
1. **Abandoned-flag bij timeout** in de handlers van schrijvende commands (`delete_element`, `operate_element`, `modify_element`, `set_parameters`, `create_*`). Klein, lost een echte bug op.
2. **Server-helper** (`defineRevitTool`) met `isError`, foutcode, hint en MCP-annotaties; daarna alle toolbestanden omzetten. Mechanisch, alleen server.
3. **Server-`instructions` en een paar prompts** (modelcheck, parameteraudit) en een `revit://status`-resource.
4. **Paging-patroon** (`limit` + `offset` + `total_matched`/`truncated`) op `ai_element_filter` en `get_current_view_elements`.
5. **`dryRun`** op `delete_element` en bulkwijzigingen.
6. **Leestools** naar keuze als nieuwe commands: `read_schedule`, `model_warnings`, `list_rooms`, `wall_layers`, `project_structure`, `export_model`.

- **Voordelen:** eigen werk en netwerkopzet blijven intact; elk stap is los te testen en uit te rollen; stappen 1–4 raken geen protocol.
- **Nadelen:** handwerk; de online code is niet één-op-één bruikbaar door het andere handlerpatroon en de ontbrekende SDK-laag.
- **Inschatting:** stappen 1–3 samen ongeveer een dag; daarna per leestool een paar uur inclusief test op R24/R26.

### Optie B — Online als tweede MCP-server naast de eigen

Beide add-ins tegelijk in Revit laden (poort 8080 en 8712 conflicteren niet) en beide als aparte MCP-server in de client configureren: jouw server voor modelleren, de online server voor lezen, renderen en exporteren.

- **Voordelen:** direct 28 extra tools zonder ze te porten; render-pipeline erbij.
- **Nadelen:** nooit in Revit getest; twee tabbladen en twee installaties; geen netwerkdeployment (het DLL zou je wel via de `AddinFiles`-map kunnen uitrollen, maar settings en token blijven per gebruiker); dubbele en overlappende tools verwarren het model; Engels tegenover Nederlands.
- **Geschikt als:** je vooral snel de render-/exportfuncties wilt proberen, op één werkplek, voordat je iets port.

### Optie C — Overstappen op de online architectuur

De eigen tools herschrijven op de online opzet (één add-in, HTTP-bridge, centrale dispatcher, catalogus), en de netwerkdeployment daarop opnieuw bouwen.

- **Voordelen:** eenvoudiger, beter testbare codebase; transport-, framing- en timeoutproblemen in één keer weg; geen `RevitMCPSDK`-afhankelijkheid meer.
- **Nadelen:** feitelijk een herschrijving van alle ~20 commands; je verliest het dynamisch laden van commandsets (of moet het opnieuw bouwen); de basis is zelf onbewezen in Revit; geen upstream om mee te synchen, dus geen langetermijnvoordeel ten opzichte van optie A.
- **Inschatting:** enkele weken inclusief testen op R24/R26 en de netwerkketen.
- **Geschikt als:** je toch al overweegt de `RevitMCPSDK`-laag kwijt te raken. Overweeg dan eerder een tussenvorm: de online `RevitDispatcher`-aanpak binnen je eigen plugin, met de commandsets als bron van functies in plaats van handlers.

### Snelle vergelijking

| | A: overnemen | B: naast elkaar | C: overstappen |
|---|---|---|---|
| Eenmalig werk | laag tot middel (stapsgewijs) | laag | hoog |
| Risico voor de netwerkopzet | laag | laag | hoog |
| Risico door ongeteste online code | laag (alleen wat je test) | hoog | hoog |
| Eigen werk behouden | volledig | volledig | herschrijven |
| Architectuurwinst | gedeeltelijk | geen | volledig |
| Extra tools | wat je port | alle 28 | wat je port |

## 7. Naslag

- Opnieuw ophalen: `git clone https://github.com/constructelligence-lab/revit-mcp.git`.
- Belangrijkste bestanden online:

| Bestand | Inhoud |
|---|---|
| `server/tools.mjs` | De toolcatalogus: naam, op, write-vlag, zod-schema, beschrijving |
| `server/server.mjs` | Generieke registratie, schrijfpoort, samengestelde tools, prompts, resources |
| `server/bridge.mjs` | HTTP-client, token, timeouts, foutmeldingen |
| `server/mock.mjs` | Het sample-model voor tests en demo |
| `addin/Bridge/BridgeServer.cs` | `HttpListener`, token, schrijfpoort, JSON-envelope |
| `addin/Bridge/RevitDispatcher.cs` | Wachtrij + `ExternalEvent` + abandoned-request-regel |
| `addin/Handlers/RevitOps.cs` | De `switch` van op naar functie, gedeelde helpers (eenheden, ids, parameters) |
| `addin/Handlers/WriteOps.cs` | `set_parameter`: preview, één transactie, rollback, eenheden |
| `docs/protocol.md` / `docs/security.md` | Het contract en het dreigingsmodel |

## 8. Aanvulling: robuustheid en timeouts bij veel data (drie repos)

> Opgesteld: 14 november 2026. Los toegevoegd en nog niet verwerkt in §2.2/§2.3; samenvoegen kan later. Hier is ook de pyRevit-variant [mcp-servers-for-revit/mcp-server-for-revit-python](https://github.com/mcp-servers-for-revit/mcp-server-for-revit-python) (commit `6ca46b3`, 14 oktober 2026) meegenomen. Vraag: welke opzet is het meest robuust, en welke geeft de minste kans op timeouts als er meer data door moet?

### 8.1 Transport naast elkaar

| | Jouw fork | Constructelligence | Python (pyRevit) |
|---|---|---|---|
| Listener in Revit | `TcpListener` op poort 8080 (eigen code) | `HttpListener` (`addin/Bridge/BridgeServer.cs`) | pyRevit Routes, een HTTP-server die pyRevit zelf meelevert (poort 48884) |
| Protocol | JSON-RPC 2.0 zonder scheidingsteken | `POST /op` met `{ op, args }`, antwoord `{ ok, result \| error }` | REST, een URL per functie (`GET /revit_mcp/get_view/<naam>`) |
| Berichtgrenzen | Geen: de client parset de buffer tot het lukt, de plugin leest één `Read` | HTTP (Content-Length), body maximaal 1 MB | HTTP |
| Client | Node `net.Socket` + `connectionMutex` | Node `fetch` + `AbortSignal.timeout` | Python `httpx` |
| Revit-thread | Per command een eigen ExternalEvent + handler | Eén `RevitDispatcher`: één ExternalEvent met een wachtrij van closures | pyRevit regelt dat zelf |
| Beveiliging | Alleen localhost | Localhost + bearer-token | Alleen loopback + een pairing-geheim per Windows-gebruiker + een bevestigingsvenster in Revit voor wijzigingen en code |

### 8.2 Zwakke plekken in jouw transport bij grotere data

1. **Verzoeken groter dan 8 KB gaan stuk.** Dit is B4 uit de LuDattilo-vergelijking en het staat nog open. In [SocketService.cs:172-198](plugin/Core/SocketService.cs#L172-L198) leest de plugin één keer maximaal 8192 bytes en geeft dat direct aan `ProcessJsonRPCRequest`:
   ```csharp
   byte[] buffer = new byte[8192];
   bytesRead = stream.Read(buffer, 0, buffer.Length);
   string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
   string response = ProcessJsonRPCRequest(message);
   ```
   - Een lange `send_code_to_revit`, een lijst met honderden element-ID's of een grote batch om elementen aan te maken geeft een parse-fout. Wat er na de eerste 8 KB komt, wordt als een tweede, kapot verzoek gelezen.
   - Ook kleinere berichten kunnen in TCP in twee stukken binnenkomen. Lokaal gebeurt dat zelden, maar het is geen garantie.
   - Een UTF-8-teken van meerdere bytes (bijvoorbeeld een `ë` in een Nederlandse parameternaam) kan precies op de 8 KB-grens worden doorgeknipt.
2. **Grote antwoorden worden steeds trager geparsed.** `SocketClient.ts` voegt elk binnenkomend stuk toe aan `this.buffer` en probeert dan `JSON.parse` op de **hele** buffer. Bij antwoorden van een paar MB (duizenden elementen, base64-afbeeldingen) gaat dat kwadratisch omhoog. Het werkt nog wel.
3. **Timeouts zijn krap en passen niet bij elkaar.** De client wacht 120 s, maar de commands geven zelf veel eerder op. Telling van `RaiseAndWaitForCompletion(...)` in `commandset/Commands`:

   | Timeout | Aantal commands |
   |---|---|
   | 10 s | 8 |
   | 15 s | 8 |
   | 20 s | 2 |
   | 60 s | 6 |
   | 120 s | 3 |

   Commands die veel data lezen, lopen bij een groot model als eerste tegen 10–15 s aan.
4. **Na een timeout draait de handler alsnog.** Het eerste deel (een schrijfactie wordt alsnog uitgevoerd) staat al in §2.3. Daarnaast kan een **lezend** command een fout resultaat geven: de late uitvoering van verzoek 1 zet het `ManualResetEvent` op het moment dat verzoek 2 van hetzelfde command al wacht. Verzoek 2 krijgt dan het resultaat van verzoek 1 terug, of een half resultaat.

### 8.3 Beoordeling

| | Jouw fork | Constructelligence | Python (pyRevit) |
|---|---|---|---|
| Grote berichten heen | ❌ maximaal 8 KB, geen framing | ✅ | ✅ |
| Grote berichten terug | ⚠️ werkt, maar kwadratisch parsen | ✅ | ✅ |
| Verwerkingssnelheid in Revit | ✅ gecompileerd C# | ✅ gecompileerd C# | ⚠️ Python-interpreter; lussen over veel elementen zijn merkbaar trager |
| Timeouts | ⚠️ per command vastgelegd, vaak 10–15 s | ✅ per soort: lezen 30 s, schrijven 60 s, render/export 180 s | ⚠️ alleen aan de client-kant (30/60/120 s) |
| Gedrag na een timeout | ❌ de handler draait later alsnog en kan een later verzoek een verkeerd resultaat geven | ✅ het verzoek wordt als "abandoned" gemarkeerd en overgeslagen | ? niet uitgezocht (interne werking van pyRevit Routes) |
| Gelijktijdige verzoeken | ✅ mutex | ✅ wachtrij | ✅ pyRevit zet ze in een rij |

**Conclusie:** constructelligence is het meest robuust. Jouw fork komt qua verwerkingssnelheid even ver (beide C#), maar verliest het op transport en timeout-afhandeling. De pyRevit-variant heeft wel een betrouwbaar transport, maar is het traagst zodra er veel elementen verwerkt moeten worden.

Twee kanttekeningen:
- De pure verwerkingstijd hangt vooral af van het werk op Revit's thread (collectors, parameters lezen), niet van het transport.
- Er is niets gemeten. Dit is een beoordeling op basis van de code.

### 8.4 Advies

Overstappen is niet nodig: de C#-opzet en de netwerkdeployment zijn goed. Neem de sterke punten van constructelligence over binnen de huidige opzet:

1. **Framing in het transport** (lost punt 1 en 2 op):
   - Zet een lengte vóór elk bericht, of een `\n` erachter. JSON van `JsonConvert`/`JSON.stringify` bevat zonder opmaak geen kale newlines, dus `\n` is de kleinste ingreep.
   - Plugin: lees tot het bericht compleet is (bijvoorbeeld met een `StreamReader.ReadLine()` op de `NetworkStream`, dan gaat UTF-8 ook goed), en sluit antwoorden af met `\n`.
   - `SocketClient.ts`: splits de buffer op `\n` in plaats van de hele buffer te parsen.
   - **Let op de uitrol:** plugin en server moeten tegelijk worden uitgerold. `McpServerSyncService` haalt de server pas op bij een Revit-start, en een Revit-sessie die al draait houdt de oude plugin-DLL. Een tussenvorm die beide formaten accepteert (bijvoorbeeld de oude parse-poging laten staan zolang er geen `\n` binnenkomt) voorkomt dat teamleden met een oude en een nieuwe helft langs elkaar heen praten.
2. **Afgelopen verzoeken overslaan** (lost punt 4 op, en de schrijf-bug uit §2.3):
   - Geef elk verzoek een volgnummer of een `abandoned`-vlag die bij een timeout wordt gezet.
   - De handler controleert die vlag aan het begin van `Execute` en slaat het werk over als hij gezet is.
   - De handler zet het signaal alleen voor het verzoek dat bij dat nummer hoort.
   - Dit kan centraal in een basisklasse, zodat niet elke handler het zelf hoeft te doen.
3. **Timeouts per soort** (lost punt 3 op): bijvoorbeeld lezen 30–60 s en zwaar werk (export, beelden) 120 s, in plaats van 10–15 s per command. Houd de client-timeout (nu 120 s) iets ruimer dan de langste command-timeout.
