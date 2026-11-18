import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateDoorScheduleByRoomTool(server: McpServer) {
  server.tool(
    "create_door_schedule_by_room",
    "Create a Door Schedule organized by room with family, type, and dimensions",
    {
      name: z
        .string()
        .optional()
        .describe("Schedule name"),
      includeToRoom: z
        .boolean()
        .optional()
        .default(true)
        .describe("Include To Room field. Default: true"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_schedule", {
            preset: "door_by_room",
            categoryName: "OST_Doors",
            name: args.name ?? "Door Schedule by Room",
            includeToRoom: args.includeToRoom ?? true,
          });
        });
        return rawToolResponse("create_door_schedule_by_room", response);
      } catch (error) {
        return rawToolError(
          "create_door_schedule_by_room",
          `Create door schedule by room failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
