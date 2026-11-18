import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerDeleteWorksetTool(server: McpServer) {
  server.tool(
    "delete_workset",
    "Delete a user workset and move its elements to another workset. Requires user confirmation.",
    {
      worksetName: z.string().describe("Name of the workset to delete"),
      moveToWorksetName: z
        .string()
        .describe(
          "Name of the workset where elements will be moved before deletion"
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("delete_workset", {
            worksetName: args.worksetName,
            moveToWorksetName: args.moveToWorksetName,
          });
        }, 120000);

        return toolResponse("delete_workset", response);
      } catch (error) {
        return toolError(
          "delete_workset",
          `Delete workset failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
