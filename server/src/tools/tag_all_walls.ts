import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerTagAllWallsTool(server: McpServer) {
  server.tool(
    "tag_all_walls",
    "Tag walls in the current view with wall tags.",
    {
      useLeader: z
        .boolean()
        .optional()
        .default(false)
        .describe("Whether to use a leader line when creating the tags"),
      tagTypeId: z
        .string()
        .optional()
        .describe("The ID of the specific wall tag family type to use."),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("tag_walls", params);
        });
        
        return rawToolResponse("tag_all_walls", response);
      } catch (error) {
        return rawToolError("tag_all_walls", `Wall tagging failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}