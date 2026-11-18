import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerRenameFamiliesTool(server: McpServer) {
  server.tool(
    "rename_families",
    "Rename loaded families with find/replace or prefix/suffix.",
    {
      operation: z
        .enum(["prefix", "suffix", "find_replace"])
        .describe("Type of rename operation to perform"),
      prefix: z
        .string()
        .optional()
        .describe("Prefix to add (for \"prefix\" operation)"),
      suffix: z
        .string()
        .optional()
        .describe("Suffix to add (for \"suffix\" operation)"),
      findText: z
        .string()
        .optional()
        .describe("Text to find (for \"find_replace\" operation)"),
      replaceText: z
        .string()
        .optional()
        .describe("Replacement text (for \"find_replace\" operation)"),
      categories: z
        .array(z.string())
        .optional()
        .describe("Filter by categories (e.g. [\"Doors\", \"Windows\"])"),
      scope: z
        .enum(["whole_model", "active_view", "selection"])
        .optional()
        .default("whole_model")
        .describe("Scope of elements to consider"),
      renameTypes: z
        .boolean()
        .optional()
        .default(false)
        .describe("If true, also rename family type (FamilySymbol) names"),
      dryRun: z
        .boolean()
        .optional()
        .default(true)
        .describe(
          "If true, only previews changes without applying. Set to false to apply."
        ),
    },
    async (args, extra) => {
      const params = {
        operation: args.operation,
        prefix: args.prefix ?? "",
        suffix: args.suffix ?? "",
        findText: args.findText ?? "",
        replaceText: args.replaceText ?? "",
        categories: args.categories ?? [],
        scope: args.scope ?? "whole_model",
        renameTypes: args.renameTypes ?? false,
        dryRun: args.dryRun ?? true,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("rename_families", params);
        });

        return rawToolResponse("rename_families", response);
      } catch (error) {
        return rawToolError("rename_families", `Rename families failed: ${errorMessage(error)}`);
      }
    }
  );
}
