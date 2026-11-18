import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateStructuralFramingSystemTool(server: McpServer) {
  server.tool(
    "create_structural_framing_system",
    "Create a beam system from boundary lines on a level.",
    {
      levelName: z
        .string()
        .describe("Level name (e.g. 'Level 1')."),
      xMin: z
        .number()
        .describe("Minimum X coordinate of the rectangular boundary in millimeters"),
      xMax: z
        .number()
        .describe("Maximum X coordinate of the rectangular boundary in millimeters"),
      yMin: z
        .number()
        .describe("Minimum Y coordinate of the rectangular boundary in millimeters"),
      yMax: z
        .number()
        .describe("Maximum Y coordinate of the rectangular boundary in millimeters"),
      spacing: z
        .number()
        .positive()
        .describe("Spacing between beams in millimeters"),
      directionEdge: z
        .enum(["bottom", "right", "top", "left"])
        .default("bottom")
        .describe("Which edge defines the beam direction."),
      layoutRule: z
        .enum(["fixed_distance"])
        .default("fixed_distance")
        .describe(
          "Layout rule type. Currently only 'fixed_distance' is supported."
        ),
      justify: z
        .enum(["beginning", "center", "end", "directionline"])
        .default("center")
        .describe("Beam justification within the layout."),
      beamTypeName: z
        .string()
        .optional()
        .describe("Beam family type name (e.g. 'W10x12')."),
      elevation: z
        .number()
        .default(0)
        .describe("Elevation offset from the level in millimeters."),
      is3d: z
        .boolean()
        .default(false)
        .describe("Whether to create a 3D beam system."),
    },
    async (args, extra) => {
      const params = {
        levelName: args.levelName,
        xMin: args.xMin,
        xMax: args.xMax,
        yMin: args.yMin,
        yMax: args.yMax,
        spacing: args.spacing,
        directionEdge: args.directionEdge,
        layoutRule: args.layoutRule,
        justify: args.justify,
        beamTypeName: args.beamTypeName,
        elevation: args.elevation,
        is3d: args.is3d,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "create_structural_framing_system",
            params
          );
        });

        return rawToolResponse("create_structural_framing_system", response);
      } catch (error) {
        return rawToolError("create_structural_framing_system", `Create structural framing system failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
