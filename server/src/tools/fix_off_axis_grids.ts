import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerFixOffAxisGridsTool(server: McpServer) {
  server.tool(
    "fix_off_axis_grids",
    "Rotate slightly off-axis 3D grid lines about their midpoint to the nearest 0/45/90 orientation. Skips pinned grids.",
    {
      elementIds: z
        .union([z.array(z.number()), z.string()])
        .optional()
        .describe("List or CSV of grid element IDs to fix. If omitted, scans and fixes all eligible off-axis grids in the model."),
      minAngleDeg: z
        .number()
        .default(0.0000001)
        .optional()
        .describe("Minimum angular deviation in degrees from nearest axis (default 0.0000001)"),
      maxAngleDeg: z
        .number()
        .default(0.1)
        .optional()
        .describe("Maximum angular deviation in degrees from nearest axis (default 0.1)"),
      maxMoveInches: z
        .number()
        .positive()
        .default(1.0)
        .optional()
        .describe("Maximum predicted movement in inches before an element is skipped (default 1.0)"),
      previewOnly: z
        .boolean()
        .default(false)
        .optional()
        .describe("When true, report what would be fixed without modifying the model (default false)"),
    },
    async (args, _extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("fix_off_axis_grids", {
            elementIds: args.elementIds,
            minAngleDeg: args.minAngleDeg,
            maxAngleDeg: args.maxAngleDeg,
            maxMoveInches: args.maxMoveInches ?? 1.0,
            previewOnly: args.previewOnly ?? false,
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
              text: `Fix off-axis grids failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
