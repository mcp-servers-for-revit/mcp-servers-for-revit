# Revit MCP — Quick Start Guide

This guide will get you up and running with Claude AI + Revit in under 5 minutes.
No programming or technical knowledge required.

---

## What You Need

| # | What | Where to get it |
|---|------|-----------------|
| 1 | **Autodesk Revit** (2023, 2024, 2025, 2026, or 2027) | Already installed on your PC |
| 2 | **Claude Desktop** | [claude.ai/download](https://claude.ai/download) — install and sign in |
| 3 | **Revit MCP Plugin** (ZIP file) | [Download from GitHub Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) |

> **You do NOT need** Node.js, Python, Git, or any other developer tool.
> Everything is included in the ZIP file.

---

## Installation (3 steps)

### Step 1 — Download the correct ZIP

Go to the [Releases page](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) and download the ZIP that matches your Revit version:

- `mcp-servers-for-revit-vX.Y.Z-Revit2023.zip`
- `mcp-servers-for-revit-vX.Y.Z-Revit2024.zip`
- `mcp-servers-for-revit-vX.Y.Z-Revit2025.zip`
- `mcp-servers-for-revit-vX.Y.Z-Revit2026.zip`
- `mcp-servers-for-revit-vX.Y.Z-Revit2027.zip`

### Step 2 — Extract to the Revit Addins folder

1. Press **Win + R** on your keyboard
2. Paste this path (replace `2025` with your Revit year if different):
   ```
   %AppData%\Autodesk\Revit\Addins\2025
   ```
3. Press **Enter** — a folder opens
4. **Extract** the ZIP contents directly into this folder

When done, you should see this structure:
```
Addins\2025\
  mcp-servers-for-revit.addin       <-- this file must be here
  revit_mcp_plugin\                 <-- this folder must be here
      RevitMCPPlugin.dll
      Commands\
      ...
```

### Step 3 — Open Revit

1. **Close Revit** if it's already open, then reopen it
2. If Windows asks about an unknown add-in, click **"Always Load"**
3. Go to the **Add-Ins** tab — you should see three buttons:
   - **Revit MCP Switch** — starts/stops the connection
   - **MCP Panel** — shows the monitoring panel
   - **Settings** — plugin settings

> **What happens automatically:** The first time Revit loads, the plugin configures Claude Desktop for you. You'll see a dialog saying *"Claude Desktop configured automatically"*. Just click OK.

---

## Using Claude with Revit

### First Time Setup

1. **In Revit:** Click **Revit MCP Switch** — the indicator turns green
2. **Restart Claude Desktop** completely (right-click the icon in the system tray → Quit, then reopen it)
3. In Claude Desktop, you should see a small hammer icon at the bottom — this means the Revit tools are available

### Talking to Claude

Open a chat in Claude Desktop and type naturally. Here are some examples to get started:

**Get information:**
```
What levels does this project have?
```
```
Show me all door types available in the project.
```
```
How many elements are in this model? Give me statistics by category.
```

**Create elements:**
```
Create a wall from (0,0) to (5000,0) on Level 1.
```
```
Place rooms in all enclosed spaces on Level 1.
```
```
Create a grid system 6x4 at 7200mm spacing.
```

**Modify elements:**
```
Set the Comments parameter to "Reviewed" on element 12345.
```
```
Change all doors of type X to type Y.
```
```
Add prefix "REV-" to all door Marks.
```

**Views and sheets:**
```
Create a floor plan for Level 2.
```
```
Create sheets A101 through A105.
```
```
Export all sheets to PDF.
```

**Model quality:**
```
Check the health of this model.
```
```
Show me all warnings.
```
```
Check for clashes between walls and pipes.
```
```
Which elements in this view are missing tags?
```
```
Show me the largest families by instance count.
```
```
Are there any empty tags to clean up?
```

> **Tip:** For the full list of available commands with example prompts, see [COMMANDS.md](COMMANDS.md).

---

## Everyday Workflow

Every time you want to use Claude with Revit:

1. Open your Revit project
2. Click **Revit MCP Switch** (green = active)
3. Open Claude Desktop and start chatting

That's it. The plugin remembers the Claude Desktop configuration — you only need to set it up once.

---

## Troubleshooting

### I don't see the three buttons in Revit

| Possible cause | Solution |
|---------------|----------|
| ZIP extracted to wrong folder | Check that `mcp-servers-for-revit.addin` is directly inside `%AppData%\Autodesk\Revit\Addins\2025\` |
| Wrong ZIP version | Download the ZIP matching your Revit year |
| DLLs blocked by Windows | Right-click `RevitMCPPlugin.dll` → Properties → check **Unblock** if present. Repeat for other DLLs |
| Source code instead of release | You should see `.dll` files, NOT `.cs` files. Download the ZIP from [Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases), not the source code |

### Claude Desktop doesn't show the hammer icon

1. Make sure Revit is open and the MCP Switch is ON (green)
2. **Fully restart** Claude Desktop (Quit from system tray, then reopen)
3. If still not working, run the fix script:
   - Open PowerShell
   - Run: `powershell -ExecutionPolicy Bypass -File "%AppData%\Autodesk\Revit\Addins\2025\revit_mcp_plugin\Commands\RevitMCPCommandSet\scripts\fix-mcp.ps1"`

### Claude says "connection refused" or tools timeout

- Click **Revit MCP Switch** in Revit to make sure the server is running (indicator should be green)
- Make sure you have a Revit project open (not just the Start screen)

### I had it working before but now it stopped

- Close and reopen Revit
- Click **Revit MCP Switch** to restart the server
- Restart Claude Desktop

---

## Tips for Best Results

- **Be specific** — "Create a 200mm concrete wall from (0,0) to (5000,0) on Level 1" works better than "add a wall"
- **Check types first** — Ask "Show me all available door types" before asking to place a door
- **Use element IDs** — Select elements in Revit, then ask Claude "What do I have selected?" to get their IDs
- **Dry run first** — For bulk operations, add "without applying changes" to preview what would happen
- **Coordinates are in mm** — All positions and dimensions are in millimeters
- **Parameter names depend on language** — In Italian Revit, "Comments" is "Commenti", "Mark" is "Contrassegno", etc.
