import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerGetSelectedElementsTool(server: McpServer) {
  server.tool(
    "get_selected_elements",
    "Get elements currently selected in Revit, with their family, type, level, and location (in mm). You can limit the number of returned elements.",
    {
      limit: z
        .number()
        .optional()
        .describe("Maximum number of elements to return"),
    },
    async (args, extra) => {
      const params = {
        limit: args.limit || 100,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_selected_elements", params);
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
              text: `get selected elements failed: ${
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
