import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerDimensionGridsTool(server: McpServer) {
  server.tool(
    "dimension_grids",
    "Automatically dimension the grids in the active Revit plan view. Groups parallel straight grids and, for each group, adds an overall dimension (first to last grid) and a bay-by-bay dimension string (every grid). BEFORE CALLING: if the user has not said where the dimensions should go, ask them: for vertical grids (running up/down on screen, usually numbered 1, 2, 3...) top, bottom, both or at the bubble end; for horizontal grids (running left/right, usually lettered A, B, C...) left, right, both or at the bubble end. Do not guess these. Sides are as seen on screen, so they stay correct in rotated views. Angled grids with no parallel partner and curved grids are skipped and reported. The active view must be a plan (not a sheet or 3D view). All changes are made in one transaction, so a single Undo in Revit removes them. Offsets are PAPER millimeters (as printed on the sheet) and are scaled by the view scale automatically.",
    {
      verticalGridsAt: z
        .enum(["top", "bottom", "both", "bubble"])
        .describe(
          "Required. Where to dimension grids that run up/down on screen: 'top', 'bottom', 'both', or 'bubble' (the end showing grid bubbles; falls back to top if no bubbles are visible). Ask the user if they have not said."
        ),
      horizontalGridsAt: z
        .enum(["left", "right", "both", "bubble"])
        .describe(
          "Required. Where to dimension grids that run left/right on screen: 'left', 'right', 'both', or 'bubble' (the end showing grid bubbles; falls back to left if no bubbles are visible). Ask the user if they have not said."
        ),
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
          "Paper distance in mm (as printed) from the grid ends to the overall dimension line, measured toward the building. Multiplied by the view scale. Default: 10"
        ),
      bayOffset: z
        .number()
        .nonnegative()
        .optional()
        .describe(
          "Paper distance in mm (as printed) from the grid ends to the bay dimension line, measured toward the building. Multiplied by the view scale. Default: 18"
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
