import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerCreatePointBasedElementTool(server: McpServer) {
  server.tool(
    "create_point_based_element",
    "Create one or more point-based elements in Revit such as doors, windows, or furniture. Supports batch creation with detailed parameters including family type ID, position, dimensions, and level information. All units are in millimeters (mm).",
    {
      data: z
        .array(
          z.object({
            name: z
              .string()
              .describe("Description of the element (e.g., door, window)"),
            category: z
              .string()
              .optional()
              .describe(
                "Revit built-in category (e.g., OST_Doors, OST_Windows, OST_Furniture). Required when typeId isn't given; the first type of this category is used."
              ),
            typeId: z
              .number()
              .optional()
              .describe("The ID of the family type to create."),
            locationPoint: z
              .object({
                x: z.number().describe("X coordinate"),
                y: z.number().describe("Y coordinate"),
                z: z.number().describe("Z coordinate"),
              })
              .describe(
                "The position coordinates where the element will be placed"
              ),
            width: z.number().describe("Width of the element in mm"),
            depth: z.number().optional().describe("Depth of the element in mm"),
            height: z.number().describe("Height of the element in mm"),
            levelId: z
              .number()
              .optional()
              .describe(
                "ID of the level to place the element on (e.g. from ai_element_filter with filterCategory OST_Levels). Preferred over baseLevel."
              ),
            baseLevel: z
              .number()
              .optional()
              .describe(
                "Only used when levelId isn't given: an elevation in mm, and the nearest level is used"
              ),
            baseOffset: z
              .number()
              .optional()
              .describe("Offset in mm above the level (default 0)"),
            rotation: z
              .number()
              .optional()
              .describe("Rotation angle in degrees (0-360)"),
            hostWallId: z
              .number()
              .optional()
              .describe(
                "The ElementId of a specific wall to use as host for doors/windows. " +
                "If not provided, the nearest wall will be auto-detected."
              ),
            facingFlipped: z
              .boolean()
              .optional()
              .default(false)
              .describe(
                "Whether to flip the facing direction of the door/window. " +
                "When true, the element faces the opposite side of the wall."
              ),
          })
        )
        .describe("Array of point-based elements to create"),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "create_point_based_element",
            params
          );
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
              text: `Create point-based element failed: ${
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
