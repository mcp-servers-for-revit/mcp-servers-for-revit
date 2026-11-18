import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions, suggestIf } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerWorkflowSheetSetTool(server: McpServer) {
  server.tool(
    "workflow_sheet_set",
    "Auto-create sheets from views with title blocks and viewports.",
    {
      sheets: z.array(z.object({
        number: z.string().describe("Sheet number (e.g. 'A101')."),
        name: z.string().describe("Sheet name/title (e.g. 'Floor Plan - Level 1')."),
      })).describe("Array of sheet definitions with number and name."),
      titleBlockName: z.string().optional()
        .describe("Title block name. Default: first available."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("workflow_sheet_set", {
            sheets: args.sheets,
            titleBlockName: args.titleBlockName ?? "",
          });
        });

        const data = typeof response === 'object' ? (response as any)?.data ?? response : response;
        const sheetsCreated = data?.sheetsCreated ?? 0;
        const enriched = addSuggestions(response, [
          suggestIf(sheetsCreated > 0, "Place views on the new sheets", "Sheets are empty — add viewports to populate them"),
          suggestIf(sheetsCreated > 0, "Align viewports", "Ensure consistent viewport placement across sheets"),
          suggestIf(sheetsCreated > 0, "Export sheets to PDF", "Generate deliverable documents from the new sheet set"),
        ]);

        return rawToolResponse("workflow_sheet_set", enriched);
      } catch (error) {
        return rawToolError("workflow_sheet_set", `Workflow sheet set failed: ${errorMessage(error)}`);
      }
    }
  );
}
