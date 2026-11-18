# mcp-server-for-revit

MCP server for interacting with Autodesk Revit through AI assistants like Claude.

This package is the MCP server component of [mcp-servers-for-revit](https://github.com/LuDattilo/revit-mcp-server). It exposes Revit operations as MCP tools that AI clients can call. The server communicates with the [Revit plugin](https://github.com/LuDattilo/revit-mcp-server) over TCP to execute commands inside Revit.

> [!NOTE]
> This server requires the mcp-servers-for-revit Revit plugin to be installed and running inside Revit. See the [full project README](https://github.com/LuDattilo/revit-mcp-server) for setup instructions.

## Setup

**Claude Code**

```bash
claude mcp add mcp-server-for-revit -- npx -y mcp-server-for-revit
```

**Claude Desktop**

Claude Desktop → Settings → Developer → Edit Config → `claude_desktop_config.json`:

```json
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "npx",
            "args": ["-y", "mcp-server-for-revit"]
        }
    }
}
```

Restart Claude Desktop. When you see the hammer icon, the MCP server is connected.

## Supported Tools (124)

See the [full tool list](https://github.com/LuDattilo/revit-mcp-server#supported-tools-124) in the main README for the complete catalog organized by category:

- **Project & Model Info** — project metadata, views, parameters, phases, worksets, links
- **Model Analysis** — AI element filter, health check, clash detection, measurements
- **Materials & Quantities** — material properties, takeoffs, quantities
- **Element Creation** — walls, doors, floors, grids, levels, arrays, structural framing
- **Element Modification** — move, rotate, copy, type swap, parameter writes, phase/workset
- **Views & Sheets** — create views, filters, templates, sheets, viewports, schedules
- **Annotation** — dimensions, text notes, filled regions, auto-tagging
- **Data Export** — room data, element data (JSON/CSV), schedule export, PDF/DWG/IFC
- **Batch Operations** — rename, renumber, purge, CAD cleanup, shared parameters
- **Advanced** — execute C# code, local data storage

## Development

```bash
npm install
npm run build
```

## License

[MIT](https://github.com/LuDattilo/revit-mcp-server/blob/main/LICENSE)
