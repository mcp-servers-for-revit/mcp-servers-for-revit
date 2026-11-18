import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateElevationsFromRoomsTool(server: McpServer) {
  server.tool(
    "create_elevations_from_rooms",
    "Create interior elevation views for rooms (N/S/E/W).",
    {
      roomIds: z.array(z.number()).optional().describe("Specific room IDs to create elevations for."),
      viewType: z.enum(["elevation", "section"]).optional().describe("Type of view to create. Default: elevation."),
      directions: z.array(z.enum(["north", "south", "east", "west"])).optional().describe("Which directions to create elevations for. Default: all four directions."),
      scale: z.number().optional().describe("View scale (e.g. 50 for 1:50). Default: 50."),
      offset: z.number().optional().describe("Offset from room boundary in mm. Default: 300."),
      viewTemplateId: z.number().optional().describe("Element ID of view template to apply."),
      namingPattern: z.string().optional().describe("Name pattern: {RoomName}, {RoomNumber}, {Direction}, {Level}."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_elevations_from_rooms", {
            roomIds: args.roomIds ?? [],
            viewType: args.viewType ?? "elevation",
            directions: args.directions ?? ["north", "south", "east", "west"],
            scale: args.scale ?? 50,
            offset: args.offset ?? 300,
            viewTemplateId: args.viewTemplateId ?? -1,
            namingPattern: args.namingPattern ?? "{RoomName} - {Direction}",
          });
        });
        return rawToolResponse("create_elevations_from_rooms", response);
      } catch (error) {
        return rawToolError("create_elevations_from_rooms", `Create elevations from rooms failed: ${errorMessage(error)}`);
      }
    }
  );
}
