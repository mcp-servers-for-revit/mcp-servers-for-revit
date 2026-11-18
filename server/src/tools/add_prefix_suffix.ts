import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerAddPrefixSuffixTool(server: McpServer) {
  server.tool(
    "add_prefix_suffix",
    "Add prefix/suffix to parameter values. Preview with dryRun=true.",
    {
      parameterName: z.string().describe("Name of the parameter to modify."),
      prefix: z.string().optional().describe("Prefix to prepend to existing value."),
      suffix: z.string().optional().describe("Suffix to append to existing value."),
      separator: z.string().optional().default("").describe("Separator between prefix/suffix and existing value. Default: empty string."),
      categories: z.array(z.string()).optional().describe("Filter by category names (e.g. ['Walls', 'Doors'])."),
      scope: z.enum(["whole_model", "active_view", "selection"]).optional().default("whole_model").describe("Scope: whole_model, active_view, or selection. Default: whole_model."),
      skipEmpty: z.boolean().optional().default(true).describe("Skip elements with empty parameter value. Default: true."),
      filterValue: z.string().optional().describe("Only modify elements whose parameter value contains this text."),
      dryRun: z.boolean().optional().default(true).describe("If true (default), preview changes without applying. Set to false to execute."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("add_prefix_suffix", {
            parameterName: args.parameterName,
            prefix: args.prefix ?? "",
            suffix: args.suffix ?? "",
            separator: args.separator ?? "",
            categories: args.categories ?? [],
            scope: args.scope ?? "whole_model",
            skipEmpty: args.skipEmpty ?? true,
            filterValue: args.filterValue ?? "",
            dryRun: args.dryRun ?? true,
          });
        });
        return rawToolResponse("add_prefix_suffix", response);
      } catch (error) {
        return rawToolError("add_prefix_suffix", `Add prefix/suffix failed: ${errorMessage(error)}`);
      }
    }
  );
}
