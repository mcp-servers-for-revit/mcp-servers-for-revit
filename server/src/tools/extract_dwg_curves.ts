import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExtractDwgCurvesTool(server: McpServer) {
  server.tool(
    "extract_dwg_curves",
    "Extract curve geometry (layer, type, endpoints, direction, arc/ellipse parameters, tessellated polyline) from an imported or linked DWG file. Omit dwgNameOrId to list available DWGs in the model.",
    {
      dwgNameOrId: z
        .string()
        .optional()
        .describe(
          "Element ID or name (DWG file name, exact or partial) of the imported/linked DWG to extract from. If omitted, returns a list of all available DWGs instead."
        ),
      layerFilter: z
        .string()
        .optional()
        .describe(
          "Optional exact DWG layer name to filter curves by (e.g. 'A-GRID'). If omitted, curves from all layers are returned."
        ),
      maxCurves: z
        .number()
        .int()
        .positive()
        .default(500)
        .optional()
        .describe(
          "Maximum number of curves to return in the response (default 500). Layer summary always covers all matching curves."
        ),
      includeTessellation: z
        .boolean()
        .default(true)
        .optional()
        .describe(
          "Tessellate each returned curve (default true). Set false to return only type/layer/endpoint data - safer on very large or pathological DWGs."
        ),
      maxPointsPerCurve: z
        .number()
        .int()
        .min(0)
        .default(0)
        .optional()
        .describe(
          "Cap on tessellation points per curve (0 = unlimited). Excess points are omitted and 'tessellatedTruncated' reports the omitted count."
        ),
      countAll: z
        .boolean()
        .default(true)
        .optional()
        .describe(
          "Traverse the entire DWG geometry tree even past maxCurves so layer totals are complete (default true). Set false to stop at maxCurves - faster on huge DWGs, but layer counts are partial and flagged with 'depthCapped'/'truncated'."
        ),
    },
    async (args, _extra) => {
      const params: Record<string, unknown> = {
        maxCurves: args.maxCurves ?? 500,
        includeTessellation: args.includeTessellation ?? true,
        maxPointsPerCurve: args.maxPointsPerCurve ?? 0,
        countAll: args.countAll ?? true,
      };
      if (args.dwgNameOrId !== undefined && args.dwgNameOrId !== "")
        params["dwgNameOrId"] = args.dwgNameOrId;
      if (args.layerFilter !== undefined && args.layerFilter !== "")
        params["layerFilter"] = args.layerFilter;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("extract_dwg_curves", params);
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
              text: `Extract DWG curves failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
