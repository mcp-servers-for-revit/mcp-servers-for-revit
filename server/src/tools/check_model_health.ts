import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions, suggestIf } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCheckModelHealthTool(server: McpServer) {
  server.tool(
    "check_model_health",
    "Comprehensive BIM model health audit. Returns a health score (0-100), grade (A-F), and detailed breakdown: warnings count and top 10 types, in-place families, imported CAD, unplaced rooms, unused views, detail lines. Includes actionable recommendations. Use this to assess model quality before delivery or identify cleanup priorities.\n\nGUIDANCE:\n- Full audit: call with no parameters for comprehensive model health check\n- Returns health score (0-100), grade (A-F), and specific recommendations\n- Run periodically during model development for quality assurance\n\nTIPS:\n- Score includes: warnings count, unused families, CAD imports, in-place families\n- Use audit_families for detailed family-level health analysis\n- Use get_warnings for the full list of Revit warnings\n- Address high-impact issues (score deductions > 10 points) first",
    {},
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("check_model_health", {});
        }, 300000);
        const data = typeof response === 'object' ? response : {};
        const score = data.healthScore ?? data.score ?? 100;
        const warningCount = data.warningCount ?? data.warnings?.length ?? 0;
        const unusedFamilies = data.unusedFamilies ?? data.unusedFamilyCount ?? 0;

        const enriched = addSuggestions(response, [
          suggestIf(warningCount > 0, `Show me all ${warningCount} model warnings`, `${warningCount} warnings found in the model`),
          suggestIf(unusedFamilies > 0, `Purge ${unusedFamilies} unused families`, `${unusedFamilies} unused families detected`),
          suggestIf(score < 80, "Audit all families for health issues", "Health score below 80 — detailed family audit recommended"),
        ]);

        return rawToolResponse("check_model_health", enriched);
      } catch (error) {
        return rawToolError("check_model_health", `Check model health failed: ${errorMessage(error)}`);
      }
    }
  );
}
