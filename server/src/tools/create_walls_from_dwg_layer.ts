import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateWallsFromDwgLayerTool(server: McpServer) {
  server.tool(
    "create_walls_from_dwg_layer",
    "Generate Revit walls from paired face lines on one or more DWG layers (comma-separated, e.g. 'PEN1,Pen 1'). Faces split across pen layers and fragmented collinear pieces are merged first, then duplicate segments (pens redrawing the same line) are removed. Pairs near-parallel rails whose perpendicular distance is 2in-36in (wall thickness) with sufficient directional overlap, then maps each pair's thickness to the nearest Revit wall type by compound width. Short pairs are rejected as door-jamb linework; pairs spanning detected door-swing arcs (quarter arcs on any DWG layer) are rejected. Arc curves are counted but not built. Run fix_off_axis_* and fix_spacing_elements afterward to correct drafting slop.",
    {
      data: z
        .object({
          dwgNameOrId: z
            .string()
            .describe("Element ID or name (exact or partial) of the imported/linked DWG."),
          layer: z
            .string()
            .describe("DWG layer(s) containing the wall face lines, comma-separated for pen-split faces (e.g. 'PEN1,Pen 1'). Use extract_dwg_curves to list layers."),
          heightFt: z
            .number()
            .positive()
            .default(10)
            .optional()
            .describe("Wall height in feet above the level (default 10)."),
          maxWallThicknessFt: z
            .number()
            .positive()
            .default(3.0)
            .optional()
            .describe("Maximum pairing distance in feet (default 3.0 = 36in). Minimum is fixed at 2in."),
          minWallLengthFt: z
            .number()
            .positive()
            .default(3.5)
            .optional()
            .describe("Minimum wall centerline length in feet; shorter pairs are rejected as door jamb linework (default 3.5)."),
          wallTypeName: z
            .string()
            .optional()
            .describe("Optional wall type name (exact or partial) to force for all walls. If omitted, the nearest type by thickness is chosen per wall."),
          levelId: z
            .number()
            .int()
            .optional()
            .describe("Optional Revit level ElementId. If omitted, the level nearest the layer's median Z is used."),
          excludeDoorArcs: z
            .boolean()
            .default(true)
            .optional()
            .describe("Reject wall pairs spanning detected door-swing arcs (default true)."),
          maxWalls: z
            .number()
            .int()
            .positive()
            .default(200)
            .optional()
            .describe("Maximum walls to create (default 200, hard cap 5000)."),
        })
        .describe("Configuration parameters: DWG layers to pair as wall faces, geometry limits, and element caps."),
    },
    async (args, _extra) => {
      const d = args.data;
      const params: Record<string, unknown> = {
        dwgNameOrId: d.dwgNameOrId,
        layer: d.layer,
        heightFt: d.heightFt ?? 10,
        maxWallThicknessFt: d.maxWallThicknessFt ?? 3.0,
        minWallLengthFt: d.minWallLengthFt ?? 3.5,
        excludeDoorArcs: d.excludeDoorArcs ?? true,
        maxWalls: d.maxWalls ?? 200,
      };
      if (d.wallTypeName) params["wallTypeName"] = d.wallTypeName;
      if (d.levelId) params["levelId"] = d.levelId;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_walls_from_dwg_layer", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Create walls from DWG layer failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
