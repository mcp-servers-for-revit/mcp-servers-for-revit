import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerListFamilySizesTool(server: McpServer) {
  server.tool(
    "list_family_sizes",
    "List all families in the project with their instance count, type count, and metadata. Helps identify bloated families (too many types), unused families (zero instances), and in-place families that may impact performance.",
    {
      limit: z
        .number()
        .optional()
        .describe("Maximum number of families to return (default: 50)"),
      sortBy: z
        .enum(["instanceCount", "typeCount", "name"])
        .optional()
        .describe(
          "Sort results by: 'instanceCount' (default), 'typeCount', or 'name'"
        ),
      categories: z
        .array(z.string())
        .optional()
        .describe(
          "Filter by category names (e.g., 'Doors', 'Windows', 'OST_Doors'). If omitted, returns all categories."
        ),
    },
    async (args, extra) => {
      const params: Record<string, unknown> = {};
      if (args.limit !== undefined) params.limit = args.limit;
      if (args.sortBy) params.sortBy = args.sortBy;
      if (args.categories) params.categories = args.categories;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("list_family_sizes", params);
        });
        return rawToolResponse("list_family_sizes", response);
      } catch (error) {
        return rawToolError(
          "list_family_sizes",
          `List family sizes failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
