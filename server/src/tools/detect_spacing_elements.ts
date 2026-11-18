import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerDetectSpacingElementsTool(server: McpServer) {
  server.tool(
    "detect_spacing_elements",
    "Detect walls and structural beams that are slightly off the 1/4-inch perpendicular regularized grid lattice. Computes signed perpendicular offset and deviation.",
    {
      limit: z
        .number()
        .int()
        .positive()
        .default(50)
        .optional()
        .describe("Maximum number of candidate elements to return (default 50)"),
    },
    async (args, _extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("detect_spacing_elements", {
            limit: args.limit ?? 50,
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
              text: `Detect spacing elements failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
