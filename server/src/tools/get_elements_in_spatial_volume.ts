import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerGetElementsInSpatialVolumeTool(server: McpServer) {
  server.tool(
    "get_elements_in_spatial_volume",
    "Find all elements contained within spatial volumes: rooms, areas, or a custom bounding box. Returns elements grouped by volume with category, family, and type info. Use categoryFilter to narrow results.\n\nGUIDANCE:\n- Elements in a room: volumeType=\"room\", volumeIds=[roomId]\n- Elements in area: volumeType=\"area\", volumeIds=[areaId]\n- Custom bounding box: volumeType=\"custom\", provide min/max XYZ in mm\n- Filter by category: categoryFilter=[\"Furniture\",\"Doors\"] to limit results\n\nTIPS:\n- Great for room-based quantity takeoffs\n- Use export_room_data to get room IDs first\n- CategoryFilter narrows results to specific element types\n- Combine with export_elements_data for detailed parameter extraction",
    {
      volumeIds: z.array(z.number()).optional().describe("IDs of rooms/areas to search within. If omitted, searches all rooms/areas."),
      volumeType: z.enum(["room", "area", "custom"]).optional().describe("Type of volume: room, area, or custom bounding box. Default: room."),
      categoryFilter: z.array(z.string()).optional().describe("Filter elements by category names (e.g. ['Doors', 'Windows', 'Furniture'])."),
      customMinX: z.number().optional().describe("Min X in mm (for custom bounding box)."),
      customMinY: z.number().optional().describe("Min Y in mm (for custom bounding box)."),
      customMinZ: z.number().optional().describe("Min Z in mm (for custom bounding box)."),
      customMaxX: z.number().optional().describe("Max X in mm (for custom bounding box)."),
      customMaxY: z.number().optional().describe("Max Y in mm (for custom bounding box)."),
      customMaxZ: z.number().optional().describe("Max Z in mm (for custom bounding box)."),
      maxElementsPerVolume: z.number().optional().default(100)
        .describe("Max elements returned per room/area/volume. Default 100."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_elements_in_spatial_volume", {
            volumeIds: args.volumeIds ?? [],
            volumeType: args.volumeType ?? "room",
            categoryFilter: args.categoryFilter ?? [],
            maxElementsPerVolume: args.maxElementsPerVolume ?? 100,
            customMinX: args.customMinX ?? 0,
            customMinY: args.customMinY ?? 0,
            customMinZ: args.customMinZ ?? 0,
            customMaxX: args.customMaxX ?? 0,
            customMaxY: args.customMaxY ?? 0,
            customMaxZ: args.customMaxZ ?? 0,
          });
        });
        return rawToolResponse("get_elements_in_spatial_volume", response);
      } catch (error) {
        return rawToolError("get_elements_in_spatial_volume", `Get elements in spatial volume failed: ${errorMessage(error)}`);
      }
    }
  );
}
