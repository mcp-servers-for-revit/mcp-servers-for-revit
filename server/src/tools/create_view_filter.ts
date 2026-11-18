import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateViewFilterTool(server: McpServer) {
  server.tool(
    "create_view_filter",
    "Create, apply, or list view filters with parameter-based rules.",
    {
      action: z
        .enum(["create", "apply", "list"])
        .describe("'create', 'apply', or 'list'."),
      filterName: z
        .string()
        .optional()
        .describe("Name for the new filter or name of existing filter to apply"),
      categoryNames: z
        .array(z.string())
        .optional()
        .describe(
          "Category names to filter (e.g. ['Walls', 'Floors']). Required for 'create'."
        ),
      parameterName: z
        .string()
        .optional()
        .describe("Parameter name to filter by (for 'create')"),
      filterRule: z
        .enum([
          "Equals",
          "DoesNotEqual",
          "IsGreaterThan",
          "IsLessThan",
          "Contains",
          "DoesNotContain",
          "BeginsWith",
          "EndsWith",
          "HasValue",
          "HasNoValue",
        ])
        .optional()
        .describe("Filter rule type (for 'create')"),
      filterValue: z
        .string()
        .optional()
        .describe("Value to filter against (for 'create')"),
      viewId: z
        .number()
        .optional()
        .describe("View ID to apply the filter to (for 'apply', default: active view)"),
      colorR: z.number().optional().describe("Override color Red 0-255"),
      colorG: z.number().optional().describe("Override color Green 0-255"),
      colorB: z.number().optional().describe("Override color Blue 0-255"),
      isVisible: z
        .boolean()
        .optional()
        .describe("Whether filtered elements are visible (default: true)"),
    },
    async (args, extra) => {
      const params = {
        action: args.action,
        filterName: args.filterName ?? "",
        categoryNames: args.categoryNames ?? [],
        parameterName: args.parameterName ?? "",
        filterRule: args.filterRule ?? "",
        filterValue: args.filterValue ?? "",
        viewId: args.viewId ?? 0,
        colorR: args.colorR ?? -1,
        colorG: args.colorG ?? -1,
        colorB: args.colorB ?? -1,
        isVisible: args.isVisible ?? true,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_view_filter", params);
        });

        return rawToolResponse("create_view_filter", response);
      } catch (error) {
        return rawToolError("create_view_filter", `Create view filter failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
