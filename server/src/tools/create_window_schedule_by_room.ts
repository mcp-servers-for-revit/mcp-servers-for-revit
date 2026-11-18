import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateWindowScheduleByRoomTool(server: McpServer) {
  server.tool(
    "create_window_schedule_by_room",
    "Create a Window Schedule organized by room with family, type, dimensions, and sill/head heights",
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
            preset: "window_by_room",
            categoryName: "OST_Windows",
            name: args.name ?? "Window Schedule by Room",
          });
        });
        return rawToolResponse("create_window_schedule_by_room", response);
      } catch (error) {
        return rawToolError(
          "create_window_schedule_by_room",
          `Create window schedule by room failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
