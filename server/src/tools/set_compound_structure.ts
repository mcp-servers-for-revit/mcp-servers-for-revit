import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerSetCompoundStructureTool(server: McpServer) {
  server.tool(
    "set_compound_structure",
    "Set or replace the compound structure (layer stratigraphy) of a system family type. Optionally duplicate the type first to avoid modifying all instances.",
    {
      typeId: z
        .number()
        .optional()
        .describe("Element ID of the type. Provide this OR typeName + category."),
      typeName: z
        .string()
        .optional()
        .describe("Name of the type to modify. Use with category."),
      category: z
        .enum(["Walls", "Floors", "Roofs", "Ceilings"])
        .optional()
        .describe("Category of the type. Required when using typeName."),
      duplicateAsName: z
        .string()
        .optional()
        .describe("If provided, duplicate the type with this name before modifying. Prevents changing all existing instances."),
      layers: z
        .array(
          z.object({
            function: z
              .enum(["Structure", "Substrate", "Insulation", "Finish1", "Finish2", "Membrane", "StructuralDeck"])
              .describe("Layer function"),
            widthMm: z
              .number()
              .describe("Layer thickness in millimeters. Must be 0 for Membrane layers."),
            materialName: z
              .string()
              .optional()
              .describe("Material name to assign. If omitted and materialId not set, uses 'By Category'."),
            materialId: z
              .number()
              .optional()
              .describe("Material element ID. Alternative to materialName."),
            wraps: z
              .boolean()
              .optional()
              .describe("Whether this layer participates in wrapping at inserts/ends. Default: false."),
          })
        )
        .describe("Array of layers from exterior to interior (walls) or top to bottom (floors/roofs/ceilings)."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_compound_structure", {
            typeId: args.typeId,
            typeName: args.typeName,
            category: args.category,
            duplicateAsName: args.duplicateAsName,
            layers: args.layers,
          });
        }, 120000);

        return toolResponse("set_compound_structure", response);
      } catch (error) {
        return toolError(
          "set_compound_structure",
          `Set compound structure failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
