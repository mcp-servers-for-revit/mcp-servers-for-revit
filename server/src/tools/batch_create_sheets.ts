import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions, suggestIf } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerBatchCreateSheetsTool(server: McpServer) {
  server.tool(
    "batch_create_sheets",
    "Create multiple sheets at once with title blocks and optional view placement. Each sheet can have its own number, name, title block, and views to place.\n\nGUIDANCE:\n- Create multiple sheets: provide array of {sheetNumber, sheetName, titleBlockName}\n- Auto-sequence: use a numbering pattern like A101, A102, A103\n- With views: optionally specify viewIds to auto-place on each sheet\n\nTIPS:\n- Sheet numbers must be unique — check existing sheets first\n- Use get_available_family_types with \"Title Blocks\" to find title block names\n- Use place_viewport after creation to position views on sheets\n- Use align_viewports to align view positions across multiple sheets",
    {
      sheets: z
        .array(
          z.object({
            number: z.string().describe("Sheet number (e.g. 'A-101')"),
            name: z.string().describe("Sheet name (e.g. 'Floor Plan - Level 1')"),
            titleBlockName: z
              .string()
              .optional()
              .describe("Title block family type name for this sheet"),
            viewIds: z
              .array(z.number())
              .optional()
              .describe("View IDs to place on this sheet"),
          })
        )
        .describe("Array of sheet definitions to create"),
      defaultTitleBlockName: z
        .string()
        .optional()
        .describe("Default title block for sheets that don't specify one"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("batch_create_sheets", args);
        });

        const data = typeof response === 'object' ? response : {};
        const sheetsCreated = data.sheetsCreated ?? data.createdSheets?.length ?? data.count ?? 0;

        const enriched = addSuggestions(response, [
          suggestIf(sheetsCreated > 0, "Place views on the new sheets", "Sheets are empty — place viewports to populate them"),
          suggestIf(sheetsCreated > 0, "Align viewports across the new sheets", "Consistent viewport placement improves drawing set quality"),
        ]);

        return rawToolResponse("batch_create_sheets", enriched);
      } catch (error) {
        return rawToolError("batch_create_sheets", `Batch create sheets failed: ${errorMessage(error)}`);
      }
    }
  );
}
