import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreatePlaceholderSheetsTool(server: McpServer) {
  server.tool(
    "create_placeholder_sheets",
    "Create multiple placeholder sheets with sequential numbering.",
    {
      action: z
        .enum(["create", "list", "convert", "delete"])
        .describe("Operation to perform"),
      sheets: z
        .array(
          z.object({
            number: z.string().describe("Sheet number (e.g. 'A-101')"),
            name: z.string().describe("Sheet name (e.g. 'Floor Plan - Level 1')"),
          })
        )
        .optional()
        .describe("Sheets to create (for 'create' action)"),
      sheetIds: z
        .array(z.number())
        .optional()
        .describe("Sheet element IDs (for 'convert' and 'delete' actions)"),
      titleBlockId: z
        .number()
        .optional()
        .describe("Title block type element ID (for 'convert' action)"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_placeholder_sheets", args);
        });
        return rawToolResponse("create_placeholder_sheets", response);
      } catch (error) {
        return rawToolError("create_placeholder_sheets", `Create placeholder sheets failed: ${errorMessage(error)}`);
      }
    }
  );
}
