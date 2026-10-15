import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerCreateRoomTool(server: McpServer) {
  server.tool(
    "create_room",
    "Create and place rooms in Revit at specified locations. Rooms are placed within enclosed wall boundaries and can be named and numbered. The location point should be inside an enclosed area bounded by walls. All coordinates are in millimeters (mm). The returned area is in m² and perimeter in mm.",
    {
      data: z
        .array(
          z.object({
            name: z
              .string()
              .describe("Room name (e.g., 'Server Room', 'Kitchen', 'Office')"),
            number: z
              .string()
              .optional()
              .describe("Room number (e.g., '101', 'A-01')"),
            location: z
              .object({
                x: z.number().describe("X coordinate in mm (should be inside enclosed walls)"),
                y: z.number().describe("Y coordinate in mm (should be inside enclosed walls)"),
                z: z.number().describe("Z coordinate in mm (typically 0 or level elevation)"),
              })
              .describe(
                "The location point where the room will be placed - must be inside an enclosed area"
              ),
            levelId: z
              .number()
              .optional()
              .describe("Revit Level ElementId. If not provided, uses the nearest level to the Z coordinate"),
            upperLimitId: z
              .number()
              .optional()
              .describe("Upper limit Level ElementId for room height"),
            limitOffset: z
              .number()
              .optional()
              .describe("Offset from upper limit in mm"),
            baseOffset: z
              .number()
              .optional()
              .describe("Offset from base level in mm"),
            department: z
              .string()
              .optional()
              .describe("Department the room belongs to"),
            comments: z
              .string()
              .optional()
              .describe("Additional comments for the room"),
          })
        )
        .describe("Array of rooms to create"),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_room", params);
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
              text: `Create room failed: ${
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
