import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerBulkModifyParameterValuesTool(server: McpServer) {
  server.tool(
    "bulk_modify_parameter_values",
    "Bulk set, prefix, suffix, find/replace, or clear parameter values.",
    {
      elementIds: z.array(z.number()).optional().describe("Element IDs to modify. If omitted, uses categoryName to find elements."),
      categoryName: z.string().optional().describe("Revit category name (e.g. 'Walls', 'Doors'). Used when elementIds not provided."),
      parameterName: z.string().describe("Name of the parameter to modify."),
      operation: z.enum(["set", "prefix", "suffix", "find_replace", "clear"]).describe("set, prefix, suffix, find_replace, or clear."),
      value: z.string().optional().describe("Value to set/prefix/suffix. Required for set, prefix, suffix operations."),
      findText: z.string().optional().describe("Text to find (for find_replace operation)."),
      replaceText: z.string().optional().describe("Replacement text (for find_replace operation)."),
      onlyEmpty: z.boolean().optional().describe("If true, only modify elements where parameter is currently empty."),
      dryRun: z.boolean().optional().default(true).describe("If true (default), preview changes without applying. Set to false to execute."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("bulk_modify_parameter_values", {
            elementIds: args.elementIds ?? [],
            categoryName: args.categoryName ?? "",
            parameterName: args.parameterName,
            operation: args.operation,
            value: args.value ?? "",
            findText: args.findText ?? "",
            replaceText: args.replaceText ?? "",
            onlyEmpty: args.onlyEmpty ?? false,
            dryRun: args.dryRun ?? true,
          });
        }, 300000);
        return rawToolResponse("bulk_modify_parameter_values", response);
      } catch (error) {
        return rawToolError("bulk_modify_parameter_values", `Bulk modify parameter values failed: ${errorMessage(error)}`);
      }
    }
  );
}
