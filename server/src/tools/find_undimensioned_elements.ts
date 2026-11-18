import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerFindUndimensionedElementsTool(server: McpServer) {
  server.tool(
    "find_undimensioned_elements",
    "Find elements in the active view (or a specified view) that are not referenced by any dimension. Useful for QA/QC audits to ensure all structural and architectural elements are properly dimensioned.",
    {
      categories: z
        .array(z.string())
        .optional()
        .describe(
          "List of Revit built-in category names to check (e.g., 'OST_Walls', 'OST_Grids', 'OST_Columns'). Defaults to common structural/architectural categories."
        ),
      viewId: z
        .number()
        .optional()
        .describe("View ID to check. Defaults to the active view."),
      limit: z
        .number()
        .optional()
        .describe("Maximum number of undimensioned elements to return (default: 500)"),
    },
    async (args, extra) => {
      const params: Record<string, unknown> = {};
      if (args.categories) params.categories = args.categories;
      if (args.viewId !== undefined) params.viewId = args.viewId;
      if (args.limit !== undefined) params.limit = args.limit;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("find_undimensioned_elements", params);
        });
        return rawToolResponse("find_undimensioned_elements", response);
      } catch (error) {
        return rawToolError(
          "find_undimensioned_elements",
          `Find undimensioned elements failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
