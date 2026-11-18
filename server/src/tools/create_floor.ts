import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

const pointSchema = z.object({
  x: z.number().describe("X coordinate in mm"),
  y: z.number().describe("Y coordinate in mm"),
});

export function registerCreateFloorTool(server: McpServer) {
  server.tool(
    "create_floor",
    "Create a floor element from boundary points on a level.",
    {
      boundaryPoints: z
        .array(pointSchema)
        .optional()
        .describe("Array of 2D points defining the floor boundary in mm (minimum 3 points)."),
      roomId: z
        .number()
        .optional()
        .describe(
          "Room element ID to derive floor boundary from (alternative to boundaryPoints)"
        ),
      floorTypeName: z
        .string()
        .optional()
        .describe("Floor type name to use. If omitted, uses the first available floor type."),
      levelElevation: z
        .number()
        .optional()
        .describe("Level elevation in mm for the floor."),
      isStructural: z
        .boolean()
        .optional()
        .describe("Whether the floor is structural (default: false)"),
    },
    async (args, extra) => {
      const params = {
        boundaryPoints: args.boundaryPoints ?? [],
        roomId: args.roomId ?? 0,
        floorTypeName: args.floorTypeName ?? "",
        levelElevation: args.levelElevation ?? 0,
        isStructural: args.isStructural ?? false,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_floor", params);
        });

        return rawToolResponse("create_floor", response);
      } catch (error) {
        return rawToolError("create_floor", `Create floor failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
