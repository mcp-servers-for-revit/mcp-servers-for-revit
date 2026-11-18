import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerNavigateViewTool(server: McpServer) {
  server.tool(
    "navigate_view",
    "Navigate views: activate a view, zoom to fit, zoom to specific elements, zoom in/out, or close a view.",
    {
      action: z.enum(["activate", "zoom_to_fit", "zoom_to_elements", "zoom", "close"])
        .describe("Navigation action to perform."),
      viewId: z.number().optional().describe("Target view ID."),
      viewName: z.string().optional().describe("Target view name (partial match). Alternative to viewId."),
      elementIds: z.array(z.number()).optional().describe("Elements to zoom to (for zoom_to_elements)."),
      zoomFactor: z.number().optional().describe("Zoom factor for 'zoom' action (>1 = in, <1 = out)."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("navigate_view", {
            action: args.action,
            viewId: args.viewId,
            viewName: args.viewName,
            elementIds: args.elementIds ?? [],
            zoomFactor: args.zoomFactor,
          });
        }, 30000);
        return toolResponse("navigate_view", response);
      } catch (error) {
        return toolError("navigate_view", `Navigate view failed: ${errorMessage(error)}`);
      }
    }
  );
}
