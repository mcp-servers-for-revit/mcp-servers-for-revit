import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerGetCurrentViewElementsTool(server: McpServer) {
  server.tool(
    "get_current_view_elements",
    "Get elements from the current active view in Revit. You can filter by model categories (like Walls, Floors) or annotation categories (like Dimensions, Text). Use includeHidden to show/hide invisible elements and limit to control the number of returned elements. Each element includes its family, type, level, and location (in mm).",
    {
      modelCategoryList: z
        .array(z.string())
        .optional()
        .describe(
          "List of Revit model category names (e.g., 'OST_Walls', 'OST_Doors', 'OST_Floors')"
        ),
      annotationCategoryList: z
        .array(z.string())
        .optional()
        .describe(
          "List of Revit annotation category names (e.g., 'OST_Dimensions', 'OST_WallTags', 'OST_TextNotes')"
        ),
      includeHidden: z
        .boolean()
        .optional()
        .describe("Whether to include hidden elements in the results"),
      limit: z
        .number()
        .optional()
        .describe("Maximum number of elements to return"),
    },
    async (args, extra) => {
      const params = {
        modelCategoryList: args.modelCategoryList || [],
        annotationCategoryList: args.annotationCategoryList || [],
        includeHidden: args.includeHidden || false,
        limit: args.limit || 100,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "get_current_view_elements",
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
              text: `get current view elements failed: ${
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
