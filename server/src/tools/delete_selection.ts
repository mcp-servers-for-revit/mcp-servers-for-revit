import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerDeleteSelectionTool(server: McpServer) {
  server.tool(
    "delete_selection",
    "Delete currently selected elements with optional dryRun preview.",
    {
      name: z
        .string()
        .describe("Name of the saved selection to delete"),
    },
    async (args, extra) => {
      const params = {
        name: args.name,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("delete_selection", params);
        }, 30000);

        return rawToolResponse("delete_selection", response);
      } catch (error) {
        return rawToolError("delete_selection", `delete selection failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
