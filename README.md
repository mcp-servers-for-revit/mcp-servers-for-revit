[![Cover Image](./assets/cover.png?v=2)](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit)

# mcp-servers-for-revit

**Connect AI assistants to Autodesk Revit via the Model Context Protocol.**

**Collega assistenti AI ad Autodesk Revit tramite il Model Context Protocol.**

---

mcp-servers-for-revit enables AI clients like Claude, Cline, and other MCP-compatible tools to read, create, modify, and delete elements in Revit projects in real time. It exposes 80+ tools covering project info, model analysis, element creation, batch operations, data export, and more.

mcp-servers-for-revit permette a client AI come Claude, Cline e altri strumenti compatibili MCP di leggere, creare, modificare e cancellare elementi nei progetti Revit in tempo reale. Espone oltre 80 tool che coprono informazioni di progetto, analisi del modello, creazione di elementi, operazioni batch, esportazione dati e altro.

> [!NOTE]
> Originally forked from [revit-mcp](https://github.com/romanzarkhin/revit-mcp) by Roman Zarkhin. This version adds 80+ tools (up from 15), multi-version Revit support (2023-2026), language-independent operation, and an embedded Claude chat panel.
>
> Originariamente fork di [revit-mcp](https://github.com/romanzarkhin/revit-mcp) di Roman Zarkhin. Questa versione aggiunge oltre 80 tool (dai 15 originali), supporto multi-versione Revit (2023-2026), funzionamento indipendente dalla lingua e un pannello chat Claude integrato.

## Key Features / Funzionalita principali

- **80+ MCP tools** — project info, model health, clash detection, element CRUD, batch operations, data export (PDF/DWG/IFC/CSV)
- **Revit 2023, 2024, 2025, 2026** — fully tested on all four versions
- **Language-independent** — works with any Revit UI language (English, Italian, French, German, etc.) using BuiltInCategory resolution
- **Built-in Claude chat panel** — dockable panel inside Revit with direct AI access (Anthropic API, extended thinking enabled)
- **Real-time execution** — AI requests are executed immediately on the active model via TCP/JSON-RPC 2.0
- **Extensible command set** — add new commands without modifying the plugin core

---

- **Oltre 80 tool MCP** — info progetto, salute del modello, rilevamento interferenze, CRUD elementi, operazioni batch, export dati (PDF/DWG/IFC/CSV)
- **Revit 2023, 2024, 2025, 2026** — completamente testato su tutte e quattro le versioni
- **Indipendente dalla lingua** — funziona con qualsiasi lingua dell'interfaccia Revit (inglese, italiano, francese, tedesco, ecc.) usando la risoluzione BuiltInCategory
- **Pannello chat Claude integrato** — pannello agganciabile all'interno di Revit con accesso diretto all'AI (API Anthropic, extended thinking abilitato)
- **Esecuzione in tempo reale** — le richieste AI vengono eseguite immediatamente sul modello attivo via TCP/JSON-RPC 2.0
- **Command set estensibile** — aggiungi nuovi comandi senza modificare il core del plugin

## Architecture / Architettura

```mermaid
flowchart LR
    Client["MCP Client<br/>(Claude, Cline, etc.)"]
    Server["MCP Server<br/><code>server/</code>"]
    Plugin["Revit Plugin<br/><code>plugin/</code>"]
    CommandSet["Command Set<br/><code>commandset/</code>"]
    Revit["Revit API"]

    Client <-->|stdio| Server
    Server <-->|TCP :8080| Plugin
    Plugin -->|loads| CommandSet
    CommandSet -->|executes| Revit
```

| Component | Language | Role |
|-----------|----------|------|
| **MCP Server** (`server/`) | TypeScript | Translates AI tool calls into JSON-RPC messages over TCP |
| **Revit Plugin** (`plugin/`) | C# | Runs inside Revit, listens on `localhost:8080`, dispatches commands |
| **Command Set** (`commandset/`) | C# | Implements Revit API operations, returns structured results |

## Requirements / Requisiti

### To use / Per l'utilizzo

| Requirement | Details |
|-------------|---------|
| **Node.js** | 18+ (for the MCP server) |
| **Autodesk Revit** | 2023, 2024, 2025, or 2026 |
| **OS** | Windows 10/11 (Revit is Windows-only) |
| **Anthropic API key** (optional) | Required only for the built-in chat panel. Set via `%USERPROFILE%\.claude\api_key.txt` or env `ANTHROPIC_API_KEY` |

### To build from source / Per compilare da sorgente

| Requirement | Details |
|-------------|---------|
| **Visual Studio 2022** | With .NET desktop development workload |
| **.NET Framework 4.8 SDK** | For Revit 2023-2024 builds |
| **.NET 8 SDK** | For Revit 2025-2026 builds |
| **Node.js 18+** | For the MCP server |
| **Revit API assemblies** | Installed with Revit (referenced automatically via NuGet) |

## Quick Start / Avvio rapido

### 1. Install the Revit plugin / Installa il plugin Revit

Download the ZIP for your Revit version from the [Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) page and extract to:

Scarica lo ZIP per la tua versione di Revit dalla pagina [Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) e estrai in:

```
%AppData%\Autodesk\Revit\Addins\<your Revit version>\
```

After extraction / Dopo l'estrazione:

```
Addins/2025/
├── mcp-servers-for-revit.addin
└── revit_mcp_plugin/
    ├── RevitMCPPlugin.dll
    ├── tool_schemas.json
    └── Commands/
        └── RevitMCPCommandSet/
            ├── command.json
            └── 2025/
                ├── RevitMCPCommandSet.dll
                └── ...
```

### 2. Configure the MCP server / Configura il server MCP

**Claude Code**

```bash
claude mcp add revit-mcp -- cmd /c npx -y mcp-server-for-revit
```

**Claude Desktop**

Claude Desktop → Settings → Developer → Edit Config → `claude_desktop_config.json`:

```json
{
    "mcpServers": {
        "revit-mcp": {
            "command": "cmd",
            "args": ["/c", "npx", "-y", "mcp-server-for-revit"]
        }
    }
}
```

### 3. Start Revit / Avvia Revit

The plugin loads automatically. Click **"Revit MCP Switch"** in the ribbon to start the TCP server. When the status indicator turns green, the connection is active.

Il plugin si carica automaticamente. Clicca **"Revit MCP Switch"** nel ribbon per avviare il server TCP. Quando l'indicatore di stato diventa verde, la connessione e attiva.

![Claude Desktop connection](./assets/claude.png)

## Supported Revit Versions / Versioni Revit supportate

| Version | .NET Target | Status | Notes |
|---------|-------------|--------|-------|
| **Revit 2023** | .NET Framework 4.8 | Fully tested | Italian localization verified |
| **Revit 2024** | .NET Framework 4.8 | Built & compatible | Same codebase as R23 |
| **Revit 2025** | .NET 8 | Fully tested | Structural model (Snowdon Towers) |
| **Revit 2026** | .NET 8 | Fully tested | Primary development target |

All tools work across all versions. The command set uses compile-time constants (`REVIT2023`, `REVIT2024`, etc.) to handle API differences between versions (e.g., `ElementId` is `long` in R24+, `int` in R23).

Tutti i tool funzionano su tutte le versioni. Il command set usa costanti di compilazione (`REVIT2023`, `REVIT2024`, ecc.) per gestire le differenze API tra le versioni (es. `ElementId` e `long` in R24+, `int` in R23).

## Supported Tools (80+) / Tool supportati (80+)

### Project & Model Info / Info progetto e modello

| Tool | Description / Descrizione |
| ---- | ----------- |
| `get_project_info` | Project metadata, levels, phases, links, worksets |
| `get_current_view_info` | Active view type, name, scale, detail level |
| `get_current_view_elements` | Elements from the active view filtered by category |
| `get_selected_elements` | Currently selected elements |
| `get_available_family_types` | Family types filtered by category |
| `get_element_parameters` | All instance and type parameters for elements |
| `get_warnings` | Model warnings and errors |
| `get_phases` | Phases and phase filters |
| `get_worksets` | Workset info and status |
| `get_shared_parameters` | Project parameters bound to categories |
| `manage_links` | List, reload, or unload linked Revit models |

### Model Analysis & Auditing / Analisi e audit del modello

| Tool | Description / Descrizione |
| ---- | ----------- |
| `ai_element_filter` | Intelligent element query by category, type, visibility, bounding box |
| `analyze_model_statistics` | Element counts by category, type, family, and level |
| `check_model_health` | Health score (0-100), grade (A-F), actionable recommendations |
| `clash_detection` | Geometric intersection detection between element sets |
| `measure_between_elements` | Distance measurement (center-to-center, closest points, bounding box) |
| `get_elements_in_spatial_volume` | Find elements within a 3D bounding region |

### Materials & Quantities / Materiali e quantita

| Tool | Description / Descrizione |
| ---- | ----------- |
| `get_materials` | List materials filtered by class or name |
| `get_material_properties` | Physical, structural, and thermal properties |
| `get_material_quantities` | Material takeoffs: area, volume, element counts |

### Element Creation / Creazione elementi

| Tool | Description / Descrizione |
| ---- | ----------- |
| `create_line_based_element` | Walls, beams, pipes (start/end points) |
| `create_point_based_element` | Doors, windows, furniture (insertion point) |
| `create_surface_based_element` | Floors, ceilings, roofs (boundary) |
| `create_floor` | Floors from boundary points or room boundaries |
| `create_room` | Rooms at specified locations |
| `create_grid` | Grid systems with automatic spacing |
| `create_level` | Levels at specified elevations |
| `create_structural_framing_system` | Beam framing systems within a boundary |
| `create_array` | Linear or radial arrays of elements |

### Element Modification / Modifica elementi

| Tool | Description / Descrizione |
| ---- | ----------- |
| `modify_element` | Move, rotate, mirror, or copy elements |
| `operate_element` | Select, hide, isolate, highlight, delete |
| `change_element_type` | Batch swap family types |
| `set_element_parameters` | Write parameter values on elements |
| `set_element_phase` | Change element phase assignment |
| `set_element_workset` | Change element workset assignment |
| `match_element_properties` | Copy parameters from source to target elements |
| `copy_elements` | Copy elements between views |
| `delete_element` | Delete elements by ID |
| `load_family` | Load a family file (.rfa) into the project |

### Views & Sheets / Viste e tavole

| Tool | Description / Descrizione |
| ---- | ----------- |
| `create_view` | Create floor plans, sections, elevations, 3D views |
| `duplicate_view` | Duplicate views (independent, dependent, with detailing) |
| `create_view_filter` | Create, apply, or list view filters |
| `apply_view_template` | List, apply, or remove view templates |
| `override_graphics` | Per-element graphic overrides (color, transparency, lineweight) |
| `color_elements` | Color elements by parameter value |
| `create_sheet` | Create sheets with title blocks |
| `batch_create_sheets` | Create multiple sheets at once |
| `place_viewport` | Place views onto sheets |
| `create_schedule` | Create schedule views with fields, filters, sorting |
| `create_revision` | List, create, or add revisions to sheets |

### Annotation / Annotazioni

| Tool | Description / Descrizione |
| ---- | ----------- |
| `create_dimensions` | Dimension annotations between elements or points |
| `create_text_note` | Text note annotations in views |
| `create_filled_region` | Hatched/filled regions in views |
| `tag_all_walls` | Auto-tag all walls in the active view |
| `tag_all_rooms` | Auto-tag all rooms in the active view |

### Data Export / Esportazione dati

| Tool | Description / Descrizione |
| ---- | ----------- |
| `export_room_data` | All room data (area, volume, department, finishes) |
| `export_elements_data` | Bulk element data export with filtering (JSON/CSV) |
| `export_schedule` | Export schedules to CSV/TXT files |
| `get_schedule_data` | Read schedule contents or list all schedules |
| `batch_export` | Export sheets/views to PDF, DWG, or IFC |

### Batch Operations & Cleanup / Operazioni batch e pulizia

| Tool | Description / Descrizione |
| ---- | ----------- |
| `batch_rename` | Batch rename views, sheets, levels, grids, rooms |
| `renumber_elements` | Sequential renumbering of rooms, doors, windows |
| `sync_csv_parameters` | Write parameter values back from CSV/AI data |
| `purge_unused` | Identify and remove unused families, types, materials |
| `cad_link_cleanup` | Audit and clean up CAD imports and links |
| `add_shared_parameter` | Add shared parameters to categories |

### Advanced / Avanzato

| Tool | Description / Descrizione |
| ---- | ----------- |
| `send_code_to_revit` | Execute arbitrary C# code inside Revit |
| `store_project_data` | Store project metadata in local database |
| `store_room_data` | Store room metadata in local database |
| `query_stored_data` | Query stored project and room data |
| `say_hello` | Display a greeting dialog (connection test) |

## Built-in Chat Panel / Pannello chat integrato

The Revit plugin includes a dockable chat panel that connects directly to the Anthropic API. It provides a Claude chat interface inside Revit where the AI can autonomously execute tools on the active model.

Il plugin Revit include un pannello chat agganciabile che si connette direttamente alle API Anthropic. Fornisce un'interfaccia di chat Claude all'interno di Revit dove l'AI puo eseguire autonomamente i tool sul modello attivo.

- **Model**: Claude Sonnet 4.6 with extended thinking (10K token budget)
- **System prompt**: Autonomous mode — Claude executes actions directly without unnecessary confirmations
- **Features**: Tool execution feedback, thinking summary, round progress, stop/cancel, chat export (TXT/MD/JSON)

## Known Limitations / Limitazioni note

| Limitation | Details |
|------------|---------|
| **Windows only** | Revit runs only on Windows; macOS/Linux are not supported |
| **Single model** | The plugin operates on the active document only; background documents are not accessible |
| **TCP port 8080** | The plugin listens on `localhost:8080`; if the port is occupied, the server won't start |
| **No undo integration** | Operations executed by AI tools create standard Revit transactions but are not grouped into a single undo step |
| **`send_code_to_revit`** | May fail if third-party addins cause assembly conflicts (e.g., duplicate DLL references) |
| **Parameter names are localized** | Revit parameter names depend on UI language. Use BuiltInCategory names (e.g., `OST_Walls`) for categories. The command set resolves categories automatically, but parameter names must match the Revit language |
| **No streaming** | Tool results are returned as a single response; large results (e.g., exporting thousands of elements) may take time |
| **Anthropic API key** | The built-in chat panel requires an Anthropic API key. External MCP clients (Claude Code, Claude Desktop) use their own authentication |

| Limitazione | Dettagli |
|-------------|----------|
| **Solo Windows** | Revit funziona solo su Windows; macOS/Linux non sono supportati |
| **Singolo modello** | Il plugin opera solo sul documento attivo; i documenti in background non sono accessibili |
| **Porta TCP 8080** | Il plugin ascolta su `localhost:8080`; se la porta e occupata, il server non si avvia |
| **Nessuna integrazione undo** | Le operazioni eseguite dai tool AI creano transazioni Revit standard ma non sono raggruppate in un singolo passo di annullamento |
| **`send_code_to_revit`** | Puo fallire se addin di terze parti causano conflitti di assembly (es. riferimenti DLL duplicati) |
| **I nomi dei parametri sono localizzati** | I nomi dei parametri Revit dipendono dalla lingua dell'interfaccia. Usa i nomi BuiltInCategory (es. `OST_Walls`) per le categorie. Il command set risolve le categorie automaticamente, ma i nomi dei parametri devono corrispondere alla lingua di Revit |
| **Nessuno streaming** | I risultati dei tool vengono restituiti come risposta singola; risultati grandi (es. esportazione di migliaia di elementi) possono richiedere tempo |
| **Chiave API Anthropic** | Il pannello chat integrato richiede una chiave API Anthropic. I client MCP esterni (Claude Code, Claude Desktop) usano la propria autenticazione |

## Development / Sviluppo

### MCP Server

```bash
cd server
npm install
npm run build
```

The server compiles TypeScript to `server/build/`. During development you can run it directly with `npx tsx server/src/index.ts`.

### Revit Plugin + Command Set

Open `mcp-servers-for-revit.sln` in Visual Studio. The solution contains both the plugin and command set projects. Build configurations target Revit 2023-2026:

| Configuration | Target | .NET |
|---------------|--------|------|
| `Debug R23` / `Release R23` | Revit 2023 | .NET Framework 4.8 |
| `Debug R24` / `Release R24` | Revit 2024 | .NET Framework 4.8 |
| `Debug R25` / `Release R25` | Revit 2025 | .NET 8 |
| `Debug R26` / `Release R26` | Revit 2026 | .NET 8 |

Building the solution automatically assembles the complete deployable layout in `plugin/bin/AddIn <year> <config>/` — the command set is copied into the plugin's `Commands/` folder as part of the build.

## Testing

The test project uses [Nice3point.TUnit.Revit](https://github.com/Nice3point/RevitUnit) to run integration tests against a live Revit instance.

```bash
# Revit 2026
dotnet test -c Debug.R26 -r win-x64 tests/commandset

# Revit 2025
dotnet test -c Debug.R25 -r win-x64 tests/commandset
```

> **Note:** The `-r win-x64` flag is required on ARM64 machines because the Revit API assemblies are x64-only.

## Project Structure / Struttura del progetto

```
mcp-servers-for-revit/
├── mcp-servers-for-revit.sln    # Combined solution (plugin + commandset + tests)
├── command.json                 # Command set manifest
├── server/                      # MCP server (TypeScript) - tools exposed to AI clients
│   └── src/tools/               # One .ts file per tool (80+ tools)
├── plugin/                      # Revit add-in (C#) - TCP bridge + chat panel
│   └── UI/                      # Dockable chat panel (XAML + code-behind)
├── commandset/                  # Command implementations (C#) - Revit API operations
│   ├── Commands/                # Command registration
│   ├── Services/                # Event handlers (one per tool)
│   └── Utils/                   # CategoryResolver, ProjectUtils, etc.
├── tests/                       # Integration tests (TUnit + live Revit)
├── assets/                      # Images for documentation
├── .github/                     # CI/CD workflows
├── LICENSE
└── README.md
```

## Releasing

A single `v*` tag drives the entire release. The [release workflow](.github/workflows/release.yml) automatically:

- Builds the Revit plugin + command set for Revit 2023-2026
- Creates a GitHub release with `mcp-servers-for-revit-vX.Y.Z-Revit<year>.zip` assets
- Publishes the MCP server to npm as [`mcp-server-for-revit`](https://www.npmjs.com/package/mcp-server-for-revit)

```powershell
# Bump version, commit, and tag
./scripts/release.ps1 -Version X.Y.Z

# Push to trigger CI
git push origin main --tags
```

## Acknowledgements / Ringraziamenti

This project started as a fork of [revit-mcp](https://github.com/romanzarkhin/revit-mcp) by Roman Zarkhin ([mcpservers.org listing](https://mcpservers.org/servers/romanzarkhin/revit-mcp)), which provided the original 15-tool MCP server for Revit. It was later merged with the expanded work by the [mcp-servers-for-revit](https://github.com/mcp-servers-for-revit) team:

Questo progetto e nato come fork di [revit-mcp](https://github.com/romanzarkhin/revit-mcp) di Roman Zarkhin ([listing mcpservers.org](https://mcpservers.org/servers/romanzarkhin/revit-mcp)), che ha fornito il server MCP originale con 15 tool per Revit. E stato successivamente unito al lavoro ampliato dal team [mcp-servers-for-revit](https://github.com/mcp-servers-for-revit):

- [revit-mcp](https://github.com/mcp-servers-for-revit/revit-mcp) - Original MCP server
- [revit-mcp-plugin](https://github.com/mcp-servers-for-revit/revit-mcp-plugin) - Revit plugin
- [revit-mcp-commandset](https://github.com/mcp-servers-for-revit/revit-mcp-commandset) - Command set

## License / Licenza

This project is released under the **MIT License** — see [LICENSE](LICENSE) for the full text.

Questo progetto e rilasciato sotto la **Licenza MIT** — vedi [LICENSE](LICENSE) per il testo completo.

### What MIT allows / Cosa permette la MIT

| | Allowed / Permesso | Condition / Condizione |
|---|---|---|
| Commercial use / Uso commerciale | Yes / Si | Include copyright notice / Includi la nota di copyright |
| Modification / Modifica | Yes / Si | Include copyright notice / Includi la nota di copyright |
| Distribution / Distribuzione | Yes / Si | Include copyright notice / Includi la nota di copyright |
| Private use / Uso privato | Yes / Si | — |
| Sublicensing / Sublicenza | Yes / Si | Include copyright notice / Includi la nota di copyright |

### Disclaimer of Liability / Esclusione di responsabilita

> **THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.** The authors and contributors are not liable for any damages, data loss, model corruption, or unintended modifications to Revit projects arising from the use of this software. Use at your own risk.

> **IL SOFTWARE E FORNITO "COSI COM'E", SENZA GARANZIA DI ALCUN TIPO.** Gli autori e i contributori non sono responsabili per eventuali danni, perdita di dati, corruzione del modello o modifiche involontarie ai progetti Revit derivanti dall'uso di questo software. L'uso e a proprio rischio e pericolo.

**Important / Importante:**

- This software executes commands on live Revit models. Always work on copies or ensure you have backups before using AI-driven automation.
- The AI (Claude or other MCP clients) may misinterpret instructions and execute unintended operations. Review AI-generated actions before confirming batch operations on production models.
- This project is not affiliated with, endorsed by, or supported by Autodesk, Inc. "Autodesk" and "Revit" are registered trademarks of Autodesk, Inc.

---

- Questo software esegue comandi su modelli Revit attivi. Lavora sempre su copie o assicurati di avere backup prima di usare l'automazione guidata dall'AI.
- L'AI (Claude o altri client MCP) potrebbe interpretare erroneamente le istruzioni ed eseguire operazioni non previste. Verifica le azioni generate dall'AI prima di confermare operazioni batch su modelli di produzione.
- Questo progetto non e affiliato, approvato o supportato da Autodesk, Inc. "Autodesk" e "Revit" sono marchi registrati di Autodesk, Inc.
