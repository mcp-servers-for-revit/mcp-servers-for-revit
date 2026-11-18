import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerGetMaterialsTool(server: McpServer) {
  server.tool(
    "get_materials",
    "List materials in the project, optionally filtered by class.",
    {
      materialClass: z
        .string()
        .optional()
        .describe("Filter materials by class (case-insensitive exact match, e."),
      nameFilter: z
        .string()
        .optional()
        .describe(
          "Filter materials whose name contains this substring (case-insensitive)"
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_materials", { materialClass: args.materialClass, nameFilter: args.nameFilter });
        }, 30000);

        return toolResponse("get_materials", response);
      } catch (error) {
        return toolError("get_materials", `Get materials failed: ${errorMessage(error)}`);
      }
    }
  );
}
