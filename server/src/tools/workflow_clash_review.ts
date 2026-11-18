import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions, suggestIf } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerWorkflowClashReviewTool(server: McpServer) {
  server.tool(
    "workflow_clash_review",
    "Detect clashes between two categories and visualize results.",
    {
      categoryA: z.string()
        .describe("First category (e.g. 'Walls', 'Pipes')."),
      categoryB: z.string()
        .describe("Second Revit category name to check (e.g. 'Ducts', 'Mechanical Equipment')."),
      tolerance: z.number().optional().default(0)
        .describe("Tolerance in mm. Default: 0."),
      createSectionBox: z.boolean().optional().default(true)
        .describe("Create section box around clashes. Default: true."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("workflow_clash_review", {
            categoryA: args.categoryA,
            categoryB: args.categoryB,
            tolerance: args.tolerance ?? 0,
            createSectionBox: args.createSectionBox ?? true,
          });
        });

        const data = typeof response === 'object' ? response as any : {};
        const clashCount = data?.clashCount ?? 0;
        const enriched = addSuggestions(response, [
          suggestIf(clashCount > 0, "Export clashing elements to Excel", "Review and document detected clashes offline"),
          suggestIf(clashCount > 0, `Color the ${clashCount} clashing elements red for visibility`, "Highlight clashes visually in the model"),
          suggestIf(clashCount === 0, "Run a full model health audit", "No clashes found — verify overall model quality"),
        ]);

        return rawToolResponse("workflow_clash_review", enriched);
      } catch (error) {
        return rawToolError("workflow_clash_review", `Workflow clash review failed: ${errorMessage(error)}`);
      }
    }
  );
}
