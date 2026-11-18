import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { storeRoomsBatch, getProjectByName, getRoomsByProjectId } from "../database/service.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

const RoomSchema = z.object({
  room_id: z.string().describe("Unique identifier for the room (Revit Element ID)"),
  room_name: z.string().optional().describe("Room name"),
  room_number: z.string().optional().describe("Room number"),
  department: z.string().optional().describe("Department"),
  level: z.string().optional().describe("Level or floor"),
  area: z.number().optional().describe("Room area"),
  perimeter: z.number().optional().describe("Room perimeter"),
  occupancy: z.string().optional().describe("Occupancy type"),
  comments: z.string().optional().describe("Additional comments"),
  metadata: z.record(z.string()).optional().describe("Additional room metadata as key-value pairs")
});

export function registerStoreRoomDataTool(server: McpServer) {
  server.tool(
    "store_room_data",
    "Store custom key-value data on room elements.",
    {
      project_name: z.string().describe("The name of the Revit project this room belongs to"),
      rooms: z.array(RoomSchema).describe("Array of room data to store")
    },
    async (args) => {
      try {
        // Get or create project
        const project = getProjectByName(args.project_name);

        if (!project) {
          return rawToolError("store_room_data", `Project "${args.project_name}" not found. Please store project data first using store_project_data tool.`);
        }

        // Store rooms
        const count = storeRoomsBatch(project.id, args.rooms);
        const rooms = getRoomsByProjectId(project.id);

        return rawToolResponse("store_room_data", {
          success: true,
          message: `Stored ${count} room(s) successfully`,
          project_id: project.id,
          project_name: args.project_name,
          total_rooms: rooms.length,
          rooms_stored: count
        });
      } catch (error) {
        return rawToolError("store_room_data", `Store room data failed: ${errorMessage(error)}`);
      }
    }
  );
}
