import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerBatchRenameTool(server: McpServer) {
  server.tool(
    "batch_rename",
    "Batch rename elements using find/replace, prefix, suffix, or regex.",
    {
      elementIds: z
        .array(z.number())
        .optional()
        .describe("Specific element IDs to rename."),
      targetCategory: z
        .enum(["Views", "Sheets", "Levels", "Grids", "Rooms"])
        .optional()
        .describe(
          "Category of elements to rename (used when elementIds is not provided)"
        ),
      findText: z
        .string()
        .optional()
        .describe("Text to find in existing names (for find/replace mode)"),
      replaceText: z
        .string()
        .optional()
        .describe("Replacement text (for find/replace mode)"),
      prefix: z
        .string()
        .optional()
        .describe("Prefix to add to names"),
      suffix: z
        .string()
        .optional()
        .describe("Suffix to add to names"),
      dryRun: z
        .boolean()
        .optional()
        .describe("If true (default), only previews changes without applying."),
    },
    async (args, extra) => {
      const params = {
        elementIds: args.elementIds ?? [],
        targetCategory: args.targetCategory ?? "",
        findText: args.findText ?? "",
        replaceText: args.replaceText ?? "",
        prefix: args.prefix ?? "",
        suffix: args.suffix ?? "",
        dryRun: args.dryRun ?? true,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("batch_rename", params);
        }, 300000);

        return rawToolResponse("batch_rename", response);
      } catch (error) {
        return rawToolError("batch_rename", `Batch rename failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
