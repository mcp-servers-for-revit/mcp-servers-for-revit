import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerSectionBoxFromSelectionTool(server: McpServer) {
  server.tool(
    "section_box_from_selection",
    "Set or create a 3D section box fitted around selected elements.",
    {
      elementIds: z
        .array(z.number())
        .optional()
        .describe("Element IDs to fit the section box around."),
      useCurrentSelection: z
        .boolean()
        .optional()
        .describe("If true, uses currently selected elements in Revit."),
      offsetMm: z
        .number()
        .optional()
        .describe("Offset buffer in mm around the bounding box of elements. Default: 1000mm."),
      duplicateView: z
        .boolean()
        .optional()
        .describe("If true, creates a new 3D view."),
      viewName: z
        .string()
        .optional()
        .describe("Name for the new 3D view (only if duplicateView=true)."),
      isolateElements: z
        .boolean()
        .optional()
        .describe("If true, temporarily isolates elements in the view. Default: false."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("section_box_from_selection", {
            elementIds: args.elementIds ?? [],
            useCurrentSelection: args.useCurrentSelection ?? (args.elementIds === undefined || args.elementIds.length === 0),
            offsetMm: args.offsetMm ?? 1000,
            duplicateView: args.duplicateView ?? true,
            viewName: args.viewName ?? "",
            isolateElements: args.isolateElements ?? false,
          });
        });
        return rawToolResponse("section_box_from_selection", response);
      } catch (error) {
        return rawToolError("section_box_from_selection", `Section box from selection failed: ${errorMessage(error)}`);
      }
    }
  );
}
