import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateWallsFromDwgPocheTool(server: McpServer) {
  server.tool(
    "create_walls_from_dwg_poche",
    "Generate Revit walls from the hatch fills of an imported or linked DWG (poche regions). Works on the programmatic representation of graphical hatches: DWG hatch entities come into Revit as flat (volume-zero) solids with no solid-level layer; the source DWG layer lives on Face.GraphicsStyleId of each face. This tool scans flat solids, keeps faces on the requested DWG layer (or all of them if no layer is given), extracts each face's boundary loop, merges collinear edges into straight runs, pairs runs as the two faces of one wall (exact corners), and merges collinear centerline pieces across loops. Collinear gaps up to 8ft (door openings incl. jamb trim) are bridged with thickness agreement and jamb-run evidence, so walls are placed continuous across openings; insert door objects afterwards to cut the real openings. Regions wider than the thickness band (2in..maxWallThicknessFt) are ignored automatically (room fills etc). Thickness maps to the nearest Revit wall type by compound width. Use create_walls_from_dwg_layer instead when you want to work with lines and polylines.",
    {
      data: z
        .object({
          dwgNameOrId: z
            .string()
            .describe("Element ID or name (exact or partial) of the imported/linked DWG."),
          pocheLayer: z
            .string()
            .optional()
            .describe("Optional DWG layer to restrict which hatch-fill faces count as poche (matched on Face.GraphicsStyleId, case-insensitive). Omit to accept hatch fills from every layer."),
          heightFt: z
            .number()
            .positive()
            .default(10)
            .optional()
            .describe("Wall height in feet above the level (default 10)."),
          maxWallThicknessFt: z
            .number()
            .positive()
            .default(5.0)
            .optional()
            .describe("Maximum pairing distance in feet (default 5.0 with the poche containment gate; lines across open rooms are culled by hatch-containment, not thickness). Minimum is fixed at 2in."),
          minWallLengthFt: z
            .number()
            .positive()
            .default(2.0)
            .optional()
            .describe("Minimum wall centerline length in feet (default 2; lower to keep sill/patch segments). Shorter centerlines are rejected as jamb linework."),
          wallTypeName: z
            .string()
            .optional()
            .describe("Optional wall type name (exact or partial) to force for all walls. If omitted, the nearest type by thickness is chosen per wall."),
          levelId: z
            .number()
            .int()
            .optional()
            .describe("Optional Revit level ElementId. If omitted, the level nearest the hatch-fill vertices' median Z is used."),
          excludeDoorArcs: z
            .boolean()
            .default(true)
            .optional()
            .describe("Reject short wall centerlines spanning detected door-swing arcs (default true)."),
          maxWalls: z
            .number()
            .int()
            .positive()
            .default(200)
            .optional()
            .describe("Maximum walls to create (default 200, hard cap 5000)."),
        })
        .describe("Configuration parameters: which DWG hatch-fill faces to scan, wall geometry limits, and element caps."),
    },
    async (args, _extra) => {
      const d = args.data;
      const params: Record<string, unknown> = {
        dwgNameOrId: d.dwgNameOrId,
        pocheLayer: d.pocheLayer ?? "",
        heightFt: d.heightFt ?? 10,
        maxWallThicknessFt: d.maxWallThicknessFt ?? 5.0,
        minWallLengthFt: d.minWallLengthFt ?? 2.0,
        excludeDoorArcs: d.excludeDoorArcs ?? true,
        maxWalls: d.maxWalls ?? 200,
      };
      if (d.wallTypeName) params["wallTypeName"] = d.wallTypeName;
      if (d.levelId) params["levelId"] = d.levelId;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_walls_from_dwg_poche", params);
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
              text: `Create walls from DWG poche failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
