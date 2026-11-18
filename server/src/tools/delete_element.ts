import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerDeleteElementTool(server: McpServer) {
  server.tool(
    "delete_element",
    "Delete elements by ID with optional dryRun preview.",
    {
      elementIds: z
        .array(z.number().int())
        .describe("The IDs of the elements to delete"),
      dryRun: z
        .boolean()
        .optional()
        .describe("If true (default), preview which elements would be deleted without actually deleting them"),
    },
    async (args, extra) => {
      const params = {
        elementIds: args.elementIds,
        dryRun: args.dryRun ?? true,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("delete_element", params);
        });

        return rawToolResponse("delete_element", response);
      } catch (error) {
        return rawToolError("delete_element", `delete element failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
