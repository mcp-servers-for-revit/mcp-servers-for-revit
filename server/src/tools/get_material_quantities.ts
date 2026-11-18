import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerGetMaterialQuantitiesTool(server: McpServer) {
  server.tool(
    "get_material_quantities",
    "Calculate material quantities and takeoffs from the current Revit project. Returns detailed information about each material including name, class, area, volume, and element counts. Useful for cost estimation, material ordering, and sustainability analysis.\n\nGUIDANCE:\n- Material takeoff: returns areas and volumes per material across selected categories\n- Cost estimation: combine quantities with material costs\n- Compare alternatives: run for different design options\n\nTIPS:\n- Filter by category for focused takeoffs (e.g. just walls or floors)\n- Values are in project display units (m², m³ or ft², ft³)\n- Use get_materials and get_material_properties for material details",
    {
      categoryFilters: z
        .array(z.string())
        .optional()
        .describe("Optional list of Revit category names to filter by (e.g., ['OST_Walls', 'OST_Floors', 'OST_Roofs']). If not specified, all categories are included."),
      selectedElementsOnly: z
        .boolean()
        .optional()
        .default(false)
        .describe("Whether to only analyze currently selected elements. Defaults to false (analyze entire project)."),
      maxResults: z
        .number()
        .optional()
        .default(50)
        .describe("Maximum materials to return. Default 50."),
    },
    async (args, extra) => {
      const params = {
        categoryFilters: args.categoryFilters ?? null,
        selectedElementsOnly: args.selectedElementsOnly ?? false,
        maxResults: args.maxResults ?? 50,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_material_quantities", params);
        });

        return rawToolResponse("get_material_quantities", response);
      } catch (error) {
        return rawToolError("get_material_quantities", `Get material quantities failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
