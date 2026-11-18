import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerFixSpacingElementsTool(server: McpServer) {
  server.tool(
    "fix_spacing_elements",
    "Regularize perpendicular spacing of walls and structural beams onto a 1/4-inch lattice relative to grids, with live iterative self-correction.",
    {
      elementIds: z
        .union([z.array(z.number()), z.string()])
        .describe("List or CSV of wall and beam element IDs to snap to grid lattice (10-12 IDs per call recommended for large models)."),
      maxMoveInches: z
        .number()
        .positive()
        .default(1.0)
        .optional()
        .describe("Maximum allowed movement in inches before an element is skipped (default 1.0)"),
    },
    async (args, _extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("fix_spacing_elements", {
            elementIds: args.elementIds,
            maxMoveInches: args.maxMoveInches ?? 1.0,
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
              text: `Fix spacing elements failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
