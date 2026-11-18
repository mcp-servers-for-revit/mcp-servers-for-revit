# Revit MCP Schedule System — Design Spec

## Overview

Comprehensive schedule creation, modification, and querying system for the Revit MCP plugin. Three-tier architecture: enhanced core command, preset shortcuts, and CRUD operations.

## Tier 1 — Enhanced `create_schedule` Command

Improve the existing `create_schedule` command to support all Revit schedule types and full field configuration.

### Schedule Types (via `scheduleType` parameter)

| Type | Revit API Method | Description |
|------|-----------------|-------------|
| `regular` | `ViewSchedule.CreateSchedule(doc, categoryId)` | Standard element schedule |
| `key_schedule` | `ViewSchedule.CreateKeySchedule(doc, categoryId)` | Key schedule for shared parameters |
| `material_takeoff` | `ViewSchedule.CreateMaterialTakeoff(doc, categoryId)` | Material quantities |
| `note_block` | `ViewSchedule.CreateNoteBlock(doc, familyId)` | Annotation symbol schedule |
| `sheet_list` | `ViewSchedule.CreateSheetList(doc)` | Sheet index |
| `view_list` | `ViewSchedule.CreateViewList(doc)` | View index |
| `revision_schedule` | `ViewSchedule.CreateRevisionSchedule(doc)` | Revision tracking |
| `keynote_legend` | `ViewSchedule.CreateKeynoteLegend(doc)` | Keynote legend |

### Field Configuration

Each field supports:
- `parameterName` (string) — field lookup by name
- `fieldType` (string) — Instance, Type, Count, Formula
- `heading` (string) — custom column heading
- `horizontalAlignment` (string) — Left, Center, Right
- `isHidden` (boolean) — hide field
- `gridColumnWidth` (double) — column width
- `isCalculatedField` (boolean) — calculated field flag
- `formula` (string) — formula for calculated fields

### Filter Configuration

Each filter supports:
- `fieldName` (string) — field to filter
- `filterType` (string) — Equal, NotEqual, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Contains, DoesNotContain, BeginsWith, DoesNotBeginWith, EndsWith, DoesNotEndWith, HasValue, HasNoValue
- `filterValue` (string) — value to filter by

### Sort/Group Configuration

Each sort/group field supports:
- `fieldName` (string) — field to sort/group by
- `sortOrder` (string) — Ascending, Descending
- `showHeader` (boolean) — show group header
- `showFooter` (boolean) — show group footer
- `showBlankLine` (boolean) — blank line after group
- `showCount` (boolean) — show item count in header

### Display Options

- `showTitle` (boolean) — show schedule title
- `showHeaders` (boolean) — show column headers
- `showGridLines` (boolean) — show grid lines
- `isItemized` (boolean) — itemize every instance
- `showGrandTotal` (boolean) — show grand total row
- `showGrandTotalCount` (boolean) — show count in grand total
- `grandTotalTitle` (string) — custom grand total label
- `includeLinkedFiles` (boolean) — include linked model elements

### Preset Parameter

`preset` (string, optional) — auto-configures fields, filters, sorting for common use cases:
- `room_finish` — Number, Name, Level, Area, Floor/Wall/Ceiling/Base Finish sorted by Number
- `door_by_room` — From Room Number/Name, Family, Type, Width, Height, Mark sorted by Room
- `window_by_room` — From Room Number/Name, Family, Type, Width, Height, Sill/Head Height sorted by Room
- `wall_summary` — Family, Type, Width, Length, Area, Volume grouped by Type
- `material_quantities` — Material, Area, Volume sorted by Material
- `family_inventory` — Family, Type, Count grouped by Family

When `preset` is used, explicit fields/filters/sorting override the preset defaults.

## Tier 2 — Shortcut Commands

Each shortcut wraps `create_schedule` with a preset and optional customization.

### `create_room_finish_schedule`
- **Parameters**: `name` (optional), `levelFilter` (optional), `additionalFields` (optional)
- **Default fields**: Number, Name, Level, Area, Floor Finish, Wall Finish, Ceiling Finish, Base Finish
- **Default sort**: Number ascending

### `create_door_schedule_by_room`
- **Parameters**: `name`, `levelFilter`, `includeToRoom` (default true)
- **Default fields**: From Room Number, From Room Name, To Room Number, Family, Type, Width, Height, Mark, Level, Comments
- **Default sort**: From Room Number ascending

### `create_window_schedule_by_room`
- **Parameters**: `name`, `levelFilter`
- **Default fields**: From Room Number, From Room Name, Family, Type, Width, Height, Sill Height, Head Height, Mark, Level
- **Default sort**: From Room Number ascending

### `create_material_takeoff_schedule`
- **Parameters**: `name`, `categoryName` (required), `includeVolume` (default true)
- **Default fields**: Material Name, Material Class, Area, Volume
- **Default sort**: Material Name ascending

### `create_sheet_list_schedule`
- **Parameters**: `name`
- **Default fields**: Sheet Number, Sheet Name, Drawn By, Checked By, Current Revision, Current Revision Date
- **Default sort**: Sheet Number ascending

### `create_view_list_schedule`
- **Parameters**: `name`
- **Default fields**: View Name, View Type, Scale, Sheet Number, Sheet Name
- **Default sort**: View Type then View Name ascending

## Tier 3 — CRUD Operations

### `modify_schedule`
- **Parameters**:
  - `scheduleId` or `scheduleName` — target schedule
  - `action` — one of: `add_field`, `remove_field`, `set_filter`, `clear_filters`, `set_sorting`, `clear_sorting`, `set_grouping`, `rename`, `set_display_options`
  - Action-specific parameters (field config, filter config, etc.)
- **Multiple actions** can be batched in one call via `actions` array

### `delete_schedule`
- **Parameters**: `scheduleId` or `scheduleName`, `confirm` (boolean, required)

### `duplicate_schedule`
- **Parameters**: `scheduleId` or `scheduleName`, `newName` (required)

### `list_schedulable_fields`
- **Parameters**: `categoryName` or `categoryId`, `scheduleType` (default "regular")
- **Returns**: list of all available fields with name, type, parameter ID
- **Purpose**: AI utility — lets Claude discover which fields exist before creating a schedule

## Implementation Files

### C# (commandset/)
- `Commands/DataExtraction/CreateScheduleCommand.cs` — enhance existing
- `Services/CreateScheduleEventHandler.cs` — enhance existing, add presets + all types
- `Commands/DataExtraction/ModifyScheduleCommand.cs` — new
- `Services/DataExtraction/ModifyScheduleEventHandler.cs` — new
- `Commands/DataExtraction/DeleteScheduleCommand.cs` — new (simple, reuse DeleteElementEventHandler pattern)
- `Commands/DataExtraction/DuplicateScheduleCommand.cs` — new
- `Services/DataExtraction/DuplicateScheduleEventHandler.cs` — new
- `Commands/DataExtraction/ListSchedulableFieldsCommand.cs` — new
- `Services/DataExtraction/ListSchedulableFieldsEventHandler.cs` — new

### TypeScript (server/src/tools/)
- `create_schedule.ts` — enhance existing
- Shortcut tools (6): `create_room_finish_schedule.ts`, `create_door_schedule_by_room.ts`, `create_window_schedule_by_room.ts`, `create_material_takeoff_schedule.ts`, `create_sheet_list_schedule.ts`, `create_view_list_schedule.ts`
- `modify_schedule.ts` — new
- `delete_schedule.ts` — new
- `duplicate_schedule.ts` — new
- `list_schedulable_fields.ts` — new

### Registration
- `command.json` — add new command entries
- `register.ts` — add imports and module entries

## Total: ~20 files to create/modify, 12 new MCP tools
