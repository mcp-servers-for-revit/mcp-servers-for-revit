#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { registerTools } from "./tools/register.js";
import { withRevitConnection } from "./utils/ConnectionManager.js";
import * as ToolAvailability from "./utils/ToolAvailability.js";

// Create a server instance
const server = new McpServer({
  name: "mcp-server-for-revit",
  version: "1.0.0",
});

// Start the server
async function main() {
  // Register tools
  const toolHandles = await registerTools(server);
  ToolAvailability.init(server, toolHandles);

  // Connect to the transport layer
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error("Revit MCP Server start success");

  // Dynamic tool advertisement: if Revit is already online, fold its
  // registered-command list into the advertised tools shortly after startup.
  // Fire-and-forget - while Revit is unreachable the full tool list stays
  // advertised (stdio client UX must not depend on plugin connectivity).
  withRevitConnection(async () => {})
    .then(() => console.error("Tool advertisement synced with Revit"))
    .catch(() =>
      console.error("Revit not reachable at startup; advertising all tools")
    );
}

main().catch((error) => {
  console.error("Error starting Revit MCP Server:", error);
  process.exit(1);
});