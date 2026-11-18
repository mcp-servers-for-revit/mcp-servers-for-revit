import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateModelLinesFromDwgLayerTool(server: McpServer) {
  server.tool(
    "create_model_lines_from_dwg_layer",
    "Draw one DWG layer's curves as Revit model lines. Lines become bound lines, bounded arcs become 3-point arcs, and other curve types (full circles, ellipses, splines) become tessellated polylines. All curves are placed on a sketch plane at the layer's median elevation. Use extract_dwg_curves to discover DWG names and layer names first.",
    {
      data: z
        .object({
          dwgNameOrId: z
            .string()
            .describe(
              "Element ID or name (DWG file name, exact or partial) of the imported/linked DWG to draw from."
            ),
          layer: z
            .string()
            .describe(
              "Exact DWG layer name to draw (e.g. 'A-GRID'). Use extract_dwg_curves to list layer names."
            ),
          maxLines: z
            .number()
            .int()
            .positive()
            .default(200)
            .optional()
            .describe(
              "Maximum number of model lines to create (default 200, hard cap 5000)."
            ),
        })
        .describe("Configuration parameters: DWG source, layer to draw, and line cap."),
    },
    async (args, _extra) => {
      const d = args.data;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_model_lines_from_dwg_layer", {
            dwgNameOrId: d.dwgNameOrId,
            layer: d.layer,
            maxLines: d.maxLines ?? 200,
          });
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
              text: `Create model lines from DWG layer failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
