import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerGetCompoundStructureTool(server: McpServer) {
  server.tool(
    "get_compound_structure",
    "Get the compound structure (layer stratigraphy) of a system family type (wall, floor, roof, ceiling). Returns all layers with thickness, material, and function.",
    {
      typeId: z
        .number()
        .optional()
        .describe("Element ID of the type. Provide this OR typeName + category."),
      typeName: z
        .string()
        .optional()
        .describe("Name of the type (e.g. 'Generic - 200mm'). Use with category."),
      category: z
        .enum(["Walls", "Floors", "Roofs", "Ceilings"])
        .optional()
        .describe("Category of the type. Required when using typeName."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_compound_structure", {
            typeId: args.typeId,
            typeName: args.typeName,
            category: args.category,
          });
        }, 120000);

        return toolResponse("get_compound_structure", response);
      } catch (error) {
        return toolError(
          "get_compound_structure",
          `Get compound structure failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
