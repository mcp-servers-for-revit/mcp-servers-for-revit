import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateLineBasedElementTool(server: McpServer) {
  server.tool(
    "create_line_based_element",
    "Create line-based elements (walls, beams, pipes, ducts, etc.).",
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
              .describe(
                "Thickness/width of the element (e.g., wall thickness)"
              ),
            height: z
              .number()
              .describe("Height of the element (e.g., wall height)"),
            baseLevel: z.number().describe("Base level height"),
            baseOffset: z.number().describe("Offset from the base level"),
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

        return rawToolResponse("create_line_based_element", response);
      } catch (error) {
        return rawToolError("create_line_based_element", `Create line-based element failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
