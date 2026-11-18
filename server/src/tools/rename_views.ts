import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerRenameViewsTool(server: McpServer) {
  server.tool(
    "rename_views",
    "Rename views using find/replace, prefix, suffix, or regex.",
    {
      operation: z
        .enum(["prefix", "suffix", "find_replace"])
        .describe("Rename operation to apply"),
      prefix: z
        .string()
        .optional()
        .describe("Prefix to add to view names (required for prefix operation)"),
      suffix: z
        .string()
        .optional()
        .describe("Suffix to add to view names (required for suffix operation)"),
      findText: z
        .string()
        .optional()
        .describe("Text to find in view names (required for find_replace operation)"),
      replaceText: z
        .string()
        .optional()
        .describe("Replacement text (required for find_replace operation)"),
      viewTypes: z
        .array(
          z.enum([
            "FloorPlan",
            "CeilingPlan",
            "Section",
            "Elevation",
            "ThreeDimensional",
            "DraftingView",
            "Legend",
            "Schedule",
            "AreaPlan",
            "StructuralPlan",
          ])
        )
        .optional()
        .describe("Filter by view types. If omitted, all view types are included."),
      filterName: z
        .string()
        .optional()
        .describe("Only rename views whose name contains this text"),
      dryRun: z
        .boolean()
        .optional()
        .default(true)
        .describe("If true (default), only previews changes without applying."),
    },
    async (args, extra) => {
      const params = {
        operation: args.operation,
        prefix: args.prefix ?? "",
        suffix: args.suffix ?? "",
        findText: args.findText ?? "",
        replaceText: args.replaceText ?? "",
        viewTypes: args.viewTypes ?? [],
        filterName: args.filterName ?? "",
        dryRun: args.dryRun ?? true,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("rename_views", params);
        });

        return rawToolResponse("rename_views", response);
      } catch (error) {
        return rawToolError("rename_views", `Rename views failed: ${errorMessage(error)}`);
      }
    }
  );
}
