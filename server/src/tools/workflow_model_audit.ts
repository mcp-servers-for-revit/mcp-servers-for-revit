import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions, suggestIf } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerWorkflowModelAuditTool(server: McpServer) {
  server.tool(
    "workflow_model_audit",
    "Run a comprehensive model health audit with scoring.",
    {
      includeWarnings: z.boolean().optional().default(true)
        .describe("Include warning details in the report."),
      includeFamilies: z.boolean().optional().default(true)
        .describe("Include unused family type detection (slower on large models)."),
      maxWarnings: z.number().optional().default(50)
        .describe("Maximum warnings to include in detail."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("workflow_model_audit", {
            includeWarnings: args.includeWarnings ?? true,
            includeFamilies: args.includeFamilies ?? true,
            maxWarnings: args.maxWarnings ?? 50,
          });
        }, 300000);

        const data = typeof response === 'object' ? (response as any)?.data ?? response : response;
        const enriched = addSuggestions(response, [
          suggestIf((data?.unusedFamilyTypeCount ?? 0) > 0, "Purge unused families and types", "Clean up detected unused items"),
          suggestIf((data?.cadImportCount ?? 0) > 0, "Clean up CAD imports", "Remove unnecessary CAD files"),
          suggestIf((data?.healthScore ?? 100) < 70, "Export model warnings to review offline", "Score below 70 needs detailed attention"),
        ]);

        return rawToolResponse("workflow_model_audit", enriched);
      } catch (error) {
        return rawToolError("workflow_model_audit", `Workflow model audit failed: ${errorMessage(error)}`);
      }
    }
  );
}
