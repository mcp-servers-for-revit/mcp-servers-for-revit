import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerFilterByParameterValueTool(server: McpServer) {
  server.tool(
    "filter_by_parameter_value",
    "Filter and select elements by parameter value conditions.",
    {
      categories: z
        .array(z.string())
        .describe(
          "Revit categories to filter, e.g. ['OST_Walls', 'OST_Doors']"
        ),
      parameterName: z
        .string()
        .describe("Parameter name to filter on"),
      condition: z.enum([
        "equals",
        "not_equals",
        "contains",
        "not_contains",
        "begins_with",
        "not_begins_with",
        "ends_with",
        "not_ends_with",
        "greater_than",
        "less_than",
        "is_empty",
        "is_not_empty",
      ]),
      value: z
        .string()
        .optional()
        .describe(
          "Value to compare against. Not required for is_empty/is_not_empty"
        ),
      caseSensitive: z.boolean().optional().default(false),
      scope: z
        .enum(["whole_model", "active_view", "selection"])
        .optional()
        .default("whole_model"),
      parameterType: z
        .enum(["instance", "type", "both"])
        .optional()
        .default("both"),
      returnParameters: z
        .array(z.string())
        .optional()
        .describe(
          "Additional parameters to return for each matched element"
        ),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "filter_by_parameter_value",
            params
          );
        });

        return rawToolResponse("filter_by_parameter_value", response);
      } catch (error) {
        return rawToolError("filter_by_parameter_value", `Filter by parameter value failed: ${errorMessage(error)}`);
      }
    }
  );
}
