# Revit MCP Server - Complete Reference

> **62 MCP tools** | **140 unit tests** | **Revit 2023–2026** | Coordinates in **millimeters (mm)**

---

## Table of Contents

- [Setup](#setup)
- [Element Creation](#element-creation)
- [Element Modification](#element-modification)
- [Element Query & Data Extraction](#element-query--data-extraction)
- [View & Sheet Management](#view--sheet-management)
- [Parameter Management](#parameter-management)
- [Project Management](#project-management)
- [Documentation & Annotation](#documentation--annotation)
- [Model Audit & Cleanup](#model-audit--cleanup)
- [Advanced Automation (New)](#advanced-automation-new)
- [Chat Panel](#chat-panel)
- [Troubleshooting](#troubleshooting)

---

## Setup

### Requirements
- Autodesk Revit 2023, 2024, 2025, or 2026
- Node.js 18+
- .NET 8.0 SDK (for Revit 2025/2026) or .NET Framework 4.8 (for Revit 2023/2024)

### Quick Start
1. Build the plugin: `dotnet build plugin/RevitMCPPlugin.csproj -c "Debug R25"`
2. Build the MCP server: `cd server && npm run build`
3. Open Revit and click **"Revit MCP Switch"** in the ribbon
4. Configure `.mcp.json` or Claude Desktop to connect to the MCP server

### Chat Panel (inside Revit)
The plugin includes a dockable chat panel accessible via **"MCP Panel"** in the ribbon.
- Calls Anthropic API with 16 Revit tool definitions
- Executes commands on the model via JSON-RPC (localhost:8080)
- API key: set `ANTHROPIC_API_KEY` env var or create `%USERPROFILE%\.claude\api_key.txt`

---

## Element Creation

### `create_level`
Create levels at specified elevations with automatic floor plan generation.
```json
{
  "data": [
    { "name": "Ground Floor", "elevation": 0, "createFloorPlan": true },
    { "name": "First Floor", "elevation": 3500, "createFloorPlan": true, "createCeilingPlan": true },
    { "name": "Roof", "elevation": 7000, "isBuildingStory": false }
  ]
}
```

### `create_grid`
Create a grid system with X/Y axes, spacing, and naming. Supports prefixes (e.g., "MCP-A").
```json
{
  "xCount": 5, "xSpacing": 6000, "xStartLabel": "A", "xNamingStyle": "alphabetic",
  "yCount": 4, "ySpacing": 5000, "yStartLabel": "1", "yNamingStyle": "numeric"
}
```

### `create_line_based_element`
Create walls, beams, pipes, or other line-based elements.
```json
{
  "data": [{
    "category": "OST_Walls",
    "locationLine": { "p0": {"x": 0, "y": 0, "z": 0}, "p1": {"x": 10000, "y": 0, "z": 0} },
    "thickness": 200, "height": 3000, "baseLevel": 0, "baseOffset": 0
  }]
}
```

### `create_point_based_element`
Create doors, windows, furniture, or other point-based elements.
```json
{
  "data": [{
    "name": "door", "typeId": 12345,
    "locationPoint": {"x": 5000, "y": 0, "z": 0},
    "width": 900, "height": 2100, "baseLevel": 0, "baseOffset": 0
  }]
}
```

### `create_surface_based_element`
Create surface-based elements like floors from boundary curves.

### `create_floor`
Create floor elements from boundary points or room boundaries.
```json
{
  "boundaryPoints": [
    {"x": 0, "y": 0}, {"x": 10000, "y": 0},
    {"x": 10000, "y": 8000}, {"x": 0, "y": 8000}
  ],
  "levelElevation": 0, "isStructural": true
}
```

### `create_room`
Create rooms at specified locations within enclosed boundaries.
```json
{
  "data": [
    { "name": "MCP Office", "number": "101", "location": {"x": 5000, "y": 5000, "z": 0}, "department": "Engineering" },
    { "name": "MCP Meeting Room", "number": "102", "location": {"x": 15000, "y": 5000, "z": 0} }
  ]
}
```

### `create_structural_framing_system`
Create structural beam framing systems with configurable spacing.

### `create_array`
Create linear or radial arrays of elements.
```json
// Linear array
{ "elementIds": [12345], "arrayType": "linear", "count": 4, "spacingX": 3000, "spacingY": 0, "spacingZ": 0 }

// Radial array
{ "elementIds": [12345], "arrayType": "radial", "count": 6, "centerX": 0, "centerY": 0, "totalAngle": 360 }
```

### `create_filled_region` *(new)*
Create 2D filled regions in views.
```json
{
  "boundaryPoints": [
    {"x": 0, "y": 0}, {"x": 5000, "y": 0},
    {"x": 5000, "y": 3000}, {"x": 0, "y": 3000}
  ],
  "filledRegionTypeName": "Solid Black"
}
```

---

## Element Modification

### `modify_element`
Move, rotate, mirror, or copy elements.
```json
// Move
{ "elementIds": [12345, 67890], "action": "move", "translation": {"x": 5000, "y": 0, "z": 0} }

// Rotate 45 degrees
{ "elementIds": [12345], "action": "rotate", "rotationCenter": {"x": 0, "y": 0, "z": 0}, "rotationAngle": 45 }

// Mirror
{ "elementIds": [12345], "action": "mirror", "mirrorPlaneOrigin": {"x": 0, "y": 0, "z": 0}, "mirrorPlaneNormal": {"x": 1, "y": 0, "z": 0} }

// Copy with offset
{ "elementIds": [12345], "action": "copy", "copyOffset": {"x": 10000, "y": 0, "z": 0} }
```

### `delete_element`
Delete elements by ID.
```json
{ "elementIds": [12345, 67890] }
```

### `copy_elements`
Copy elements between views.
```json
{
  "elementIds": [12345], "sourceViewId": 100, "targetViewId": 200,
  "offsetX": 5000, "offsetY": 0, "offsetZ": 0
}
```

### `operate_element`
Select, color, hide, isolate, or unhide elements in the active view.

### `change_element_type` *(new)*
Batch swap family types on multiple elements.
```json
{
  "elementIds": [12345, 67890],
  "targetTypeName": "W14x109",
  "targetFamilyName": "W-Wide Flange"
}
```

### `match_element_properties` *(new)*
Copy parameter values from source to target elements.
```json
{
  "sourceElementId": 12345,
  "targetElementIds": [67890, 11111],
  "parameterNames": ["Comments", "Mark"],
  "includeTypeParameters": false
}
```

### `batch_rename`
Batch rename views, sheets, levels, grids, or rooms.
```json
{ "targetCategory": "Views", "findText": "Level", "replaceText": "Floor", "prefix": "ARCH-", "dryRun": false }
```

### `renumber_elements` *(new)*
Sequentially renumber rooms, doors, windows by spatial order.
```json
{
  "targetCategory": "Rooms",
  "startNumber": 100, "increment": 1,
  "prefix": "R-", "sortBy": "location", "dryRun": false
}
```

---

## Element Query & Data Extraction

### `ai_element_filter`
Query elements by category, type, visibility, or spatial location.
```json
{
  "data": {
    "filterCategory": "OST_StructuralColumns",
    "includeInstances": true, "includeTypes": false, "maxElements": 50
  }
}
```

### `get_selected_elements`
Get currently selected elements in Revit.

### `get_current_view_elements`
Get all elements visible in the active view.

### `get_current_view_info`
Get information about the active view.

### `get_available_family_types`
List available family types by category.

### `get_element_parameters`
Get all instance and type parameters of elements.
```json
{ "elementIds": [12345, 67890], "includeTypeParameters": true }
```

### `get_schedule_data`
Read schedule data or list all schedules.
```json
{ "action": "list" }
// or
{ "action": "read", "scheduleId": 12345 }
```

### `export_room_data`
Extract rooms with area, volume, perimeter, and parameters.

### `get_material_quantities`
Calculate material takeoffs with area and volume per material.

### `analyze_model_statistics`
Get element counts by category, type, family, and level.
```json
{ "includeDetailedTypes": true }
```

### `get_materials`
List all materials with color, transparency, and assets.

### `get_material_properties`
Get detailed structural, thermal, and appearance properties.
```json
{ "materialId": 12345 }
```

---

## View & Sheet Management

### `create_view`
Create FloorPlan, CeilingPlan, Section, Elevation, or 3D views.
```json
// Floor Plan
{ "viewType": "FloorPlan", "name": "MCP Ground Floor", "levelElevation": 0, "scale": 100 }

// Section
{ "viewType": "Section", "name": "MCP Section A", "direction": {"x": 0, "y": 1, "z": 0}, "scale": 50 }

// 3D View
{ "viewType": "3D", "name": "MCP Overview", "detailLevel": "Fine" }
```

### `create_sheet`
Create sheets with title blocks.
```json
{ "sheetNumber": "MCP-A101", "sheetName": "Ground Floor Plan" }
```

### `place_viewport`
Place views on sheets as viewports.
```json
{ "sheetId": 12345, "viewId": 67890, "positionX": 420, "positionY": 297 }
```

### `create_view_filter`
Create, apply, or list view filters.

### `color_elements`
Color elements by parameter value (color splash).
```json
{ "categoryName": "Structural Columns", "parameterName": "Family and Type", "useGradient": true }
```

### `duplicate_view` *(new)*
Duplicate views with options.
```json
{
  "viewIds": [12345, 67890],
  "duplicateOption": "withDetailing",
  "newNameSuffix": " - Copy"
}
```

### `apply_view_template` *(new)*
List, apply, or remove view templates.
```json
// List all templates
{ "action": "list" }

// Apply template to views
{ "action": "apply", "viewIds": [12345, 67890], "templateName": "Structural Plan" }

// Remove template
{ "action": "remove", "viewIds": [12345] }
```

### `override_graphics` *(new)*
Set per-element graphic overrides in a view.
```json
{
  "elementIds": [12345, 67890],
  "projectionLineColor": {"r": 255, "g": 0, "b": 0},
  "surfaceForegroundColor": {"r": 255, "g": 200, "b": 200},
  "transparency": 50, "action": "set"
}
```

---

## Parameter Management

### `set_element_parameters`
Set parameter values on elements.
```json
{
  "requests": [
    { "elementId": 12345, "parameterName": "Comments", "value": "Set by MCP" },
    { "elementId": 12345, "parameterName": "Mark", "value": "M-001" }
  ]
}
```

### `get_shared_parameters`
List all shared and project parameters.

### `add_shared_parameter`
Add a shared parameter to categories.
```json
{
  "parameterName": "MCP_Status",
  "groupName": "Identity Data",
  "categories": ["Walls", "Floors", "Doors"],
  "isInstance": true
}
```

---

## Project Management

### `get_project_info`
Get project metadata, phases, worksets, links, and levels.
```json
{ "includePhases": true, "includeWorksets": true, "includeLinks": true, "includeLevels": true }
```

### `get_phases`
Get all phases and phase filters.

### `set_element_phase`
Set created/demolished phase on elements.

### `get_worksets`
List worksets with properties.

### `set_element_workset`
Move elements to a different workset.

### `manage_links`
List, reload, or unload Revit links.
```json
{ "action": "list" }
```

### `load_family`
Load families, list loaded families, or duplicate types.
```json
// Load a family
{ "action": "load", "familyPath": "C:\\Families\\MyDoor.rfa" }

// List loaded families
{ "action": "list", "categoryFilter": "Doors" }

// Duplicate a type
{ "action": "duplicate_type", "sourceTypeId": 12345, "newTypeName": "Custom Door 1000" }
```

### `create_revision` *(new)*
Manage project revisions.
```json
// List revisions
{ "action": "list" }

// Create revision
{ "action": "create", "date": "2026-03-28", "description": "Coordination Update", "issuedBy": "BIM Manager" }

// Add to sheets
{ "action": "add_to_sheets", "sheetIds": [12345, 67890] }
```

---

## Documentation & Annotation

### `create_text_note`
Create text note annotations in views.
```json
{
  "textNotes": [{
    "text": "MCP - Structural Note",
    "position": {"x": 5000, "y": 5000, "z": 0},
    "horizontalAlignment": "Center"
  }]
}
```

### `create_dimensions`
Create dimension annotations between elements or points.
```json
{
  "dimensions": [{
    "startPoint": {"x": 0, "y": 0, "z": 0},
    "endPoint": {"x": 10000, "y": 0, "z": 0},
    "elementIds": [12345, 67890]
  }]
}
```

### `create_schedule`
Create schedules with fields, filters, and sorting.
```json
{
  "categoryName": "OST_StructuralColumns",
  "name": "MCP Column Schedule",
  "fields": [
    {"parameterName": "Family and Type"},
    {"parameterName": "Base Level"},
    {"parameterName": "Length"}
  ],
  "sortFields": [{"fieldName": "Base Level", "sortOrder": "Ascending"}]
}
```

### `export_schedule`
Export schedule data to CSV/text file.

### `tag_rooms`
Tag all rooms in the current view.

### `tag_walls`
Tag all walls in the current view.

---

## Model Audit & Cleanup

### `get_warnings`
Get model warnings for quality auditing.
```json
{ "severityFilter": "Warning", "maxWarnings": 100 }
```

### `purge_unused`
Identify and remove unused families, types, and materials.
```json
{ "dryRun": true }
```

### `clash_detection` *(new)*
Detect geometric clashes between element categories.
```json
{
  "categoryA": "Ducts",
  "categoryB": "StructuralFraming",
  "maxResults": 50
}
```

### `cad_link_cleanup` *(new)*
Audit and clean up CAD imports/links.
```json
// List all CAD elements
{ "action": "list" }

// Delete all imports
{ "action": "delete", "deleteImports": true, "deleteLinks": false }
```

---

## Advanced Automation (New)

These 10 tools were added based on research of the most commonly automated BIM tasks:

| Tool | Description |
|------|-------------|
| `renumber_elements` | Sequential renumbering by spatial order |
| `change_element_type` | Batch family type swap |
| `apply_view_template` | List/apply/remove view templates |
| `duplicate_view` | Duplicate views (independent/dependent/with detailing) |
| `override_graphics` | Per-element graphic overrides |
| `create_filled_region` | 2D filled regions with boundaries |
| `create_revision` | Revision management and sheet assignment |
| `match_element_properties` | Copy parameters from source to targets |
| `clash_detection` | Geometric intersection detection |
| `cad_link_cleanup` | Audit and delete CAD imports/links |

---

## Chat Panel

The dockable panel inside Revit provides a Claude-style chat interface:

- **UI**: Light theme matching claude.ai (white background, pill-shaped input, Claude orange branding)
- **API**: Calls Anthropic API (claude-sonnet-4-20250514) with 16 MCP tool definitions
- **Execution**: When Claude decides to use a tool, it sends JSON-RPC to localhost:8080
- **Multi-turn**: Supports up to 5 tool execution rounds per message
- **Setup**: API key via `ANTHROPIC_API_KEY` env var or `~/.claude/api_key.txt`

### Example prompts for the chat panel:
- "Che progetto ho aperto?"
- "Fai un audit del modello"
- "Crea un livello MCP a 15000mm"
- "Mostra i warning"
- "Quanti elementi strutturali ci sono?"

---

## Troubleshooting

### Duplicated AddInId error
Multiple `.addin` files with the same GUID. Check:
```
%AppData%\Autodesk\Revit\Addins\2025\
```
Remove any duplicate `revit-mcp.addin` — keep only `mcp-servers-for-revit.addin`.

### Slow Revit startup
Common causes:
- **Enscape** (orphan `.addin` files): Delete `0_Enscape.addin` from `C:\ProgramData\Autodesk\Revit\Addins\`
- **pyRevit** (old versions): Delete `pyRevit.addin` from ProgramData addins
- **Speckle duplicate**: Remove `SpeckleRevit2` folder if using Speckle v3
- **Unnecessary DLLs**: Remove WebView2/Windows SDK DLLs from plugin folder

### IFC Export error (Autodesk Issues Addin)
`ReflectionTypeLoadException: Could not load Revit.IFC.Export`
- Caused by IFC Exporter version mismatch
- Fix: Update Revit to latest patch (2025.3+) or update IFC Exporter from Autodesk Desktop App

### MCP connection failed
- Ensure "Revit MCP Switch" is clicked (server must be running)
- Default port: 8080
- Check Windows Firewall is not blocking localhost connections

### Chat panel shows "API key not configured"
Create the key file:
```
echo sk-ant-YOUR-KEY > %USERPROFILE%\.claude\api_key.txt
```

---

## Architecture

```
mcp-servers-for-revit/
├── plugin/                    # Revit Plugin (C#/WPF)
│   ├── Core/                  # Application, SocketService, CommandManager
│   └── UI/                    # Dockable Panel, Chat UI
├── commandset/                # MCP Command Set (C#)
│   ├── Commands/              # 62 command classes
│   ├── Services/              # 62 event handlers
│   └── Models/                # Data models
├── server/                    # MCP Server (TypeScript)
│   └── src/tools/             # 62 tool definitions
├── tests/                     # Unit tests (140 tests, TUnit)
├── command.json               # Command registry
└── COMMANDS.md                # This file
```

---

## Notes

- All spatial coordinates are in **millimeters (mm)**
- Revit uses **feet internally** — conversion is handled automatically (1 ft = 304.8 mm)
- Tools marked *(new)* were added in the latest update
- Use `dryRun: true` on destructive operations to preview changes
- Element IDs can be found using `ai_element_filter`, `get_current_view_elements`, or `get_selected_elements`
- The plugin supports **Revit 2023–2026** (.NET Framework 4.8 and .NET 8.0)
