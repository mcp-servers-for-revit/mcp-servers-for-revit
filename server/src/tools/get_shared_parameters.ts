import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerGetSharedParametersTool(server: McpServer) {
  server.tool(
    "get_shared_parameters",
    "List shared parameters and their category bindings.",
    {
      categoryFilter: z
        .string()
        .optional()
        .describe("Optional category name filter."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_shared_parameters", {
            categoryFilter: args.categoryFilter,
          });
        }, 30000);

        return toolResponse("get_shared_parameters", response);
      } catch (error) {
        return toolError("get_shared_parameters", `Get shared parameters failed: ${errorMessage(error)}`);
      }
    }
  );
}
