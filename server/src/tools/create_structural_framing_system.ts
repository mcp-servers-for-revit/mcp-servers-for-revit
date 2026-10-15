import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerCreateStructuralFramingSystemTool(server: McpServer) {
  server.tool(
    "create_structural_framing_system",
    "Create a structural beam framing system in Revit. Generates beams within a rectangular boundary at fixed spacing intervals. The system uses Revit's BeamSystem API to create properly connected beam layouts. All units are in millimeters (mm).",
    {
      levelName: z
        .string()
        .describe(
          "Name of the level to place the beam system on (e.g., 'Level 1', 'Level 2'). If the level doesn't exist but follows 'Level N' pattern, it will be auto-created at 4000mm floor-to-floor height."
        ),
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
        .describe(
          "Which edge defines the beam direction. Beams run perpendicular to this edge. 'bottom'/'top' = beams run in Y direction, 'left'/'right' = beams run in X direction."
        ),
      layoutRule: z
        .enum(["fixed_distance"])
        .default("fixed_distance")
        .describe(
          "Layout rule type. Currently only 'fixed_distance' is supported."
        ),
      justify: z
        .enum(["beginning", "center", "end", "directionline"])
        .default("center")
        .describe(
          "Beam justification within the layout. 'center' places beams symmetrically, 'beginning'/'end' align to boundary edges."
        ),
      beamTypeName: z
        .string()
        .optional()
        .describe(
          "Name of the beam family type to use (e.g., 'W10x12', 'W-Wide Flange'). If not provided, the first available structural framing type will be used."
        ),
      elevation: z
        .number()
        .default(0)
        .describe(
          "Elevation offset from the level in millimeters. Use this to adjust the vertical position of the beam system."
        ),
      is3d: z
        .boolean()
        .default(false)
        .describe(
          "Whether to create a 3D beam system. Set to true for sloped or non-planar systems."
        ),
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
              text: `Create structural framing system failed: ${
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
