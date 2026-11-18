import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerFixOffAxisInPlaceTool(server: McpServer) {
  server.tool(
    "fix_off_axis_inplace",
    "Fix slightly off-axis sketch profile lines of In-Place Component Extrusion forms using midpoint rotation with fallback to ray-ray closure.",
    {
      hostIds: z
        .union([z.array(z.number()), z.string()])
        .optional()
        .describe("List or CSV of In-Place FamilyInstance or Extrusion form element IDs to fix."),
      lineIds: z
        .union([z.array(z.number()), z.string()])
        .optional()
        .describe("Optional list or CSV of specific flagged sketch line IDs to snap."),
      minAngleDeg: z
        .number()
        .default(0.0000001)
        .optional()
        .describe("Minimum angular deviation in degrees (default 0.0000001)"),
      maxAngleDeg: z
        .number()
        .default(0.1)
        .optional()
        .describe("Maximum angular deviation in degrees (default 0.1)"),
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
          return await revitClient.sendCommand("fix_off_axis_inplace", {
            hostIds: args.hostIds,
            lineIds: args.lineIds,
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
              text: `Fix off-axis in-place sketches failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
