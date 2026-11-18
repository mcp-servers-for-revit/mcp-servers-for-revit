import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions, suggestIf } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerClashDetectionTool(server: McpServer) {
  server.tool(
    "clash_detection",
    "Detect geometric intersections (clashes) between two sets of elements. Specify by category names (e.g., 'Ducts' vs 'StructuralFraming') or specific element IDs. Returns pairs of clashing elements.\n\nGUIDANCE:\n- Check wall-pipe clashes: category1=\"Walls\", category2=\"Pipes\"\n- MEP coordination: category1=\"Ducts\", category2=\"Structural Framing\"\n- Full model check: provide two categories to check for intersections\n\nTIPS:\n- Returns pairs of clashing element IDs with intersection details\n- Use operate_element to highlight/isolate clashing elements\n- Focus on critical clashes (structural vs MEP) first\n- Combine with section_box_from_selection to zoom into clash locations",
    {
      categoryA: z
        .string()
        .optional()
        .describe("Category name for set A (e.g., Walls, Ducts, Pipes, StructuralFraming, Columns, Floors)"),
      categoryB: z
        .string()
        .optional()
        .describe("Category name for set B"),
      elementIdsA: z
        .array(z.number())
        .optional()
        .describe("Specific element IDs for set A (overrides categoryA)"),
      elementIdsB: z
        .array(z.number())
        .optional()
        .describe("Specific element IDs for set B (overrides categoryB)"),
      tolerance: z
        .number()
        .optional()
        .describe("Tolerance in mm (default: 0, exact intersection)"),
      maxResults: z
        .number()
        .optional()
        .describe("Maximum number of clashes to return (default: 100)"),
    },
    async (args, extra) => {
      const params = {
        categoryA: args.categoryA ?? "",
        categoryB: args.categoryB ?? "",
        elementIdsA: args.elementIdsA ?? [],
        elementIdsB: args.elementIdsB ?? [],
        tolerance: args.tolerance ?? 0,
        maxResults: args.maxResults ?? 100,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("clash_detection", params);
        }, 300000);

        const data = typeof response === 'object' ? response : {};
        const clashCount = data.clashCount ?? data.clashes?.length ?? 0;

        const enriched = addSuggestions(response, [
          suggestIf(clashCount > 0, "Isolate the clashing elements in the current view", `${clashCount} clashes detected — isolate them for review`),
          suggestIf(clashCount > 0, "Create a section box around the clash area", "Section box helps focus on the clash location in 3D"),
        ]);

        return rawToolResponse("clash_detection", enriched);
      } catch (error) {
        return rawToolError("clash_detection", `Clash detection failed: ${errorMessage(error)}`);
      }
    }
  );
}
