import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerLinesPerViewCountTool(server: McpServer) {
  server.tool(
    "lines_per_view_count",
    "Count detail lines and model lines per view across the project. Helps identify views with excessive line work that may impact performance. Results sorted by total line count descending.",
    {
      threshold: z
        .number()
        .optional()
        .describe(
          "Only return views with total lines >= this threshold (default: 0, returns all views)"
        ),
      includeDetailLines: z
        .boolean()
        .optional()
        .describe("Include detail lines in the count (default: true)"),
      includeModelLines: z
        .boolean()
        .optional()
        .describe("Include model lines in the count (default: true)"),
      limit: z
        .number()
        .optional()
        .describe("Maximum number of views to return (default: 200)"),
    },
    async (args, extra) => {
      const params: Record<string, unknown> = {};
      if (args.threshold !== undefined) params.threshold = args.threshold;
      if (args.includeDetailLines !== undefined)
        params.includeDetailLines = args.includeDetailLines;
      if (args.includeModelLines !== undefined)
        params.includeModelLines = args.includeModelLines;
      if (args.limit !== undefined) params.limit = args.limit;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("lines_per_view_count", params);
        });
        return rawToolResponse("lines_per_view_count", response);
      } catch (error) {
        return rawToolError(
          "lines_per_view_count",
          `Lines per view count failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
