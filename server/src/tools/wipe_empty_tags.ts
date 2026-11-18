import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerWipeEmptyTagsTool(server: McpServer) {
  server.tool(
    "wipe_empty_tags",
    "Find and remove tags that have empty text or reference deleted/invalid elements. DESTRUCTIVE: defaults to dryRun=true (preview only). Set dryRun=false to actually delete empty tags.",
    {
      dryRun: z
        .boolean()
        .optional()
        .describe(
          "If true (default), only reports empty tags without deleting them. Set to false to actually delete."
        ),
      viewId: z
        .number()
        .optional()
        .describe(
          "Limit scope to a specific view. If omitted, scans the entire document."
        ),
      categories: z
        .array(z.string())
        .optional()
        .describe(
          "Filter by tag category names (e.g., 'Wall Tags', 'Door Tags'). If omitted, checks all tag categories."
        ),
    },
    async (args, extra) => {
      const params: Record<string, unknown> = {
        dryRun: args.dryRun ?? true,
      };
      if (args.viewId !== undefined) params.viewId = args.viewId;
      if (args.categories) params.categories = args.categories;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("wipe_empty_tags", params);
        });
        return rawToolResponse("wipe_empty_tags", response);
      } catch (error) {
        return rawToolError(
          "wipe_empty_tags",
          `Wipe empty tags failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
