import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerFindUntaggedElementsTool(server: McpServer) {
  server.tool(
    "find_untagged_elements",
    "Find elements in the active view (or a specified view) that do not have any tags. Useful for QA/QC audits to ensure all required elements are properly tagged before issuing drawings.",
    {
      categories: z
        .array(z.string())
        .optional()
        .describe(
          "List of Revit built-in category names to check (e.g., 'OST_Walls', 'OST_Doors', 'OST_Rooms'). Defaults to common architectural categories."
        ),
      viewId: z
        .number()
        .optional()
        .describe("View ID to check. Defaults to the active view."),
      limit: z
        .number()
        .optional()
        .describe("Maximum number of untagged elements to return (default: 500)"),
    },
    async (args, extra) => {
      const params: Record<string, unknown> = {};
      if (args.categories) params.categories = args.categories;
      if (args.viewId !== undefined) params.viewId = args.viewId;
      if (args.limit !== undefined) params.limit = args.limit;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("find_untagged_elements", params);
        });
        return rawToolResponse("find_untagged_elements", response);
      } catch (error) {
        return rawToolError(
          "find_untagged_elements",
          `Find untagged elements failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
