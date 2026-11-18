import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerBatchModifyViewRangeTool(server: McpServer) {
  server.tool(
    "batch_modify_view_range",
    "Batch-modify view range (cut plane, top, bottom) for plan views.",
    {
      viewIds: z.array(z.number()).describe("IDs of plan views to modify."),
      topOffsetMm: z.number().optional().describe("Top clip plane offset from level in mm."),
      cutPlaneOffsetMm: z.number().optional().describe("Cut plane offset from level in mm (typically 1200mm)."),
      bottomOffsetMm: z.number().optional().describe("Bottom clip plane offset from level in mm."),
      viewDepthOffsetMm: z.number().optional().describe("View depth plane offset from level in mm."),
    },
    async (args, extra) => {
      try {
        const params: any = { viewIds: args.viewIds };
        if (args.topOffsetMm !== undefined) params.topOffsetMm = args.topOffsetMm;
        if (args.cutPlaneOffsetMm !== undefined) params.cutPlaneOffsetMm = args.cutPlaneOffsetMm;
        if (args.bottomOffsetMm !== undefined) params.bottomOffsetMm = args.bottomOffsetMm;
        if (args.viewDepthOffsetMm !== undefined) params.viewDepthOffsetMm = args.viewDepthOffsetMm;

        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("batch_modify_view_range", params);
        });
        return rawToolResponse("batch_modify_view_range", response);
      } catch (error) {
        return rawToolError("batch_modify_view_range", `Batch modify view range failed: ${errorMessage(error)}`);
      }
    }
  );
}
