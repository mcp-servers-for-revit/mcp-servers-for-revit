import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateRoomTool(server: McpServer) {
  server.tool(
    "create_room",
    "Create rooms at specified locations with optional parameters.",
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
              .describe("Location point in mm - must be inside enclosed walls."),
            levelId: z
              .number()
              .optional()
              .describe("Revit Level ElementId."),
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

        return rawToolResponse("create_room", response);
      } catch (error) {
        return rawToolError("create_room", `Create room failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
