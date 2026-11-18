import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerDuplicateScheduleTool(server: McpServer) {
  server.tool(
    "duplicate_schedule",
    "Duplicate a Revit schedule by ID or name with a new name.",
    {
      scheduleId: z
        .number()
        .optional()
        .describe("Schedule ElementId. Provide either scheduleId or scheduleName."),
      scheduleName: z
        .string()
        .optional()
        .describe("Schedule name (alternative to scheduleId)."),
      newName: z
        .string()
        .describe("Name for the duplicated schedule."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("duplicate_schedule", {
            scheduleId: args.scheduleId ?? 0,
            scheduleName: args.scheduleName ?? "",
            newName: args.newName,
          });
        });
        return rawToolResponse("duplicate_schedule", response);
      } catch (error) {
        return rawToolError(
          "duplicate_schedule",
          `Duplicate schedule failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
