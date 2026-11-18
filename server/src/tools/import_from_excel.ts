import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerImportFromExcelTool(server: McpServer) {
  server.tool(
    "import_from_excel",
    "Import data from Excel into Revit element parameters.",
    {
      filePath: z.string().describe("Full path to the .xlsx file to import."),
      sheetName: z.string().optional().describe("Worksheet name. Default: first sheet."),
      dryRun: z.boolean().optional().default(true)
        .describe("Preview changes without modifying the model. Always try this first."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("import_from_excel", {
            filePath: args.filePath,
            sheetName: args.sheetName ?? "",
            dryRun: args.dryRun ?? true,
          });
        }, 300000);
        return rawToolResponse("import_from_excel", response);
      } catch (error) {
        return rawToolError("import_from_excel", `Import from Excel failed: ${errorMessage(error)}`);
      }
    }
  );
}
