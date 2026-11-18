import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerGetPhasesTool(server: McpServer) {
  server.tool(
    "get_phases",
    "List all phases in the project with their sequence order.",
    {
      includePhaseFilters: z
        .boolean()
        .optional()
        .describe(
          "Include phase filters in addition to phases (default: true)"
        ),
    },
    async (args, extra) => {
      const params = {
        includePhaseFilters: args.includePhaseFilters ?? true,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_phases", params);
        }, 30000);

        return rawToolResponse("get_phases", response);
      } catch (error) {
        return rawToolError("get_phases", `Get phases failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
