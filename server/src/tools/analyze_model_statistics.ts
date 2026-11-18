import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerAnalyzeModelStatisticsTool(server: McpServer) {
  server.tool(
    "analyze_model_statistics",
    "Analyze model complexity with element counts. Returns detailed statistics about the Revit model including total element counts, total types, total families, views, sheets, counts by category (with type/family breakdown), and level-by-level element distribution. Useful for model auditing, performance analysis, and understanding model composition.\n\nGUIDANCE:\n- Model overview: element counts by category, type, family, and level\n- Quality check: identify categories with unexpected element counts\n- Pre-audit: run before check_model_health for a quick complexity assessment\n\nTIPS:\n- Useful for understanding model scope before detailed operations\n- Compare counts across levels to check consistency\n- Large element counts may indicate modeling issues",
    {
      includeDetailedTypes: z
        .boolean()
        .optional()
        .default(true)
        .describe("Whether to include detailed breakdown by family and type within each category. Defaults to true."),
      compact: z
        .boolean()
        .optional()
        .default(false)
        .describe("Return summary counts only, without full data arrays. Saves tokens for large results."),
    },
    async (args, extra) => {
      const params = {
        includeDetailedTypes: args.includeDetailedTypes ?? true,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("analyze_model_statistics", params);
        }, 300000);

        return toolResponse("analyze_model_statistics", response, args);
      } catch (error) {
        return toolError("analyze_model_statistics", `Analyze model statistics failed: ${errorMessage(error)}`);
      }
    }
  );
}
