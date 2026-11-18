import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerGetLinkedElementsTool(server: McpServer) {
  server.tool(
    "get_linked_elements",
    "Query elements from linked Revit models by category.",
    {
      linkName: z.string().optional()
        .describe("Filter by link name (partial match). Empty = all linked models."),
      categories: z.array(z.string()).optional()
        .describe("Category names to query. Empty = all categories."),
      parameterNames: z.array(z.string()).optional()
        .describe("Parameters to extract per element. Empty = just ID/Category/Name."),
      maxElements: z.number().optional().default(5000)
        .describe("Max elements per linked model."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_linked_elements", {
            linkName: args.linkName ?? "",
            categories: args.categories ?? [],
            parameterNames: args.parameterNames ?? [],
            maxElements: args.maxElements ?? 5000,
          });
        });
        return rawToolResponse("get_linked_elements", response);
      } catch (error) {
        return rawToolError("get_linked_elements", `Get linked elements failed: ${errorMessage(error)}`);
      }
    }
  );
}
