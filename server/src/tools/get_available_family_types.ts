import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerGetAvailableFamilyTypesTool(server: McpServer) {
  server.tool(
    "get_available_family_types",
    "Get available family types in the current Revit project. You can filter by category and family name, and limit the number of returned types.",
    {
      categoryList: z
        .array(z.string())
        .optional()
        .describe(
          "List of Revit category names to filter by (e.g., 'OST_Walls', 'OST_Doors', 'OST_Furniture')"
        ),
      familyNameFilter: z
        .string()
        .optional()
        .describe("Filter family types by family name (partial match)"),
      limit: z
        .number()
        .optional()
        .describe("Maximum number of family types to return"),
    },
    async (args, extra) => {
      const params = {
        categoryList: args.categoryList || [],
        familyNameFilter: args.familyNameFilter || "",
        limit: args.limit || 100,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "get_available_family_types",
            params
          );
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
              text: `get available family types failed: ${
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
