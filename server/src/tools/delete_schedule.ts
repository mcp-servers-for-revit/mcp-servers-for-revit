import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerDeleteScheduleTool(server: McpServer) {
  server.tool(
    "delete_schedule",
    "Delete a Revit schedule by ID or name. Requires confirm=true to proceed.",
    {
      scheduleId: z
        .number()
        .optional()
        .describe("Schedule ElementId. Provide either scheduleId or scheduleName."),
      scheduleName: z
        .string()
        .optional()
        .describe("Schedule name (alternative to scheduleId)."),
      confirm: z
        .boolean()
        .describe("Must be true to confirm deletion. Safety check to prevent accidental deletions."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("delete_schedule", {
            scheduleId: args.scheduleId ?? 0,
            scheduleName: args.scheduleName ?? "",
            confirm: args.confirm,
          });
        });
        return rawToolResponse("delete_schedule", response);
      } catch (error) {
        return rawToolError(
          "delete_schedule",
          `Delete schedule failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
