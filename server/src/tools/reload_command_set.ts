import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { invalidate } from "../utils/ToolAvailability.js";

export function registerReloadCommandSetTool(server: McpServer) {
  server.tool(
    "reload_command_set",
    "Live-reload one or all command set dlls in the Revit plugin (no Revit restart needed). Call after overwriting a command set dll on disk; the plugin byte-loads the changed assembly and re-registers its commands. Because the plugin is built with byte loading, overwriting the dll while Revit runs is allowed.",
    {
      assemblyName: z
        .string()
        .optional()
        .describe("Optional file-name filter (e.g. 'DwgCommandSet'). Omit to reload all enabled command sets."),
    },
    async (args, _extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("reload_command_set", {
            assemblyName: args.assemblyName ?? "",
          });
        });
        // The registry may have changed - re-sync the advertised tool list on
        // the next connection (cheap TTL miss) instead of trusting stale state.
        invalidate();
        return {
          content: [{ type: "text", text: JSON.stringify(response, null, 2) }]
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Reload failed: ${
                error instanceof Error ? error.message : String(error)
              }`
            }
          ]
        };
      }
    }
  );
}
