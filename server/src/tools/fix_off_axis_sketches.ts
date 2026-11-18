import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerFixOffAxisSketchesTool(server: McpServer) {
  server.tool(
    "fix_off_axis_sketches",
    "Fix slightly off-axis sketch profiles in Floors, Ceilings, and FootPrintRoofs using exact ray-ray intersection reconstruction for 100% closed boundary loops.",
    {
      hostIds: z
        .union([z.array(z.number()), z.string()])
        .optional()
        .describe("List or CSV of host element IDs (Floor, Ceiling, Roof) to process."),
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
      maxElements: z
        .number()
        .int()
        .positive()
        .default(50)
        .optional()
        .describe("Maximum number of elements to process in a single run (default 50)"),
      previewOnly: z
        .boolean()
        .default(false)
        .optional()
        .describe("When true, report what would be fixed without modifying the model (default false)"),
    },
    async (args, _extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("fix_off_axis_sketches", {
            hostIds: args.hostIds,
            lineIds: args.lineIds,
            minAngleDeg: args.minAngleDeg,
            maxAngleDeg: args.maxAngleDeg,
            maxMoveInches: args.maxMoveInches ?? 1.0,
            maxElements: args.maxElements ?? 50,
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
              text: `Fix off-axis sketches failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
