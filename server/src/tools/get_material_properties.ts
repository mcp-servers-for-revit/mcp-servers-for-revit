import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerGetMaterialPropertiesTool(server: McpServer) {
  server.tool(
    "get_material_properties",
    "Get detailed physical/thermal properties of a material.",
    {
      materialId: z
        .number()
        .optional()
        .describe("The Revit element ID of the material"),
      materialName: z
        .string()
        .optional()
        .describe("Material name (case-insensitive). Used if materialId not provided."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_material_properties", {
            materialId: args.materialId,
            materialName: args.materialName,
          });
        });

        return toolResponse("get_material_properties", response);
      } catch (error) {
        return toolError("get_material_properties", `Get material properties failed: ${errorMessage(error)}`);
      }
    }
  );
}
