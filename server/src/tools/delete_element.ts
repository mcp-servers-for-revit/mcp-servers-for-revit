import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerDeleteElementTool(server: McpServer) {
  server.tool(
    "delete_element",
    "Delete one or more elements from the Revit model by their element IDs. Revit also deletes elements that depend on them (e.g. doors hosted in a deleted wall); deletedIds lists everything removed, and skippedIds the requested IDs that didn't exist.",
    {
      elementIds: z
        .array(z.number())
        .describe("The IDs of the elements to delete"),
    },
    async (args, extra) => {
      const params = {
        elementIds: args.elementIds,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("delete_element", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
          isError: reportsFailure(response),
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `delete element failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
