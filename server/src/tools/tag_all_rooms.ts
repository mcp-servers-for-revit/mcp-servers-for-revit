import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerTagAllRoomsTool(server: McpServer) {
  server.tool(
    "tag_all_rooms",
    "Create tags for all rooms in the current active view. Tags will be placed at the center point of each room, displaying the room name and number.",
    {
      useLeader: z
        .boolean()
        .optional()
        .default(false)
        .describe("Whether to use a leader line when creating the tags"),
      tagTypeId: z
        .string()
        .optional()
        .describe("The ID of the specific room tag family type to use. If not provided, the default room tag type will be used"),
      roomIds: z
        .array(z.number())
        .optional()
        .describe("Optional array of specific room element IDs to tag. If not provided, all rooms in the current view will be tagged"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("tag_rooms", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
          isError: reportsFailure(response),
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Room tagging failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
