import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerCreateLineBasedElementTool(server: McpServer) {
  server.tool(
    "create_line_based_element",
    "Create one or more line-based elements in Revit such as walls, beams, or pipes. Supports batch creation with detailed parameters including family type ID, start and end points, height, and level (levelId). All units are in millimeters (mm).",
    {
      data: z
        .array(
          z.object({
            category: z
              .string()
              .describe("Revit built-in category (e.g., OST_Walls, OST_StructuralFraming, OST_DuctCurves)"),
            typeId: z
              .number()
              .optional()
              .describe("The ID of the family type to create."),
            locationLine: z
              .object({
                p0: z.object({
                  x: z.number().describe("X coordinate of start point"),
                  y: z.number().describe("Y coordinate of start point"),
                  z: z.number().describe("Z coordinate of start point"),
                }),
                p1: z.object({
                  x: z.number().describe("X coordinate of end point"),
                  y: z.number().describe("Y coordinate of end point"),
                  z: z.number().describe("Z coordinate of end point"),
                }),
              })
              .describe("The line defining the element's location"),
            thickness: z
              .number()
              .optional()
              .describe(
                "Not used: thickness comes from the element type (e.g. the wall type). Pick a typeId with the thickness you need."
              ),
            height: z
              .number()
              .describe("Height of the element in mm (e.g., wall height)"),
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
          })
        )
        .describe("Array of line-based elements to create"),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "create_line_based_element",
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
              text: `Create line-based element failed: ${
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
