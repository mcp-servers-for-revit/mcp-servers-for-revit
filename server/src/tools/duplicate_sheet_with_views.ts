import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerDuplicateSheetWithViewsTool(server: McpServer) {
  server.tool(
    "duplicate_sheet_with_views",
    "Duplicate a sheet and its placed views onto a new sheet.",
    {
      sheetId: z.number().describe("ElementId of the source sheet to duplicate."),
      copies: z.number().optional().default(1).describe("Number of copies to create. Default: 1."),
      duplicateViews: z.boolean().optional().default(true).describe("Duplicate placed views (not just reference them). Default: true."),
      keepLegends: z.boolean().optional().default(true).describe("Keep legend views on new sheets. Default: true."),
      keepSchedules: z.boolean().optional().default(true).describe("Keep schedules on new sheets. Default: true."),
      newSheetNumberPrefix: z.string().optional().describe("Prefix for new sheet numbers."),
      viewDuplicateOption: z.enum(["Duplicate", "DuplicateWithDetailing", "DuplicateAsDependent"]).optional().default("DuplicateWithDetailing").describe("Duplicate, DuplicateWithDetailing, or DuplicateAsDependent."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("duplicate_sheet_with_views", {
            sheetId: args.sheetId,
            copies: args.copies ?? 1,
            duplicateViews: args.duplicateViews ?? true,
            keepLegends: args.keepLegends ?? true,
            keepSchedules: args.keepSchedules ?? true,
            newSheetNumberPrefix: args.newSheetNumberPrefix ?? "",
            viewDuplicateOption: args.viewDuplicateOption ?? "DuplicateWithDetailing",
          });
        });
        return rawToolResponse("duplicate_sheet_with_views", response);
      } catch (error) {
        return rawToolError("duplicate_sheet_with_views", `Duplicate sheet with views failed: ${errorMessage(error)}`);
      }
    }
  );
}
