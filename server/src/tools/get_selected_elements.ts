import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerGetSelectedElementsTool(server: McpServer) {
  server.tool(
    "get_selected_elements",
    "Get info about currently selected elements in Revit.",
    {
      limit: z
        .number()
        .optional()
        .describe("Maximum number of elements to return"),
    },
    async (args, extra) => {
      const params = {
        limit: args.limit || 100,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_selected_elements", params);
        }, 30000);

        return rawToolResponse("get_selected_elements", response);
      } catch (error) {
        return rawToolError("get_selected_elements", `get selected elements failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
