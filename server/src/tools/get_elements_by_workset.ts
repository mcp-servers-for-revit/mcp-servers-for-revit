import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerGetElementsByWorksetTool(server: McpServer) {
  server.tool(
    "get_elements_by_workset",
    "Get all elements belonging to a specific workset, with category summary and optional filtering.",
    {
      worksetName: z
        .string()
        .describe("Name of the workset to query"),
      categoryFilter: z
        .array(z.string())
        .optional()
        .describe(
          "Optional list of category names to filter (e.g. ['Walls', 'Doors']). If omitted, returns all categories."
        ),
      maxElements: z
        .number()
        .optional()
        .describe(
          "Maximum number of element details to return. Default: 500. Category summary always includes all elements."
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_elements_by_workset", {
            worksetName: args.worksetName,
            categoryFilter: args.categoryFilter,
            maxElements: args.maxElements ?? 500,
          });
        }, 120000);

        return toolResponse("get_elements_by_workset", response);
      } catch (error) {
        return toolError(
          "get_elements_by_workset",
          `Get elements by workset failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
