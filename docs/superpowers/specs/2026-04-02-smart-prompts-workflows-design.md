# Smart Prompts, Workflows & Next-Step Suggestions — Design Spec

**Goal:** Make the Revit MCP plugin more accessible for beginners and faster for experts by adding contextual prompt suggestions in the Revit chat panel, pre-packaged workflow tools, and intelligent next-step suggestions in tool responses.

**Scope:** 3 features — contextual chips UI, 5 workflow MCP tools, next-step suggestions in ~8 existing tool responses.

---

## Feature 1: Contextual Prompt Chips (Revit Chat Panel)

### Location
`plugin/UI/MCPDockablePanel.xaml` + `.xaml.cs` — a horizontal scrollable bar of clickable chips above the text input area.

### Behavior
- On panel open and every 3-5 seconds (via existing or new DispatcherTimer), the plugin queries Revit for the current context:
  - Active view type (FloorPlan, ThreeD, Sheet, Schedule, Section, Elevation, etc.)
  - Whether elements are currently selected (count > 0)
  - Whether the active view contains rooms (FilteredElementCollector + BuiltInCategory.OST_Rooms)
  - Whether a project is open at all
- Based on context, display 4-6 prompt chips from a hardcoded mapping table (no external config)
- Clicking a chip inserts the prompt text into the chat input and sends it automatically

### Context-to-Chips Mapping

| Context Condition | Chips |
|---|---|
| No project open | "Open a project to get started" (disabled, informational) |
| FloorPlan view + rooms exist | "Tag all rooms", "Color rooms by department", "Export room data", "Create callouts for rooms" |
| FloorPlan view + no rooms | "Check model health", "Show all warnings", "Export elements to Excel" |
| ThreeD view | "Check model health", "Detect clashes", "Section box from selection", "Audit families" |
| Sheet view | "Align viewports", "Add revision", "Export sheets to PDF", "Duplicate sheet" |
| Schedule view | "Export schedule to CSV", "Export to Excel" |
| Section/Elevation view | "Create dimensions", "Add text note", "Export to PDF" |
| Elements selected (any view) | "Show parameters", "Isolate in view", "Measure distance", "Move elements" |
| Fallback (any view, nothing special) | "Check model health", "Show model statistics", "Export to Excel", "List all warnings" |

Priority: "Elements selected" chips prepend to view-based chips (max 6 total, selection takes priority).

### Visual Design
- WrapPanel or horizontal StackPanel inside a ScrollViewer (horizontal scroll only)
- Each chip: Border with CornerRadius=12, Background=#E3F2FD (light blue), Padding=8,4
- Text: FontSize=12, Foreground=#1565C0 (blue)
- Hover: Background=#BBDEFB
- Click: sends prompt, chip bar refreshes after response
- Height: ~40px total, does not take significant space from chat area
- Visibility: collapsed when MCP is offline

### Implementation Notes
- Context detection runs on Revit UI thread (already in the panel's timer callback)
- Use `UIApplication.ActiveUIDocument.ActiveView` for view type
- Use `UIApplication.ActiveUIDocument.Selection.GetElementIds().Count` for selection
- Use `FilteredElementCollector(doc, activeView.Id).OfCategory(OST_Rooms).GetElementCount()` for room detection
- Chips are simple `Button` elements styled with a DataTemplate, bound to an `ObservableCollection<PromptChip>`
- `PromptChip` model: `string Text`, `string Prompt` (what gets sent), `string Icon` (optional emoji/unicode)

---

## Feature 2: Workflow Tools (Server MCP)

### Architecture
Each workflow is a standard MCP tool (TypeScript + C# command + event handler). The C# side orchestrates multiple existing event handlers in sequence within a single TransactionGroup. The TypeScript side registers the tool and formats the combined result.

### Workflow 1: `workflow_model_audit`

**Purpose:** Comprehensive model health check in one call.

**Sequence:**
1. `check_model_health` → health score (A-F), element counts
2. `get_warnings` → warning list (max 50)
3. `audit_families` → family health, unused detection
4. `cad_link_cleanup` (dryRun=true) → CAD imports found
5. `purge_unused` (dryRun=true) → purgeable items count

**Parameters:**
- `includeWarnings` (bool, default true) — include warnings in report
- `includeFamilies` (bool, default true) — include family audit
- `maxWarnings` (int, default 50) — limit warnings returned

**Output:** Single JSON with sections: `healthScore`, `warnings`, `families`, `cadImports`, `purgeableItems`, `summary` (human-readable text).

### Workflow 2: `workflow_room_documentation`

**Purpose:** Document all rooms in one call.

**Sequence:**
1. `export_room_data` → all room data
2. `create_views_from_rooms` → section views per room
3. `tag_all_rooms` → tags placed
4. `create_color_legend` → color scheme applied

**Parameters:**
- `levelName` (string, optional) — filter rooms by level
- `colorByParameter` (string, default "Department") — parameter for color legend
- `createSections` (bool, default true) — skip view creation if false

**Output:** `roomCount`, `viewsCreated`, `tagsPlaced`, `legendCreated`, `rooms` (data array).

### Workflow 3: `workflow_sheet_set`

**Purpose:** Create a complete sheet set with views placed and aligned.

**Sequence:**
1. `batch_create_sheets` → sheets created
2. `place_viewport` → views placed on sheets (user provides view-to-sheet mapping)
3. `align_viewports` → viewports aligned

**Parameters:**
- `sheets` (array of {number, name, titleBlockId}) — sheets to create
- `viewPlacements` (array of {sheetNumber, viewId}, optional) — which view goes on which sheet
- `alignToSheet` (string, optional) — reference sheet for alignment

**Output:** `sheetsCreated`, `viewportsPlaced`, `viewportsAligned`.

### Workflow 4: `workflow_data_roundtrip`

**Purpose:** Export data to Excel, then provide the path for later import.

**Sequence:**
1. `export_to_excel` → file saved

This workflow is a single step that prepares the roundtrip. The import is manual (user edits the file, then calls `import_from_excel`). The tool returns the file path and instructions.

**Parameters:**
- `categories` (string array) — categories to export
- `parameterNames` (string array, optional) — specific parameters
- `includeTypeParameters` (bool, default false)

**Output:** `filePath`, `elementCount`, `parameterCount`, `instructions` ("Edit the file, then ask me to import it back").

### Workflow 5: `workflow_clash_review`

**Purpose:** Find clashes, isolate them, create section box for review.

**Sequence:**
1. `clash_detection` → clashes found between two categories
2. `operate_element` (isolate clashing elements) → elements isolated in current view
3. `section_box_from_selection` → 3D section box around clash area

**Parameters:**
- `categoryA` (string) — first category (e.g. "Structural Framing")
- `categoryB` (string) — second category (e.g. "Ducts")
- `tolerance` (number, default 0) — intersection tolerance in mm
- `createSectionBox` (bool, default true)

**Output:** `clashCount`, `clashes` (array with element pairs), `isolatedElements`, `sectionBoxViewId`.

### C# Implementation Pattern

All 5 workflows share a common pattern — a `WorkflowCommandBase` abstract class (or each workflow has its own command + handler). Each handler:

1. Creates a `TransactionGroup` named after the workflow
2. Calls existing event handlers in sequence (reusing their logic, NOT duplicating code)
3. Collects results from each step
4. Commits the transaction group
5. Returns combined result

Since event handlers are asynchronous (ExternalEvent), the workflow handler must orchestrate them sequentially. The simplest approach: the workflow handler runs on the Revit UI thread and calls the logic methods directly (not via ExternalEvent). Extract the core logic from each handler into static/shared methods that the workflow can call directly with a `Document` parameter.

**Alternative (simpler):** Each workflow C# handler just does the work directly — duplicating the Revit API calls from existing handlers but in a single Execute method. This avoids the complexity of calling handlers-from-handlers. The trade-off is some code duplication, but each workflow is self-contained and easier to maintain.

**Recommendation:** Use the simpler approach (self-contained Execute). The Revit API calls are straightforward (FilteredElementCollector, Transaction, etc.) and having them inline in the workflow handler is clearer than an abstraction layer.

---

## Feature 3: Next-Step Suggestions in Tool Responses

### Location
Server-side only — in the TypeScript tool files (`server/src/tools/*.ts`). Zero changes to C# code.

### Mechanism
After receiving the response from Revit, the TypeScript tool analyzes the result data and appends a `suggestedNextSteps` array to the JSON response:

```json
{
  "success": true,
  "data": { "healthScore": "C", "warningCount": 45, "unusedFamilies": 12 },
  "suggestedNextSteps": [
    { "prompt": "Show me all 45 model warnings", "reason": "Health score is C — 45 warnings need attention" },
    { "prompt": "Purge 12 unused families", "reason": "12 unused families detected during audit" }
  ]
}
```

### Suggestion Logic per Tool

| Tool | Condition | Suggestion |
|---|---|---|
| `check_model_health` | score < A | "Show me all warnings" |
| `check_model_health` | unusedFamilies > 0 | "Purge unused families" |
| `check_model_health` | always | "Audit all families" |
| `get_warnings` | warningCount > 0 | "Isolate the elements with most warnings" |
| `export_to_excel` | always | "When you're done editing, ask me to import {filePath} back" |
| `export_elements_data` | always | "Update these elements with sync_csv_parameters" |
| `clash_detection` | clashCount > 0 | "Isolate the clashing elements" |
| `clash_detection` | clashCount > 0 | "Create section box around the clash area" |
| `create_views_from_rooms` | viewsCreated > 0 | "Tag all rooms in the current view" |
| `create_views_from_rooms` | viewsCreated > 0 | "Create a color legend by department" |
| `batch_create_sheets` | sheetsCreated > 0 | "Place views on the new sheets" |
| `batch_create_sheets` | sheetsCreated > 0 | "Align viewports across sheets" |
| `audit_families` | unusedCount > 0 | "Purge unused families and types" |
| `audit_families` | issueCount > 0 | "Rename problematic families" |

### Implementation
Each tool's TypeScript handler wraps the response in a helper function:

```typescript
function addSuggestions(response: any, suggestions: Array<{prompt: string, reason: string}>) {
  return { ...response, suggestedNextSteps: suggestions.filter(s => s !== null) };
}
```

The suggestion logic is inline in each tool — simple if/else based on the response data. No separate config file or abstraction. Keep it obvious and local.

### Claude Behavior
Claude reads `suggestedNextSteps` naturally because it's in the JSON response. No special MCP protocol changes needed. Claude will say something like: *"The model has a C health score with 45 warnings. Want me to show you the warnings? I also found 12 unused families that could be purged."*

---

## Files Summary

| Action | File | Change |
|---|---|---|
| Modify | `plugin/UI/MCPDockablePanel.xaml` | Add chips bar above input |
| Modify | `plugin/UI/MCPDockablePanel.xaml.cs` | Context detection + chip generation logic |
| Create | `commandset/Commands/Workflow/WorkflowModelAuditCommand.cs` | Model audit workflow command |
| Create | `commandset/Services/Workflow/WorkflowModelAuditEventHandler.cs` | Model audit orchestration |
| Create | `commandset/Commands/Workflow/WorkflowRoomDocumentationCommand.cs` | Room docs workflow |
| Create | `commandset/Services/Workflow/WorkflowRoomDocumentationEventHandler.cs` | Room docs orchestration |
| Create | `commandset/Commands/Workflow/WorkflowSheetSetCommand.cs` | Sheet set workflow |
| Create | `commandset/Services/Workflow/WorkflowSheetSetEventHandler.cs` | Sheet set orchestration |
| Create | `commandset/Commands/Workflow/WorkflowDataRoundtripCommand.cs` | Data roundtrip workflow |
| Create | `commandset/Services/Workflow/WorkflowDataRoundtripEventHandler.cs` | Data roundtrip orchestration |
| Create | `commandset/Commands/Workflow/WorkflowClashReviewCommand.cs` | Clash review workflow |
| Create | `commandset/Services/Workflow/WorkflowClashReviewEventHandler.cs` | Clash review orchestration |
| Create | `server/src/tools/workflow_model_audit.ts` | MCP tool registration |
| Create | `server/src/tools/workflow_room_documentation.ts` | MCP tool registration |
| Create | `server/src/tools/workflow_sheet_set.ts` | MCP tool registration |
| Create | `server/src/tools/workflow_data_roundtrip.ts` | MCP tool registration |
| Create | `server/src/tools/workflow_clash_review.ts` | MCP tool registration |
| Create | `server/src/utils/suggestions.ts` | Shared addSuggestions helper |
| Modify | `server/src/tools/check_model_health.ts` | Add suggestedNextSteps |
| Modify | `server/src/tools/get_warnings.ts` | Add suggestedNextSteps |
| Modify | `server/src/tools/export_to_excel.ts` | Add suggestedNextSteps |
| Modify | `server/src/tools/export_elements_data.ts` | Add suggestedNextSteps |
| Modify | `server/src/tools/clash_detection.ts` | Add suggestedNextSteps |
| Modify | `server/src/tools/create_views_from_rooms.ts` | Add suggestedNextSteps |
| Modify | `server/src/tools/batch_create_sheets.ts` | Add suggestedNextSteps |
| Modify | `server/src/tools/audit_families.ts` | Add suggestedNextSteps |
| Modify | `command.json` | 5 new workflow commands |

**Total: ~16 new files, ~10 modified files. Zero existing C# tool code modified.**
