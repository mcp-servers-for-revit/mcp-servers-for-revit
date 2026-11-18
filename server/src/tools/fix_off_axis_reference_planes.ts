import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerFixOffAxisReferencePlanesTool(server: McpServer) {
  server.tool(
    "fix_off_axis_reference_planes",
    "Snap normal vector of slightly off-axis vertical reference planes to the nearest 0/45/90 orientation. Skips pinned reference planes and planes with dimension constraints.",
    {
      elementIds: z
        .union([z.array(z.number()), z.string()])
        .optional()
        .describe("List or CSV of reference plane IDs to fix. If omitted, scans and fixes all eligible off-axis reference planes in the model."),
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
          return await revitClient.sendCommand("fix_off_axis_reference_planes", {
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
              text: `Fix off-axis reference planes failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
