import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateSurfaceBasedElementTool(server: McpServer) {
  server.tool(
    "create_surface_based_element",
    "Create surface-based elements (floors, ceilings, roofs) from points.",
    {
      data: z
        .array(
          z.object({
            name: z
              .string()
              .describe("Description of the element (e.g., floor, ceiling)"),
            category: z
              .enum(["OST_Floors", "OST_Ceilings", "OST_Roofs"])
              .optional()
              .describe("The Revit built-in category for the element."),
            typeId: z
              .number()
              .optional()
              .describe("The ID of the family type to create."),
            boundary: z
              .object({
                outerLoop: z
                  .array(
                    z.object({
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
                  )
                  .min(3)
                  .describe("Array of line segments defining the boundary"),
              })
              .describe("Boundary definition with outer loop"),
            thickness: z.number().describe("Thickness of the element"),
            baseLevel: z.number().describe("Base level height"),
            baseOffset: z.number().describe("Offset from the base level"),
          })
        )
        .describe("Array of surface-based elements to create"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "create_surface_based_element",
            params
          );
        });

        return rawToolResponse("create_surface_based_element", response);
      } catch (error) {
        return rawToolError("create_surface_based_element", `Create surface-based element failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
