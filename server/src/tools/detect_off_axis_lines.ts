import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerDetectOffAxisLinesTool(server: McpServer) {
  server.tool(
    "detect_off_axis_lines",
    "Scan model geometry for elements with angular deviations between minAngleDeg (default 0.001°) and maxAngleDeg (default 0.1°). Scans walls, beams, grids, floors, ceilings, and roofs.",
    {
      minAngleDeg: z
        .number()
        .positive()
        .default(0.001)
        .optional()
        .describe("Minimum angular deviation in degrees from nearest orthogonal axis (default 0.001)"),
      maxAngleDeg: z
        .number()
        .positive()
        .default(0.1)
        .optional()
        .describe("Maximum angular deviation in degrees from nearest orthogonal axis (default 0.1)"),
    },
    async (args, _extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("detect_off_axis_lines", {
            minAngleDeg: args.minAngleDeg ?? 0.001,
            maxAngleDeg: args.maxAngleDeg ?? 0.1,
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
              text: `Detect off-axis lines failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
