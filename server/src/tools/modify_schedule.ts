import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerModifyScheduleTool(server: McpServer) {
  server.tool(
    "modify_schedule",
    "Modify an existing Revit schedule: add/remove fields, set/clear filters, set/clear sorting, rename, or change display options.",
    {
      scheduleId: z
        .number()
        .optional()
        .describe("Schedule ElementId. Provide either scheduleId or scheduleName."),
      scheduleName: z
        .string()
        .optional()
        .describe("Schedule name (alternative to scheduleId)."),
      action: z
        .enum([
          "add_field",
          "remove_field",
          "set_filters",
          "clear_filters",
          "set_sorting",
          "clear_sorting",
          "rename",
          "set_display_options",
        ])
        .describe("Action to perform on the schedule."),
      fieldNames: z
        .array(z.string())
        .optional()
        .describe("Field/parameter names for add_field or remove_field actions."),
      filters: z
        .array(
          z.object({
            fieldName: z.string().describe("Field name to filter by"),
            filterType: z
              .string()
              .describe("Equal, NotEqual, GreaterThan, LessThan, Contains, etc."),
            filterValue: z.string().describe("Filter value"),
          })
        )
        .optional()
        .describe("Filters to apply for set_filters action."),
      sortFields: z
        .array(
          z.object({
            fieldName: z.string().describe("Field name to sort by"),
            sortOrder: z
              .enum(["Ascending", "Descending"])
              .optional()
              .describe("Sort order. Default: Ascending"),
          })
        )
        .optional()
        .describe("Sort fields for set_sorting action."),
      newName: z
        .string()
        .optional()
        .describe("New name for rename action."),
      showTitle: z
        .boolean()
        .optional()
        .describe("Show schedule title (for set_display_options)."),
      showHeaders: z
        .boolean()
        .optional()
        .describe("Show column headers (for set_display_options)."),
      showGridLines: z
        .boolean()
        .optional()
        .describe("Show grid lines (for set_display_options)."),
      isItemized: z
        .boolean()
        .optional()
        .describe("Show every instance as a separate row (for set_display_options)."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("modify_schedule", {
            scheduleId: args.scheduleId ?? 0,
            scheduleName: args.scheduleName ?? "",
            action: args.action,
            fieldNames: args.fieldNames ?? [],
            filters: args.filters ?? [],
            sortFields: args.sortFields ?? [],
            newName: args.newName ?? "",
            showTitle: args.showTitle,
            showHeaders: args.showHeaders,
            showGridLines: args.showGridLines,
            isItemized: args.isItemized,
          });
        });
        return rawToolResponse("modify_schedule", response);
      } catch (error) {
        return rawToolError(
          "modify_schedule",
          `Modify schedule failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
