import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateSheetListScheduleTool(server: McpServer) {
  server.tool(
    "create_sheet_list_schedule",
    "Create a Sheet List schedule with sheet numbers, names, and revision info",
    {
      name: z
        .string()
        .optional()
        .describe("Schedule name"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_schedule", {
            preset: "sheet_index",
            type: "sheet_list",
            name: args.name ?? "Sheet List",
          });
        });
        return rawToolResponse("create_sheet_list_schedule", response);
      } catch (error) {
        return rawToolError(
          "create_sheet_list_schedule",
          `Create sheet list schedule failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
