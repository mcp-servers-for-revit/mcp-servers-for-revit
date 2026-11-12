import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerDimensionGridsTool(server: McpServer) {
  server.tool(
    "dimension_grids",
    "Automatically dimension the grids in the active Revit plan view. Groups parallel straight grids and, for each group, adds an overall dimension (first to last grid) and a bay-by-bay dimension string (every grid). Angled grids with no parallel partner and curved grids are skipped and reported. The active view must be a plan (not a sheet or 3D view). All changes are made in one transaction, so a single Undo in Revit removes them. Offsets are in millimeters (mm).",
    {
      includeOverall: z
        .boolean()
        .optional()
        .describe("Add the overall first-to-last grid dimension. Default: true"),
      includeBays: z
        .boolean()
        .optional()
        .describe("Add the bay-by-bay dimension string through every grid. Default: true"),
      overallOffset: z
        .number()
        .nonnegative()
        .optional()
        .describe(
          "Distance in mm from the grid ends (where the grid lines start) to the overall dimension line, measured toward the building. Default: 600"
        ),
      bayOffset: z
        .number()
        .nonnegative()
        .optional()
        .describe(
          "Distance in mm from the grid ends to the bay dimension line, measured toward the building. Default: 1800"
        ),
      skipIfExisting: z
        .boolean()
        .optional()
        .describe(
          "If the view already has grid-to-grid dimensions, stop without creating duplicates. Default: true"
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("dimension_grids", args);
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
              text: `Grid dimensioning failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
