import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateScheduleTool(server: McpServer) {
  server.tool(
    "create_schedule",
    "Create native Revit schedule with all types (regular, key, material takeoff, sheet/view list, etc.), presets, fields, filters, sorting, grouping, and display options.",
    {
      categoryName: z
        .string()
        .describe("BuiltInCategory name like 'OST_Walls', 'OST_Doors', 'OST_Rooms'."),
      name: z.string().optional().describe("Schedule name"),
      preset: z
        .enum([
          "room_finish",
          "door_by_room",
          "window_by_room",
          "wall_summary",
          "material_quantities",
          "family_inventory",
          "sheet_index",
          "view_index",
        ])
        .optional()
        .describe(
          "Preset template that auto-configures fields, filters, sorting, and grouping for common schedule types"
        ),
      scheduleType: z
        .enum([
          "regular",
          "key_schedule",
          "material_takeoff",
          "note_block",
          "sheet_list",
          "view_list",
          "revision_schedule",
          "keynote_legend",
        ])
        .optional()
        .default("regular")
        .describe("Schedule type. Default: regular"),
      familyId: z
        .number()
        .optional()
        .describe("Family ElementId required for note_block schedule type"),
      fields: z
        .array(
          z.object({
            parameterName: z
              .string()
              .describe("Parameter name to add as a schedule field"),
            fieldType: z
              .string()
              .optional()
              .describe("Field type (Instance, Type, Count, Formula, Phasing)"),
            heading: z
              .string()
              .optional()
              .describe("Custom column heading"),
            isHidden: z
              .boolean()
              .optional()
              .describe("Whether the field is hidden"),
            horizontalAlignment: z
              .enum(["Left", "Center", "Right"])
              .optional()
              .describe("Horizontal alignment. Default: Left"),
            gridColumnWidth: z
              .number()
              .optional()
              .describe("Column width in the schedule grid (in sheet units)"),
          })
        )
        .optional()
        .describe("Fields to include in the schedule"),
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
        .describe("Filters to apply to the schedule"),
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
        .describe("Sort/group fields for the schedule"),
      groupFields: z
        .array(
          z.object({
            fieldName: z.string().describe("Field name to group by"),
            sortOrder: z
              .enum(["Ascending", "Descending"])
              .optional()
              .describe("Sort order within the group. Default: Ascending"),
            showHeader: z
              .boolean()
              .optional()
              .describe("Show group header row. Default: true"),
            showFooter: z
              .boolean()
              .optional()
              .describe("Show group footer row with totals. Default: false"),
            showBlankLine: z
              .boolean()
              .optional()
              .describe("Show blank line between groups. Default: false"),
          })
        )
        .optional()
        .describe("Group fields to organize schedule rows into collapsible groups"),
      isItemized: z
        .boolean()
        .optional()
        .describe("If true, show every instance as a separate row; if false, collapse identical rows. Default: true"),
      showGrandTotal: z
        .boolean()
        .optional()
        .describe("Show grand total row at bottom of schedule. Default: false"),
      showGrandTotalCount: z
        .boolean()
        .optional()
        .describe("Show count in the grand total row. Default: false"),
      grandTotalTitle: z
        .string()
        .optional()
        .describe("Custom title for the grand total row (e.g. 'Grand Total')"),
      includeLinkedFiles: z
        .boolean()
        .optional()
        .describe("Include elements from linked Revit files. Default: false"),
      showTitle: z
        .boolean()
        .optional()
        .describe("Show schedule title. Default: true"),
      showHeaders: z
        .boolean()
        .optional()
        .describe("Show column headers. Default: true"),
      showGridLines: z
        .boolean()
        .optional()
        .describe("Show grid lines. Default: true"),
    },
    async (args, extra) => {
      try {
        // Map scheduleType → type for C# ScheduleCreationInfo model
        const { scheduleType, ...rest } = args as any;
        const payload = { ...rest, type: scheduleType ?? "Regular" };

        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_schedule", payload);
        });

        return rawToolResponse("create_schedule", response);
      } catch (error) {
        return rawToolError("create_schedule", `Create schedule failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
