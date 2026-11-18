import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateRoomFinishScheduleTool(server: McpServer) {
  server.tool(
    "create_room_finish_schedule",
    "Create a Room Finish Schedule with Number, Name, Level, Area, and finish fields",
    {
      name: z
        .string()
        .optional()
        .describe("Schedule name"),
      levelFilter: z
        .string()
        .optional()
        .describe("Level name to filter by"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_schedule", {
            preset: "room_finish",
            categoryName: "OST_Rooms",
            name: args.name ?? "Room Finish Schedule",
            levelFilter: args.levelFilter,
          });
        });
        return rawToolResponse("create_room_finish_schedule", response);
      } catch (error) {
        return rawToolError(
          "create_room_finish_schedule",
          `Create room finish schedule failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
