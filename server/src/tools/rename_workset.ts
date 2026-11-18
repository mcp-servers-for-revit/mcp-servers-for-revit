import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerRenameWorksetTool(server: McpServer) {
  server.tool(
    "rename_workset",
    "Rename an existing user workset.",
    {
      currentName: z.string().describe("Current name of the workset to rename"),
      newName: z
        .string()
        .describe(
          "New name for the workset. Cannot contain { } [ ] | ; characters."
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("rename_workset", {
            currentName: args.currentName,
            newName: args.newName,
          });
        }, 120000);

        return toolResponse("rename_workset", response);
      } catch (error) {
        return toolError(
          "rename_workset",
          `Rename workset failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
