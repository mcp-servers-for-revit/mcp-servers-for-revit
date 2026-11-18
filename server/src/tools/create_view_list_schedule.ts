import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateViewListScheduleTool(server: McpServer) {
  server.tool(
    "create_view_list_schedule",
    "Create a View List schedule with view names, types, scales, and sheet placement",
    {
      name: z
        .string()
        .optional()
        .describe("Schedule name"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_schedule", {
            preset: "view_index",
            type: "view_list",
            name: args.name ?? "View List",
          });
        });
        return rawToolResponse("create_view_list_schedule", response);
      } catch (error) {
        return rawToolError(
          "create_view_list_schedule",
          `Create view list schedule failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
