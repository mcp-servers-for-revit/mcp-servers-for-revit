import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerExportRoomDataTool(server: McpServer) {
  server.tool(
    "export_room_data",
    "Export all room data from the current Revit project. Returns detailed information about each room including name, number, level, area, volume, perimeter, department, and more. Areas are in m², volumes in m³, and perimeter and height in mm. Volume is 0 unless the project computes room volumes (Area and Volume Computations → Areas and Volumes). Useful for generating room schedules, space analysis, and facility management data.",
    {
      includeUnplacedRooms: z
        .boolean()
        .optional()
        .default(false)
        .describe("Whether to include unplaced rooms (rooms not yet placed in the model). Defaults to false."),
      includeNotEnclosedRooms: z
        .boolean()
        .optional()
        .default(false)
        .describe("Whether to include rooms that are not fully enclosed. Defaults to false."),
    },
    async (args, extra) => {
      const params = {
        includeUnplacedRooms: args.includeUnplacedRooms ?? false,
        includeNotEnclosedRooms: args.includeNotEnclosedRooms ?? false,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("export_room_data", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
          isError: reportsFailure(response),
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Export room data failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
