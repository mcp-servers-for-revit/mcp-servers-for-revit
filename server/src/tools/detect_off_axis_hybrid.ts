import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerDetectOffAxisHybridTool(server: McpServer) {
  server.tool(
    "detect_off_axis_hybrid",
    "Detect slightly off-axis elements in Revit using Revit's native warning engine (Document.GetWarnings). Fast, authoritative detection of Walls, Beams, Grids, Reference Planes, Floor/Ceiling/Roof sketches, In-Place Extrusions, and Model Lines, with movement predictions and LargeFix flagging.",
    {},
    async (_args, _extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("detect_off_axis_hybrid", {});
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
              text: `Detect off-axis hybrid failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
