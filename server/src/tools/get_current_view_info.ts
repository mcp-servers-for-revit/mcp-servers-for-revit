import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerGetCurrentViewInfoTool(server: McpServer) {
  server.tool(
    "get_current_view_info",
    "Get details about the active view (type, scale, crop, etc.).",
    {},
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_current_view_info", {});
        }, 30000);

        return rawToolResponse("get_current_view_info", response);
      } catch (error) {
        return rawToolError("get_current_view_info", `get current view info failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
