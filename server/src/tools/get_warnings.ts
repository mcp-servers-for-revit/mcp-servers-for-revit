import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions, suggestIf } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerGetWarningsTool(server: McpServer) {
  server.tool(
    "get_warnings",
    "Get all warnings/errors in the current Revit model. Warnings indicate issues like duplicate elements, overlapping geometry, room separation problems, etc. Useful for model health auditing and quality control.\n\nGUIDANCE:\n- Model health check: returns all Revit warnings/errors\n- Quality audit: identify duplicate elements, overlapping geometry, etc.\n- Pre-submission review: fix warnings before model delivery\n\nTIPS:\n- Warnings don't prevent work but indicate potential issues\n- Common warnings: duplicate instances, room not enclosed, overlapping walls\n- Use check_model_health for a comprehensive scored audit\n- Fix high-severity warnings first",
    {
      severityFilter: z
        .enum(["All", "Warning", "Error"])
        .optional()
        .describe("Filter by severity level (default: All)"),
      maxWarnings: z
        .number()
        .optional()
        .describe("Maximum number of warnings to return (default: 500)"),
      categoryFilter: z
        .string()
        .optional()
        .describe(
          "Filter warnings containing this text in the description (case-insensitive)"
        ),
    },
    async (args, extra) => {
      const params = {
        severityFilter: args.severityFilter ?? "All",
        maxWarnings: args.maxWarnings ?? 500,
        categoryFilter: args.categoryFilter ?? "",
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_warnings", params);
        }, 30000);

        const data = typeof response === 'object' ? response : {};
        const count = data.warningCount ?? data.warnings?.length ?? 0;

        const enriched = addSuggestions(response, [
          suggestIf(count > 0, "Isolate the elements with the most warnings in the current view", `${count} warnings need attention`),
          suggestIf(count > 20, "Check model health for an overall score", "Many warnings — a health audit gives the big picture"),
        ]);

        return rawToolResponse("get_warnings", enriched);
      } catch (error) {
        return rawToolError("get_warnings", `Get warnings failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
