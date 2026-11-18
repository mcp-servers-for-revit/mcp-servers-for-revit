# OffAxisCommandSet for Autodesk Revit MCP Plugin

Off-axis line inaccuracy detection and remediation toolkit for Autodesk Revit models.
Provides intelligent constraint-safe corrections for slightly off-axis walls, beams, grids, reference planes, sketch profiles (Floors, Ceilings, FootPrint Roofs), In-Place Extrusion sketches, model curves, and 1/4" grid lattice regularizers.

Developed by **Scott Mitchell Studio** (`chris@scottmitchellstudio.com`).

---

## Prerequisites

1. **Autodesk Revit 2024**
2. **Revit MCP Plugin** (`mcp-servers-for-revit`) installed on your system.

---

## Quick Installation (Recommended)

1. Extract this ZIP archive anywhere on your machine.
2. Open PowerShell in the extracted folder.
3. Run the installer script:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```
4. Restart Autodesk Revit if it is open.

---

## Manual Installation (Via Plugin UI)

If you prefer to install manually using Revit's interface:

1. Open Autodesk Revit 2024.
2. In the ribbon, go to **Revit MCP** -> **Settings** -> **Command Set** tab.
3. Click the **"Open CommandSet Folder"** button. Windows Explorer will open at:
   `%APPDATA%\Autodesk\Revit\Addins\2024\revit_mcp_plugin\Commands`
4. Copy the `OffAxisCommandSet` folder into this directory.
5. In the Revit MCP Settings window, click **"Refresh"**.
6. Select **OffAxisCommandSet** from the list, ensure the commands are checked, and click **"Save Settings"**.
7. Restart Autodesk Revit.

---

## Available Commands

| Command | Type | Description |
|---|---|---|
| `detect_off_axis_hybrid` | Detector | Warning-driven fast detection of off-axis elements and sketches |
| `detect_off_axis_lines` | Detector | Geometric scan for lines with angular deviation between min/max angles |
| `detect_spacing_elements` | Detector | Detects elements off the 1/4" perpendicular grid lattice |
| `fix_off_axis_walls_and_beams` | Fixer | Snaps endpoints of slightly off-axis walls and structural framing |
| `fix_off_axis_grids` | Fixer | Rotates slightly off-axis 3D grid lines about their midpoint |
| `fix_off_axis_reference_planes` | Fixer | Snaps normal of slightly off-axis vertical reference planes |
| `fix_off_axis_sketches` | Fixer | Ray-ray closed sketch profile solver (Floors, Ceilings, Roofs) |
| `fix_off_axis_inplace` | Fixer | In-Place Extrusion sketch profile solver (midpoint rotation + corner closure) |
| `fix_off_axis_model_lines` | Fixer | Chained ray-ray solver for top-level non-sketch model curves |
| `fix_spacing_elements` | Fixer | Live-recomputing iterative 1/4" lattice regularizer |

---

## Uninstallation

To remove this command set:
```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```
