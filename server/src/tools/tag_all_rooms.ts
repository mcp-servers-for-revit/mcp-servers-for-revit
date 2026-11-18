import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerTagAllRoomsTool(server: McpServer) {
  server.tool(
    "tag_all_rooms",
    "Tag rooms in the current view with room tags.",
    {
      useLeader: z
        .boolean()
        .optional()
        .default(false)
        .describe("Whether to use a leader line when creating the tags"),
      tagTypeId: z
        .string()
        .optional()
        .describe("The ID of the specific room tag family type to use."),
      roomIds: z
        .array(z.number())
        .optional()
        .describe("Optional array of specific room element IDs to tag."),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("tag_rooms", params);
        });

        return rawToolResponse("tag_all_rooms", response);
      } catch (error) {
        return rawToolError("tag_all_rooms", `Room tagging failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
