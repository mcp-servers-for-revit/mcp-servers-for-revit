import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateFilledRegionTool(server: McpServer) {
  server.tool(
    "create_filled_region",
    "Create a filled region in a view from boundary points.",
    {
      boundaryPoints: z
        .array(z.object({ x: z.number(), y: z.number() }))
        .describe("Boundary points in mm defining the filled region outline (minimum 3 points)"),
      viewId: z
        .number()
        .optional()
        .describe("View ID to create the region in (default: active view)"),
      filledRegionTypeName: z
        .string()
        .optional()
        .describe("Filled region type name (default: first available)"),
    },
    async (args, extra) => {
      const params = {
        boundaryPoints: args.boundaryPoints,
        viewId: args.viewId ?? 0,
        filledRegionTypeName: args.filledRegionTypeName ?? "",
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_filled_region", params);
        });
        return rawToolResponse("create_filled_region", response);
      } catch (error) {
        return rawToolError("create_filled_region", `Create filled region failed: ${errorMessage(error)}`);
      }
    }
  );
}
