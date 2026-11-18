# Revit MCP Server — User Guide

## Quick Start

1. **Install the plugin** from the [Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) page (do NOT copy source code from the repository)
2. Open **Revit 2023/2024/2025/2026/2027** with a project
3. In the **Add-Ins** tab, verify you see **three buttons**: Revit MCP Switch, MCP Panel, Settings
4. Click **Revit MCP Switch** to start the TCP server (indicator turns green)
5. Open **Claude Desktop** (or Claude Code / Claude.ai with MCP enabled)
6. Start asking Claude to interact with your Revit model

---

## Available Tools (124 commands)

### Model Information & Data Extraction

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `get_project_info` | Project overview: levels, phases, worksets, links | "What levels does this project have?" |
| `get_current_view_info` | Active view details: type, scale, crop | "What view am I looking at?" |
| `get_current_view_elements` | List elements visible in active view | "What elements are in this view?" |
| `get_selected_elements` | Get IDs of elements selected in Revit UI | "What do I have selected?" |
| `get_available_family_types` | List loaded families and types | "Show me available door types" |
| `get_element_parameters` | All parameters of specific elements | "Show me the parameters of element 12345" |
| `analyze_model_statistics` | Element counts by category, type, level | "How complex is this model?" |
| `export_room_data` | All rooms with area, volume, boundaries | "Export all room data" |
| `get_material_quantities` | Material takeoffs with areas/volumes | "Calculate material quantities for walls" |
| `export_elements_data` | Export elements by category with filters | "Export all doors with Mark, Level, Width" |
| `get_elements_in_spatial_volume` | Find elements inside rooms/areas/boxes | "What furniture is in room 101?" |

### Element Creation

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `create_point_based_element` | Place doors, furniture, fixtures | "Place a desk at coordinates 5000, 3000" |
| `create_line_based_element` | Create walls, beams, pipes | "Create a wall from (0,0) to (5000,0)" |
| `create_surface_based_element` | Create floors, ceilings | "Create a floor with these boundary points" |
| `create_floor` | Floors from points or room boundaries | "Create floors for all rooms on Level 1" |
| `create_room` | Place rooms with names and numbers | "Place rooms in all enclosed spaces" |
| `create_grid` | Grid systems with spacing | "Create a 6x4 grid at 7200mm spacing" |
| `create_level` | Levels with auto floor plan generation | "Create levels at 0, 3000, 6000, 9000mm" |
| `create_structural_framing_system` | Beam systems with spacing | "Create beam framing at 1200mm spacing" |

### Element Modification

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `set_element_parameters` | Set parameter values on elements | "Set the Comments to 'Reviewed' on element 12345" |
| `modify_element` | Move, rotate, copy, mirror elements | "Move element 12345 by 1000mm in X" |
| `delete_element` | Delete elements by ID | "Delete elements 12345 and 12346" |
| `change_element_type` | Swap element types in batch | "Change all door type X to type Y" |
| `operate_element` | Select, hide, isolate, color elements | "Isolate all structural columns in this view" |
| `override_graphics` | Per-element color, transparency, halftone | "Make element 12345 red and 50% transparent" |
| `create_array` | Linear or radial arrays | "Create 5 copies of element 12345 at 2000mm spacing" |
| `copy_elements` | Copy elements between views | "Copy dimensions from view A to view B" |
| `match_element_properties` | Copy properties source to targets | "Match properties from element A to elements B, C, D" |

### Bulk Operations

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `bulk_modify_parameter_values` | Set/prefix/suffix/replace/clear in batch | "Add prefix 'REV-' to all door Marks" |
| `sync_csv_parameters` | Import parameter values from data | "Update these elements with this data" |
| `transfer_parameters` | Copy params from source to targets | "Copy Mark and Comments from A to B, C, D" |
| `batch_rename` | Find/replace on view/sheet/element names | "Rename all views replacing 'Draft' with 'Final'" |
| `renumber_elements` | Sequential renumbering by spatial order | "Renumber all rooms left-to-right, top-to-bottom" |
| `purge_unused` | Remove unused families/types/materials | "Show me what can be purged" |

### Views & Documentation

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `create_view` | Floor plans, sections, elevations, 3D | "Create a floor plan for Level 2" |
| `create_sheet` | Sheets with title blocks | "Create sheet A101 - Floor Plan" |
| `place_viewport` | Place views on sheets | "Place the Level 1 floor plan on sheet A101" |
| `create_schedule` | Schedules with fields, filters, sorting | "Create a door schedule with Mark, Level, Width" |
| `create_dimensions` | Dimension annotations | "Dimension between these walls" |
| `create_text_note` | Text annotations in views | "Add a note 'Verify on site' at this location" |
| `create_filled_region` | Hatched/colored areas in views | "Create a filled region highlighting this area" |
| `create_revision` | Revision tracking on sheets | "Add revision 'Updated floor plan' to sheet A101" |
| `duplicate_view` | Copy views (independent/dependent) | "Duplicate this view with detailing" |
| `create_view_filter` | View filters for visibility control | "Create a filter to hide all furniture" |
| `apply_view_template` | Apply/list/remove view templates | "Apply template 'Architectural Plan' to all floor plans" |
| `create_views_from_rooms` | Auto-create views from rooms | "Create section views for all rooms on Level 1" |
| `batch_create_sheets` | Multiple sheets at once | "Create sheets A201-A210 with title block 'A1'" |
| `duplicate_sheet_with_content` | Duplicate sheets with all content | "Duplicate sheet A101 as A101-Rev2" |
| `align_viewports` | Align viewports across sheets | "Align all floor plan viewports to match A101" |
| `batch_modify_view_range` | Batch view range settings | "Set cut plane to 1200mm on all floor plans" |
| `section_box_from_selection` | 3D section box from elements | "Create section box around selected elements" |
| `batch_export` | Export to PDF, DWG, IFC | "Export all sheets to PDF" |
| `export_schedule` | Export schedule to CSV | "Export the door schedule to CSV" |

### Visualization

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `color_elements` | Color elements by parameter | "Color rooms by department" |
| `create_color_legend` | Color + legend view generation | "Colorize rooms by department with a legend" |
| `tag_all_rooms` | Tag all rooms in current view | "Tag all rooms" |
| `tag_all_walls` | Tag all walls in current view | "Tag all walls" |

### Parameters & Properties

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `manage_project_parameters` | CRUD project parameters | "Create a text parameter 'Review Status' on Walls" |
| `get_shared_parameters` | List project/shared parameters | "What parameters are available?" |
| `add_shared_parameter` | Add shared parameter from file | "Add shared parameter 'FireRating' to Doors" |

### Model Quality & Auditing

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `check_model_health` | Comprehensive health score (A-F) | "Check the health of this model" |
| `audit_families` | Family health with unused detection | "Audit all families in this project" |
| `get_warnings` | All Revit warnings/errors | "Show me all model warnings" |
| `clash_detection` | Geometric intersection detection | "Check for clashes between walls and pipes" |
| `cad_link_cleanup` | Audit/remove CAD imports | "Are there any CAD imports to clean up?" |
| `measure_between_elements` | Distance measurements | "Measure the distance between elements A and B" |
| `find_undimensioned_elements` | Find elements without dimensions in a view | "Which elements in this view are not dimensioned?" |
| `find_untagged_elements` | Find elements without tags in a view | "Find all untagged walls in this view" |
| `lines_per_view_count` | Count detail/model lines per view | "Which views have the most line work?" |
| `list_family_sizes` | List families with instance/type counts | "Show me the largest families by instance count" |
| `wipe_empty_tags` | Find/remove tags with empty text | "Are there any empty tags to clean up?" |

### Organization & Collaboration

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `get_worksets` | List worksets | "What worksets are available?" |
| `set_element_workset` | Move elements to worksets | "Move these elements to the MEP workset" |
| `get_phases` | List project phases | "What phases exist?" |
| `set_element_phase` | Assign elements to phases | "Set these elements to 'New Construction'" |
| `get_materials` | List all materials | "What materials are in this project?" |
| `get_material_properties` | Detailed material properties | "Show properties of material 'Concrete'" |
| `load_family` | Load/list families | "Load family from C:/Families/MyDoor.rfa" |
| `manage_links` | Manage linked Revit models | "List all linked models" |

### Advanced

| Command | Description | Example Prompt |
|---------|-------------|----------------|
| `ai_element_filter` | Advanced multi-parameter filtering | "Find all doors on Level 1 with width > 900mm" |
| `send_code_to_revit` | Execute custom C# code | "Run this custom Revit API code..." |

---

## Common Workflows

### 1. Model Audit & Cleanup
```
1. "Check the health of this model"                    -> check_model_health
2. "Show me all warnings"                              -> get_warnings
3. "Audit all families"                                -> audit_families
4. "Show me the largest families"                      -> list_family_sizes
5. "Which views have excessive line work?"             -> lines_per_view_count
6. "Find untagged elements in this view"               -> find_untagged_elements
7. "Find undimensioned elements"                       -> find_undimensioned_elements
8. "Are there any empty tags?"                         -> wipe_empty_tags (dryRun)
9. "What can be purged?"                               -> purge_unused (dryRun)
10. "Clean up CAD imports"                             -> cad_link_cleanup
```

### 2. Room Data & Views
```
1. "Export all room data"                              -> export_room_data
2. "Create section views for all rooms"                -> create_views_from_rooms
3. "Tag all rooms"                                     -> tag_all_rooms
4. "Color rooms by department"                         -> create_color_legend
5. "Create a room schedule"                            -> create_schedule
```

### 3. Sheet Set Creation
```
1. "Create sheets A101-A105"                           -> batch_create_sheets
2. "Place floor plan views on sheets"                  -> place_viewport
3. "Align viewports across all sheets"                 -> align_viewports
4. "Add revision to all sheets"                        -> create_revision
5. "Export all sheets to PDF"                          -> batch_export
```

### 4. Data Export & Modification
```
1. "Export all doors with Mark, Level, Type"           -> export_elements_data
2. "Update door marks from this data"                  -> sync_csv_parameters
3. "Add prefix 'D-' to all door marks"                -> bulk_modify_parameter_values
4. "Renumber doors by spatial order"                   -> renumber_elements
```

### 5. Parameter Management
```
1. "List all project parameters"                       -> manage_project_parameters (list)
2. "Create parameter 'Review Status' on Walls"         -> manage_project_parameters (create)
3. "Set 'Review Status' to 'Pending' on all walls"    -> bulk_modify_parameter_values
4. "Export wall data with Review Status"               -> export_elements_data
```

### 6. Coordination & Clash Detection
```
1. "Check for clashes between ducts and beams"         -> clash_detection
2. "Show me the clashing elements"                     -> operate_element (isolate)
3. "Create section box around clash area"              -> section_box_from_selection
4. "Measure distance between elements"                 -> measure_between_elements
```

---

## Tips for Best Results

- **Be specific**: "Create a wall from (0,0,0) to (5000,0,0) on Level 1" works better than "Add a wall"
- **Check first**: Use `get_available_family_types` before creating elements to know exact names
- **Dry run**: Use `dryRun=true` on bulk operations before committing changes
- **Parameter names**: May be localized (e.g. "Commenti" in Italian Revit instead of "Comments")
- **Coordinates**: All values in millimeters unless specified otherwise
- **Element IDs**: Use `get_selected_elements`, `ai_element_filter`, or `get_current_view_elements` to find IDs

---

## Troubleshooting

### Installation Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| **Only Switch button visible** (no MCP Panel or Settings) | Source code was copied instead of pre-built Release | Download the correct ZIP from the [Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) page — do NOT copy `.cs` files from GitHub |
| **Plugin not in Add-Ins tab** | `.addin` file missing or in wrong folder | Verify `mcp-servers-for-revit.addin` is in `%AppData%\Autodesk\Revit\Addins\<version>\` |
| **Plugin not in Add-Ins tab** | DLLs blocked by Windows | Right-click each `.dll` → Properties → check "Unblock" if present |
| **Plugin not in Add-Ins tab** | Wrong Revit version ZIP | Download the ZIP matching your Revit year (e.g., Revit2025 ZIP for Revit 2025) |

### Connection & Runtime Issues

| Issue | Solution |
|-------|----------|
| "Connection refused" | Ensure Revit is open and MCP Switch is ON (green indicator) |
| "Element not found" | Verify element ID exists with `get_current_view_elements` |
| "Parameter not found" | Check exact name with `get_element_parameters` — names depend on Revit language |
| "Family type not found" | Use `get_available_family_types` for exact type names |
| "Tool not available" | Restart Claude Desktop to refresh MCP tool list |
| "Timeout" | Large operations may take time, try with fewer elements |
| `send_code_to_revit` compilation error (CS1704) | Caused by Autodesk BIM360 add-in loading duplicate assemblies. Fixed in v1.1+. Update the plugin via `install.ps1`. |
| Claude Desktop shows no tools / "revit-mcp not connected" | Run `scripts\fix-mcp.ps1` to repair the Claude Desktop configuration automatically |

### Verifying Your Installation

Your Addins folder should contain:
```
%AppData%\Autodesk\Revit\Addins\<version>\
├── mcp-servers-for-revit.addin       <-- must be here
└── revit_mcp_plugin/                 <-- subfolder with DLLs
    ├── RevitMCPPlugin.dll            <-- main plugin DLL
    └── Commands/                     <-- command set
```

If you see `.cs` or `.csproj` files instead of `.dll` files, you copied the source code. Download the pre-built ZIP from [Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) instead.

---

## Requirements

- **Revit**: 2023, 2024, 2025, 2026, or 2027
- **Plugin**: Pre-built Release ZIP installed (see [README](README.md#1-install-the-revit-plugin))
- **Server**: Node.js MCP server running (auto-started by Claude Desktop)
- **Connection**: localhost:8080 (Revit plugin listens, MCP server connects)
