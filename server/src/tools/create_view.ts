import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateViewTool(server: McpServer) {
  server.tool(
    "create_view",
    "Create floor plan, ceiling plan, section, elevation, or 3D views.",
    {
      viewType: z
        .enum(["FloorPlan", "CeilingPlan", "Section", "Elevation", "3D"])
        .describe("Type of view to create"),
      name: z.string().optional().describe("Name for the new view"),
      levelElevation: z
        .number()
        .optional()
        .describe(
          "Level elevation in mm (for plan views, or origin Z for sections)"
        ),
      scale: z
        .number()
        .optional()
        .describe("View scale (e.g. 100 for 1:100). Default: 100"),
      detailLevel: z
        .enum(["Coarse", "Medium", "Fine"])
        .optional()
        .describe("Detail level. Default: Medium"),
      direction: z
        .object({
          x: z.number(),
          y: z.number(),
          z: z.number(),
        })
        .optional()
        .describe("Direction vector for Section/Elevation views."),
      viewFamilyTypeName: z
        .string()
        .optional()
        .describe("Specific view family type name to use"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_view", args);
        });

        return rawToolResponse("create_view", response);
      } catch (error) {
        return rawToolError("create_view", `Create view failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
