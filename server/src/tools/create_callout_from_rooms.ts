import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateCalloutFromRoomsTool(server: McpServer) {
  server.tool(
    "create_callout_from_rooms",
    "Create callout views from rooms with auto-naming and crop regions.",
    {
      roomIds: z.array(z.number()).optional()
        .describe("Specific room element IDs. If empty, uses levelName or all rooms."),
      levelName: z.string().optional()
        .describe("Level name to filter rooms (e.g. 'Level 1'). Ignored if roomIds provided."),
      offset: z.number().optional().default(300)
        .describe("Boundary offset around room in mm (default: 300mm)."),
      viewTemplateId: z.string().optional()
        .describe("View template element ID to apply to created callouts."),
      scale: z.number().optional().default(50)
        .describe("View scale (e.g. 50 = 1:50, 100 = 1:100)."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_callout_from_rooms", {
            roomIds: args.roomIds ?? [],
            levelName: args.levelName ?? "",
            offset: args.offset ?? 300,
            viewTemplateId: args.viewTemplateId ?? "",
            scale: args.scale ?? 50,
          });
        });
        return rawToolResponse("create_callout_from_rooms", response);
      } catch (error) {
        return rawToolError("create_callout_from_rooms", `Create callout from rooms failed: ${errorMessage(error)}`);
      }
    }
  );
}
