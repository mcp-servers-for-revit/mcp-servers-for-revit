import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerGetAvailableFamilyTypesTool(server: McpServer) {
  server.tool(
    "get_available_family_types",
    "Get available family types in the current Revit project. You can filter by category and family name, and limit the number of returned types.\n\nGUIDANCE:\n- Find wall types: categoryFilter=\"Walls\" — returns all available wall families and types\n- Find door types: categoryFilter=\"Doors\" — needed before create_point_based_element\n- Search all families: omit filter to see every loaded family type\n\nTIPS:\n- Always call this before creating elements to get exact family/type names\n- Use load_family to load additional families from .rfa files\n- Family names are case-sensitive — copy exact names from results",
    {
      categoryList: z
        .array(z.string())
        .optional()
        .describe(
          "List of Revit category names to filter by (e.g., 'OST_Walls', 'OST_Doors', 'OST_Furniture')"
        ),
      familyNameFilter: z
        .string()
        .optional()
        .describe("Filter family types by family name (partial match)"),
      limit: z
        .number()
        .optional()
        .describe("Maximum number of family types to return"),
      compact: z
        .boolean()
        .optional()
        .default(false)
        .describe("Return summary counts only, without full data arrays. Saves tokens for large results."),
    },
    async (args, extra) => {
      const params = {
        categoryList: args.categoryList || [],
        familyNameFilter: args.familyNameFilter || "",
        limit: args.limit || 100,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "get_available_family_types",
            params
          );
        });

        return toolResponse("get_available_family_types", response, args);
      } catch (error) {
        return toolError("get_available_family_types", `get available family types failed: ${errorMessage(error)}`);
      }
    }
  );
}
