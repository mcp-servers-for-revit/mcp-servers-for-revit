import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateMaterialTakeoffScheduleTool(server: McpServer) {
  server.tool(
    "create_material_takeoff_schedule",
    "Create a Material Takeoff schedule with quantities by category",
    {
      name: z
        .string()
        .optional()
        .describe("Schedule name"),
      categoryName: z
        .string()
        .describe("BuiltInCategory name, e.g. 'OST_Walls'"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_schedule", {
            preset: "material_quantities",
            type: "material_takeoff",
            categoryName: args.categoryName,
            name: args.name ?? "Material Takeoff",
          });
        });
        return rawToolResponse("create_material_takeoff_schedule", response);
      } catch (error) {
        return rawToolError(
          "create_material_takeoff_schedule",
          `Create material takeoff schedule failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
