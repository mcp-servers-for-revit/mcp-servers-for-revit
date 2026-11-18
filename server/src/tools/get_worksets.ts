import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerGetWorksetsTool(server: McpServer) {
  server.tool(
    "get_worksets",
    "List worksets with element counts and open/close status.",
    {
      includeSystemWorksets: z
        .boolean()
        .optional()
        .describe("Include system worksets. Default: false."),
    },
    async (args, extra) => {
      const params = {
        includeSystemWorksets: args.includeSystemWorksets ?? false,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_worksets", params);
        }, 30000);

        return toolResponse("get_worksets", response);
      } catch (error) {
        return toolError("get_worksets", `Get worksets failed: ${errorMessage(error)}`);
      }
    }
  );
}
