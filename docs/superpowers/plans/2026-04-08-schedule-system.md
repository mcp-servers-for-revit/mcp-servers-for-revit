# Schedule System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Comprehensive schedule creation, modification, and querying system covering all Revit schedule types with presets and CRUD operations.

**Architecture:** Enhance existing `create_schedule` C# handler to support all 8 schedule types + presets. Add 4 new CRUD commands (modify, delete, duplicate, list_fields). Add 6 TypeScript shortcut tools that wrap create_schedule with preset configs. All C# code follows the ExternalEventCommandBase pattern.

**Tech Stack:** C# (.NET 8 / Revit 2025 API), TypeScript (MCP SDK + Zod), Node.js ESBuild

**Source root:** `C:/Users/luigi.dattilo/Documents/revit-mcp-server/`

---

### Task 1: Enhance ScheduleCreationInfo model

**Files:**
- Modify: `commandset/Models/Views/ScheduleCreationInfo.cs`

- [ ] **Step 1: Add new properties to ScheduleCreationInfo**

Add `Preset`, `IsItemized`, `ShowGrandTotal`, `ShowGrandTotalCount`, `GrandTotalTitle`, `IncludeLinkedFiles`, `FamilyId` fields:

```csharp
// Add after ShowOutlines property (line ~97)

[JsonProperty("preset")]
public string Preset { get; set; } = string.Empty;

[JsonProperty("isItemized")]
public bool? IsItemized { get; set; }

[JsonProperty("showGrandTotal")]
public bool? ShowGrandTotal { get; set; }

[JsonProperty("showGrandTotalCount")]
public bool? ShowGrandTotalCount { get; set; }

[JsonProperty("grandTotalTitle")]
public string GrandTotalTitle { get; set; } = string.Empty;

[JsonProperty("includeLinkedFiles")]
public bool? IncludeLinkedFiles { get; set; }

[JsonProperty("familyId")]
public long FamilyId { get; set; } = 0;
```

- [ ] **Step 2: Add GridColumnWidth to ScheduleFieldInfo**

Add after `HorizontalAlignment` property:

```csharp
[JsonProperty("gridColumnWidth")]
public double? GridColumnWidth { get; set; }
```

- [ ] **Step 3: Commit**

```bash
git add commandset/Models/Views/ScheduleCreationInfo.cs
git commit -m "feat(schedule): add preset, display, and column width properties to model"
```

---

### Task 2: Enhance CreateScheduleEventHandler — all schedule types + presets

**Files:**
- Modify: `commandset/Services/CreateScheduleEventHandler.cs`

- [ ] **Step 1: Add all 8 schedule type creation in the switch**

Replace the switch block (lines 42-53) with:

```csharp
switch (scheduleType.ToLower())
{
    case "keyschedule":
    case "key_schedule":
        schedule = ViewSchedule.CreateKeySchedule(doc, categoryId);
        break;
    case "materialtakeoff":
    case "material_takeoff":
        schedule = ViewSchedule.CreateMaterialTakeoff(doc, categoryId);
        break;
    case "noteblock":
    case "note_block":
        var familyId = ScheduleInfo.FamilyId > 0
            ? new ElementId(ScheduleInfo.FamilyId)
            : new FilteredElementCollector(doc).OfClass(typeof(Family)).FirstElementId();
        schedule = ViewSchedule.CreateNoteBlock(doc, familyId);
        break;
    case "sheetlist":
    case "sheet_list":
        schedule = ViewSchedule.CreateSheetList(doc);
        break;
    case "viewlist":
    case "view_list":
        schedule = ViewSchedule.CreateViewList(doc);
        break;
    case "revisionschedule":
    case "revision_schedule":
        schedule = ViewSchedule.CreateRevisionSchedule(doc);
        break;
    case "keynotelegend":
    case "keynote_legend":
        schedule = ViewSchedule.CreateKeynoteLegend(doc);
        break;
    default:
        schedule = ViewSchedule.CreateSchedule(doc, categoryId);
        break;
}
```

- [ ] **Step 2: Add preset resolver method**

Add this method after `SetDisplayProperties`:

```csharp
private void ApplyPreset(ViewSchedule schedule)
{
    if (string.IsNullOrEmpty(ScheduleInfo.Preset)) return;

    var presetFields = GetPresetFields(ScheduleInfo.Preset);
    if (presetFields == null) return;

    // Only apply preset fields if user hasn't specified custom fields
    if (ScheduleInfo.Fields == null || ScheduleInfo.Fields.Count == 0)
    {
        ScheduleInfo.Fields = presetFields.Select(f => new ScheduleFieldInfo { ParameterName = f }).ToList();
    }

    // Apply default sort by first field if no sort specified
    if (ScheduleInfo.SortFields == null || ScheduleInfo.SortFields.Count == 0)
    {
        ScheduleInfo.SortFields = new List<ScheduleSortInfo>
        {
            new ScheduleSortInfo { FieldName = presetFields[0], SortOrder = "Ascending" }
        };
    }
}

private List<string> GetPresetFields(string preset)
{
    switch (preset.ToLower())
    {
        case "room_finish":
            return new List<string> { "Number", "Name", "Level", "Area", "Floor Finish", "Wall Finish", "Ceiling Finish", "Base Finish" };
        case "door_by_room":
            return new List<string> { "From Room: Number", "From Room: Name", "To Room: Number", "Family", "Type", "Width", "Height", "Mark", "Level", "Comments" };
        case "window_by_room":
            return new List<string> { "From Room: Number", "From Room: Name", "Family", "Type", "Width", "Height", "Sill Height", "Head Height", "Mark", "Level" };
        case "wall_summary":
            return new List<string> { "Family", "Type", "Width", "Length", "Area", "Volume" };
        case "material_quantities":
            return new List<string> { "Material: Name", "Material: Class", "Area", "Volume" };
        case "family_inventory":
            return new List<string> { "Family", "Type", "Count" };
        case "sheet_index":
            return new List<string> { "Sheet Number", "Sheet Name", "Drawn By", "Checked By", "Current Revision", "Current Revision Date" };
        case "view_index":
            return new List<string> { "View Name", "View Type", "Scale", "Sheet Number", "Sheet Name" };
        default:
            return null;
    }
}
```

- [ ] **Step 3: Call ApplyPreset before AddFields, enhance SetDisplayProperties**

In Execute(), add `ApplyPreset(schedule);` right before `var addedFields = AddFields(schedule);`

Replace `SetDisplayProperties` with:

```csharp
private void SetDisplayProperties(ViewSchedule schedule)
{
    var def = schedule.Definition;

    if (ScheduleInfo.ShowTitle.HasValue)
        def.ShowTitle = ScheduleInfo.ShowTitle.Value;
    if (ScheduleInfo.ShowHeaders.HasValue)
        def.ShowHeaders = ScheduleInfo.ShowHeaders.Value;
    if (ScheduleInfo.ShowGridLines.HasValue)
        def.ShowGridLines = ScheduleInfo.ShowGridLines.Value;
    if (ScheduleInfo.IsItemized.HasValue)
        def.IsItemized = ScheduleInfo.IsItemized.Value;
    if (ScheduleInfo.ShowGrandTotal.HasValue)
        def.ShowGrandTotal = ScheduleInfo.ShowGrandTotal.Value;
    if (ScheduleInfo.ShowGrandTotalCount.HasValue)
        def.ShowGrandTotalCount = ScheduleInfo.ShowGrandTotalCount.Value;
    if (!string.IsNullOrEmpty(ScheduleInfo.GrandTotalTitle))
        def.GrandTotalTitle = ScheduleInfo.GrandTotalTitle;
    if (ScheduleInfo.IncludeLinkedFiles.HasValue && def.CanIncludeLinkedFiles())
        def.IncludeLinkedFiles = ScheduleInfo.IncludeLinkedFiles.Value;
}
```

- [ ] **Step 4: Add GridColumnWidth in AddFields**

After `addedField.HorizontalAlignment = ...` block, add:

```csharp
if (fieldInfo.GridColumnWidth.HasValue && fieldInfo.GridColumnWidth.Value > 0)
    addedField.GridColumnWidth = fieldInfo.GridColumnWidth.Value;
```

- [ ] **Step 5: Add grouping support**

Add method after `AddSortFields`:

```csharp
private void AddGroupFields(ViewSchedule schedule, Dictionary<string, ScheduleFieldId> addedFields)
{
    if (ScheduleInfo.GroupFields == null || ScheduleInfo.GroupFields.Count == 0)
        return;

    foreach (var groupInfo in ScheduleInfo.GroupFields)
    {
        ScheduleFieldId fieldId = null;

        if (!string.IsNullOrEmpty(groupInfo.FieldName) &&
            addedFields.TryGetValue(groupInfo.FieldName, out var resolvedFieldId))
        {
            fieldId = resolvedFieldId;
        }
        else if (groupInfo.FieldIndex >= 0 && groupInfo.FieldIndex < schedule.Definition.GetFieldCount())
        {
            fieldId = schedule.Definition.GetField(groupInfo.FieldIndex).FieldId;
        }

        if (fieldId == null) continue;

        var sortOrder = ScheduleSortOrder.Ascending;
        if (!string.IsNullOrEmpty(groupInfo.SortOrder) &&
            groupInfo.SortOrder.Equals("Descending", StringComparison.OrdinalIgnoreCase))
            sortOrder = ScheduleSortOrder.Descending;

        var sgf = new ScheduleSortGroupField(fieldId, sortOrder);
        sgf.ShowHeader = groupInfo.ShowHeader;
        sgf.ShowFooter = groupInfo.ShowFooter;
        sgf.ShowBlankLine = groupInfo.ShowBlankLine;
        schedule.Definition.AddSortGroupField(sgf);
    }
}
```

Call `AddGroupFields(schedule, addedFields);` after `AddSortFields` in Execute().

- [ ] **Step 6: Increase timeout in CreateScheduleCommand.cs**

Change `RaiseAndWaitForCompletion` to `120000` (2 minutes).

- [ ] **Step 7: Commit**

```bash
git add commandset/Services/CreateScheduleEventHandler.cs commandset/Commands/CreateScheduleCommand.cs
git commit -m "feat(schedule): support all 8 types, presets, grouping, display options"
```

---

### Task 3: Enhance create_schedule TypeScript tool

**Files:**
- Modify: `server/src/tools/create_schedule.ts`

- [ ] **Step 1: Update the schema to include all new options**

Replace entire file with enhanced version adding: `preset`, `scheduleType` (all 8 types), `groupFields`, `isItemized`, `showGrandTotal`, `showGrandTotalCount`, `grandTotalTitle`, `includeLinkedFiles`, `familyId`, `gridColumnWidth` on fields.

Key additions to schema:
- `preset: z.enum(["room_finish", "door_by_room", "window_by_room", "wall_summary", "material_quantities", "family_inventory", "sheet_index", "view_index"]).optional()`
- `scheduleType: z.enum(["regular", "key_schedule", "material_takeoff", "note_block", "sheet_list", "view_list", "revision_schedule", "keynote_legend"]).optional().default("regular")`
- groupFields array with fieldName, sortOrder, showHeader, showFooter, showBlankLine
- Display options: isItemized, showGrandTotal, showGrandTotalCount, grandTotalTitle, includeLinkedFiles

- [ ] **Step 2: Build and verify**

```bash
cd server && npm run build
```

- [ ] **Step 3: Commit**

```bash
git add server/src/tools/create_schedule.ts
git commit -m "feat(schedule): enhance TS tool with all types, presets, grouping"
```

---

### Task 4: Create 6 shortcut tools (TypeScript only — they call create_schedule)

**Files:**
- Create: `server/src/tools/create_room_finish_schedule.ts`
- Create: `server/src/tools/create_door_schedule_by_room.ts`
- Create: `server/src/tools/create_window_schedule_by_room.ts`
- Create: `server/src/tools/create_material_takeoff_schedule.ts`
- Create: `server/src/tools/create_sheet_list_schedule.ts`
- Create: `server/src/tools/create_view_list_schedule.ts`
- Modify: `server/src/tools/register.ts`

Each shortcut tool follows this pattern:
- Accepts `name` (optional), `levelFilter` (optional for room/door/window), and type-specific params
- Calls `revitClient.sendCommand("create_schedule", { preset: "xxx", categoryName: "OST_xxx", name, ... })`
- Minimal schema — the preset handles field configuration

- [ ] **Step 1: Create all 6 shortcut files**

Each file ~40 lines following the standard pattern. Category mappings:
- room_finish → `OST_Rooms`
- door_by_room → `OST_Doors`
- window_by_room → `OST_Windows`
- material_takeoff → type `material_takeoff` + category param
- sheet_list → type `sheet_list` (no category needed)
- view_list → type `view_list` (no category needed)

- [ ] **Step 2: Register all 6 in register.ts**

Add imports and module entries alphabetically.

- [ ] **Step 3: Build and verify**

```bash
cd server && npm run build
```
Expected: tool count increases by 6.

- [ ] **Step 4: Commit**

```bash
git add server/src/tools/create_*_schedule*.ts server/src/tools/register.ts
git commit -m "feat(schedule): add 6 shortcut schedule tools"
```

---

### Task 5: Create list_schedulable_fields command

**Files:**
- Create: `commandset/Commands/DataExtraction/ListSchedulableFieldsCommand.cs`
- Create: `commandset/Services/DataExtraction/ListSchedulableFieldsEventHandler.cs`
- Create: `server/src/tools/list_schedulable_fields.ts`
- Modify: `server/src/tools/register.ts`
- Modify: `command.json`

- [ ] **Step 1: Create EventHandler**

Logic:
1. Create a temporary ViewSchedule for the given category
2. Call `def.GetSchedulableFields()`
3. Return field names and types
4. Delete the temporary schedule

```csharp
// Key logic in Execute():
var catId = ResolveCategoryId(doc);
var tempSchedule = ViewSchedule.CreateSchedule(doc, catId);
var fields = tempSchedule.Definition.GetSchedulableFields();
var result = fields.Select(f => new {
    name = f.GetName(doc),
    fieldType = f.FieldType.ToString(),
    parameterId = f.ParameterId.Value
}).OrderBy(f => f.name).ToList();
doc.Delete(tempSchedule.Id);
return new { category = categoryName, fieldCount = result.Count, fields = result };
```

- [ ] **Step 2: Create Command class**

Standard pattern: parse `categoryName`, `scheduleType`. Timeout 30000ms.

- [ ] **Step 3: Create TypeScript tool**

Schema: `categoryName` (required string), `scheduleType` (optional enum).

- [ ] **Step 4: Register and add to command.json**

- [ ] **Step 5: Commit**

```bash
git add commandset/Commands/DataExtraction/ListSchedulableFieldsCommand.cs \
  commandset/Services/DataExtraction/ListSchedulableFieldsEventHandler.cs \
  server/src/tools/list_schedulable_fields.ts \
  server/src/tools/register.ts command.json
git commit -m "feat(schedule): add list_schedulable_fields utility command"
```

---

### Task 6: Create modify_schedule command

**Files:**
- Create: `commandset/Commands/DataExtraction/ModifyScheduleCommand.cs`
- Create: `commandset/Services/DataExtraction/ModifyScheduleEventHandler.cs`
- Create: `server/src/tools/modify_schedule.ts`
- Modify: `server/src/tools/register.ts`
- Modify: `command.json`

- [ ] **Step 1: Create EventHandler**

Supports actions: `add_field`, `remove_field`, `set_filters`, `clear_filters`, `set_sorting`, `clear_sorting`, `rename`, `set_display_options`

Key logic:
```csharp
// Find schedule by ID or name
ViewSchedule schedule = FindSchedule(doc);
var def = schedule.Definition;

switch (Action)
{
    case "add_field":
        // Add fields from FieldNames list
        break;
    case "remove_field":
        // Remove fields by name
        break;
    case "set_filters":
        def.ClearFilters();
        // Add new filters
        break;
    case "clear_filters":
        def.ClearFilters();
        break;
    case "set_sorting":
        def.ClearSortGroupFields();
        // Add new sort/group fields
        break;
    case "clear_sorting":
        def.ClearSortGroupFields();
        break;
    case "rename":
        schedule.Name = NewName;
        break;
    case "set_display_options":
        // Set ShowTitle, ShowHeaders, etc.
        break;
}
```

- [ ] **Step 2: Create Command class**

Parse: `scheduleId` or `scheduleName`, `action`, action-specific params. Timeout 60000ms.

- [ ] **Step 3: Create TypeScript tool**

Schema with action enum and conditional params.

- [ ] **Step 4: Register and add to command.json**

- [ ] **Step 5: Commit**

```bash
git add commandset/Commands/DataExtraction/ModifyScheduleCommand.cs \
  commandset/Services/DataExtraction/ModifyScheduleEventHandler.cs \
  server/src/tools/modify_schedule.ts \
  server/src/tools/register.ts command.json
git commit -m "feat(schedule): add modify_schedule CRUD command"
```

---

### Task 7: Create delete_schedule and duplicate_schedule commands

**Files:**
- Create: `commandset/Commands/DataExtraction/DeleteScheduleCommand.cs`
- Create: `commandset/Services/DataExtraction/DeleteScheduleEventHandler.cs`
- Create: `commandset/Commands/DataExtraction/DuplicateScheduleCommand.cs`
- Create: `commandset/Services/DataExtraction/DuplicateScheduleEventHandler.cs`
- Create: `server/src/tools/delete_schedule.ts`
- Create: `server/src/tools/duplicate_schedule.ts`
- Modify: `server/src/tools/register.ts`
- Modify: `command.json`

- [ ] **Step 1: Create delete_schedule**

Simple: find schedule by ID or name, call `doc.Delete(schedule.Id)`. Requires `confirm: true`.

- [ ] **Step 2: Create duplicate_schedule**

Use `schedule.Duplicate(ViewDuplicateOption.Duplicate)`, then rename to `newName`.

- [ ] **Step 3: Create both TypeScript tools**

- [ ] **Step 4: Register and add to command.json**

- [ ] **Step 5: Commit**

```bash
git add commandset/Commands/DataExtraction/Delete* \
  commandset/Commands/DataExtraction/Duplicate* \
  commandset/Services/DataExtraction/Delete* \
  commandset/Services/DataExtraction/Duplicate* \
  server/src/tools/delete_schedule.ts \
  server/src/tools/duplicate_schedule.ts \
  server/src/tools/register.ts command.json
git commit -m "feat(schedule): add delete and duplicate schedule commands"
```

---

### Task 8: Build, deploy, and test

**Files:**
- Modify: `command.json` (final verification)

- [ ] **Step 1: Build C#**

```bash
cd C:/Users/luigi.dattilo/Documents/revit-mcp-server
dotnet build commandset/RevitMCPCommandSet.csproj --configuration "Debug R25"
```
Expected: 0 errors

- [ ] **Step 2: Build TypeScript**

```bash
cd server && npm run build
```
Expected: tool count ~124+ (was 114)

- [ ] **Step 3: Deploy to Revit** (requires Revit closed)

Rebuild with Revit closed so DLL copies to Addins folder.

- [ ] **Step 4: Test presets**

Test each preset: room_finish, door_by_room, window_by_room, material_quantities, sheet_index, view_index via the shortcut commands.

- [ ] **Step 5: Test CRUD**

- list_schedulable_fields for OST_Rooms
- modify_schedule: rename, add_field, set_filters
- duplicate_schedule
- delete_schedule

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "feat(schedule): complete schedule system - 12 new tools, all types, presets, CRUD"
```
