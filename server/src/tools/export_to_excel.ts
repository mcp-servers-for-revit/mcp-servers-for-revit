import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerExportToExcelTool(server: McpServer) {
  server.tool(
    "export_to_excel",
    `Export elements by category to an Excel (.xlsx) file with color-coded columns.
Column colors: green=instance parameter, yellow=type parameter, red=read-only.
File is saved to the specified path or Desktop by default.

GUIDANCE:
- "Export all doors to Excel": categories=["Doors"]
- "Export walls with Mark, Width, Height to Excel": categories=["Walls"], parameterNames=["Mark","Width","Height"]
- "Export everything including type parameters": includeTypeParameters=true
- "Save to specific path": filePath="C:/Exports/doors.xlsx"`,
    {
      categories: z.array(z.string()).optional()
        .describe("Category names to export (e.g. 'Walls', 'Doors'). Empty = all categories."),
      parameterNames: z.array(z.string()).optional()
        .describe("Parameter names to include. Empty = all discovered parameters."),
      includeTypeParameters: z.boolean().optional().default(false)
        .describe("Include type-level parameters (shown in yellow columns)."),
      includeElementId: z.boolean().optional().default(true)
        .describe("Include ElementId column (needed for re-import)."),
      filePath: z.string().optional()
        .describe("Output .xlsx path. Default: Desktop/RevitExport_<timestamp>.xlsx"),
      sheetName: z.string().optional().default("Export")
        .describe("Excel worksheet name."),
      colorCodeColumns: z.boolean().optional().default(true)
        .describe("Color-code header cells: green=instance, yellow=type, red=read-only."),
      maxElements: z.number().optional().default(10000)
        .describe("Max elements to export."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("export_to_excel", {
            categories: args.categories ?? [],
            parameterNames: args.parameterNames ?? [],
            includeTypeParameters: args.includeTypeParameters ?? false,
            includeElementId: args.includeElementId ?? true,
            filePath: args.filePath ?? "",
            sheetName: args.sheetName ?? "Export",
            colorCodeColumns: args.colorCodeColumns ?? true,
            maxElements: args.maxElements ?? 10000,
          });
        }, 300000);
        const data = typeof response === 'object' ? response : {};
        const filePath = data.filePath ?? "";

        const enriched = addSuggestions(response, [
          { prompt: `When you're done editing, ask me to import ${filePath} back into Revit`, reason: "Excel roundtrip: edit the file then re-import" },
        ]);

        return rawToolResponse("export_to_excel", enriched);
      } catch (error) {
        return rawToolError("export_to_excel", `Export to Excel failed: ${errorMessage(error)}`);
      }
    }
  );
}
