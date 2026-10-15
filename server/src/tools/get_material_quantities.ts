import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerGetMaterialQuantitiesTool(server: McpServer) {
  server.tool(
    "get_material_quantities",
    "Calculate material quantities and takeoffs from the current Revit project. Returns detailed information about each material including name, class, area, volume, and element counts. Areas are in m² and volumes in m³. Useful for cost estimation, material ordering, and sustainability analysis.",
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
    },
    async (args, extra) => {
      const params = {
        categoryFilters: args.categoryFilters ?? null,
        selectedElementsOnly: args.selectedElementsOnly ?? false,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_material_quantities", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
          isError: reportsFailure(response),
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Get material quantities failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
