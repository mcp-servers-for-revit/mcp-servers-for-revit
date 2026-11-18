import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerExportElementsDataTool(server: McpServer) {
  server.tool(
    "export_elements_data",
    `Export elements by category with selected parameters. Returns columns + rows in JSON or CSV.
Default limit is 100 elements. Response includes truncated:true and totalCount when results are limited.

TOKEN OPTIMIZATION: Always specify parameterNames to return only needed columns — this skips expensive parameter discovery and dramatically reduces response size.

GUIDANCE: Core data-extraction workflow:
- Export all doors with Mark, Level, Width, Height:
    categories=["Doors"], parameterNames=["Mark","Level","Width","Height"]
- Get all walls including type info:
    categories=["Walls"], includeTypeParameters=true
- Filter rooms with area > 20 m²:
    categories=["Rooms"], filterParameterName="Area", filterValue="20", filterOperator="greater_than"
- Export ALL parameters of every element in a category:
    categories=["Furniture"], parameterNames=[] (omit for all params)
- Get a CSV ready for Excel editing:
    outputFormat="csv"

QUICK PROMPTS:
- "export all [category] data"          → use that category, omit parameterNames for all params
- "compare [category] type parameters"  → includeTypeParameters=true
- "find elements where [param]=[value]" → filterParameterName + filterValue + filterOperator
- "edit parameters in bulk"             → export first, then pass rows to sync_csv_parameters`,
    {
      categories: z
        .array(z.string())
        .optional()
        .describe(
          "Category names to export (e.g. 'Walls', 'Doors', 'Rooms', 'Furniture'). " +
          "Leave empty to export all model categories (may be slow on large models)."
        ),
      parameterNames: z
        .array(z.string())
        .optional()
        .describe(
          "Parameter names to include as columns (e.g. 'Mark', 'Level', 'Comments'). " +
          "When provided, skips expensive parameter discovery — faster and smaller response. " +
          "Leave empty to export ALL parameters discovered on the elements."
        ),
      includeTypeParameters: z
        .boolean()
        .optional()
        .default(false)
        .describe(
          "When true, also collect parameters from the element's Family Type. " +
          "Useful for comparing type-level data such as Width, Height, or Material."
        ),
      includeElementId: z
        .boolean()
        .optional()
        .default(true)
        .describe(
          "Include an 'ElementId' column in the output. " +
          "Required if you intend to pass the result to sync_csv_parameters."
        ),
      outputFormat: z
        .enum(["json", "csv"])
        .optional()
        .default("json")
        .describe(
          "Output format. 'json' returns an array of objects; 'csv' returns a semicolon-delimited string."
        ),
      maxElements: z
        .number()
        .optional()
        .default(100)
        .describe(
          "Maximum elements to return. Default 100. Use higher values only when needed. The response includes truncated:true and totalCount when limited."
        ),
      filterParameterName: z
        .string()
        .optional()
        .describe("Name of the parameter to filter on (e.g. 'Level', 'Area', 'Comments')."),
      filterValue: z
        .string()
        .optional()
        .describe("Value to compare against. For numeric operators supply the number as a string."),
      filterOperator: z
        .enum(["equals", "contains", "greater_than", "less_than", "not_equals"])
        .optional()
        .default("equals")
        .describe(
          "Comparison operator for the filter. " +
          "'greater_than' and 'less_than' perform numeric comparison on the raw parameter value."
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("export_elements_data", {
            categories: args.categories ?? [],
            parameterNames: args.parameterNames ?? [],
            includeTypeParameters: args.includeTypeParameters ?? false,
            includeElementId: args.includeElementId ?? true,
            outputFormat: args.outputFormat ?? "json",
            maxElements: args.maxElements ?? 100,
            filterParameterName: args.filterParameterName ?? "",
            filterValue: args.filterValue ?? "",
            filterOperator: args.filterOperator ?? "equals",
          });
        }, 300000);
        const enriched = addSuggestions(response, [
          { prompt: "Update these elements with the modified data using sync_csv_parameters", reason: "Export-edit-import workflow" },
          { prompt: "Export this data to Excel for easier editing", reason: "Excel is more convenient for bulk parameter editing" },
        ]);

        return rawToolResponse("export_elements_data", enriched);
      } catch (error) {
        return rawToolError("export_elements_data", `Export elements data failed: ${errorMessage(error)}`);
      }
    }
  );
}
