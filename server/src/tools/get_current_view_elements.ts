import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerGetCurrentViewElementsTool(server: McpServer) {
  server.tool(
    "get_current_view_elements",
    "Get elements from the current active view in Revit. You can filter by model categories (like Walls, Floors) or annotation categories (like Dimensions, Text). Use includeHidden to show/hide invisible elements and limit to control the number of returned elements.\n\nGUIDANCE:\n- See what's in the active view: call with no parameters\n- Filter by category: use categoryFilter to narrow results (e.g. \"Walls\", \"Doors\")\n- Use before operate_element to find elements to select/hide/isolate\n\nTIPS:\n- Returns element IDs, names, categories — use these IDs with other tools\n- Large views may return many elements — use categoryFilter to limit results\n- Combine with get_element_parameters to inspect specific elements",
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
      const params: Record<string, unknown> = {
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

        return rawToolResponse("get_current_view_elements", response);
      } catch (error) {
        return rawToolError("get_current_view_elements", `get current view elements failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
