import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerSetViewCropTool(server: McpServer) {
  server.tool(
    "set_view_crop",
    "Control the crop box of a view: enable/disable, set bounds from elements with offset or explicit coordinates, show/hide crop boundary.",
    {
      viewId: z.number().optional().describe("View ID. Default: active view."),
      cropActive: z.boolean().optional().describe("Enable (true) or disable (false) the crop region."),
      cropVisible: z.boolean().optional().describe("Show (true) or hide (false) the crop boundary lines."),
      elementIds: z.array(z.number()).optional().describe("Fit crop box to these elements with offset."),
      offsetMm: z.number().optional().default(300).describe("Offset around elements in mm (default 300)."),
      minXMm: z.number().optional().describe("Explicit crop min X in mm."),
      minYMm: z.number().optional().describe("Explicit crop min Y in mm."),
      maxXMm: z.number().optional().describe("Explicit crop max X in mm."),
      maxYMm: z.number().optional().describe("Explicit crop max Y in mm."),
      reset: z.boolean().optional().describe("Reset: disable crop box."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_view_crop", {
            viewId: args.viewId,
            cropActive: args.cropActive,
            cropVisible: args.cropVisible,
            elementIds: args.elementIds ?? [],
            offsetMm: args.offsetMm ?? 300,
            minXMm: args.minXMm,
            minYMm: args.minYMm,
            maxXMm: args.maxXMm,
            maxYMm: args.maxYMm,
            reset: args.reset ?? false,
          });
        }, 120000);
        return toolResponse("set_view_crop", response);
      } catch (error) {
        return toolError("set_view_crop", `Set view crop failed: ${errorMessage(error)}`);
      }
    }
  );
}
