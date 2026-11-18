import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerPurgeUnusedTool(server: McpServer) {
  server.tool(
    "purge_unused",
    "Find and remove unused families, types, and materials.",
    {
      dryRun: z
        .boolean()
        .optional()
        .describe("If true (default), only reports what would be purged without deleting."),
      maxElements: z
        .number()
        .optional()
        .describe("Maximum number of elements to purge in one operation (default: 500)"),
    },
    async (args, extra) => {
      const params = {
        dryRun: args.dryRun ?? true,
        maxElements: args.maxElements ?? 500,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("purge_unused", params);
        }, 300000);

        return rawToolResponse("purge_unused", response);
      } catch (error) {
        return rawToolError("purge_unused", `Purge unused failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
