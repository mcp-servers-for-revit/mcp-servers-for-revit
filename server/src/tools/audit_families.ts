import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions, suggestIf } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerAuditFamiliesTool(server: McpServer) {
  server.tool(
    "audit_families",
    "Comprehensive family audit: health score, unused families, in-place families, instance counts per family/type, category breakdown, and cleanup recommendations.\n\nGUIDANCE:\n- Full audit: call with no parameters for comprehensive family analysis\n- Filter by category: categoryFilter=\"Doors\" to audit only specific category\n- Include unused: includeUnused=true (default) shows families with zero instances\n\nTIPS:\n- Health score factors: unused families, in-place families, CAD imports\n- Recommendations include specific cleanup actions\n- Use purge_unused to remove families flagged as unused\n- Category breakdown shows which areas need most attention",
    {
      includeUnused: z.boolean().optional().describe("Include unused families in results. Default: true."),
      categoryFilter: z.string().optional().describe("Filter families by category name (e.g. 'Doors'). If omitted, audits all categories."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("audit_families", {
            includeUnused: args.includeUnused ?? true,
            categoryFilter: args.categoryFilter ?? "",
          });
        });

        const data = typeof response === 'object' ? response : {};
        const unusedCount = data.unusedFamilyCount ?? data.unusedFamilies?.length ?? 0;
        const issueCount = data.issueCount ?? data.issues?.length ?? 0;

        const enriched = addSuggestions(response, [
          suggestIf(unusedCount > 0, `Purge ${unusedCount} unused families to reduce file size`, `${unusedCount} unused families are bloating the model`),
          suggestIf(issueCount > 0, "Rename families that don't follow naming conventions", `${issueCount} families have naming or health issues`),
        ]);

        return rawToolResponse("audit_families", enriched);
      } catch (error) {
        return rawToolError("audit_families", `Audit families failed: ${errorMessage(error)}`);
      }
    }
  );
}
