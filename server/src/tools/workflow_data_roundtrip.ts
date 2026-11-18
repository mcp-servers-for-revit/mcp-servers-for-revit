import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions } from "../utils/suggestions.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerWorkflowDataRoundtripTool(server: McpServer) {
  server.tool(
    "workflow_data_roundtrip",
    "Export parameters to CSV, edit externally, and re-import.",
    {
      categories: z.array(z.string()).optional()
        .describe("Category names to export (e.g. 'Walls', 'Doors'). Empty = all categories."),
      parameterNames: z.array(z.string()).optional()
        .describe("Parameter names to include. Empty = all discovered parameters."),
      includeTypeParameters: z.boolean().optional().default(false)
        .describe("Include type-level parameters (shown in yellow columns)."),
      filePath: z.string().optional()
        .describe("Output .xlsx path. Default: Desktop/RevitRoundtrip_<timestamp>.xlsx"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("workflow_data_roundtrip", {
            categories: args.categories ?? [],
            parameterNames: args.parameterNames ?? [],
            includeTypeParameters: args.includeTypeParameters ?? false,
            filePath: args.filePath ?? "",
          });
        });

        const data = typeof response === 'object' ? response : {} as any;
        const filePath = (data as any).filePath ?? "";

        const enriched = addSuggestions(response, [
          { prompt: `When you're done editing, ask me to import ${filePath} back into Revit`, reason: "Excel roundtrip: edit the file then re-import using sync_csv_parameters or import_table" },
        ]);

        return rawToolResponse("workflow_data_roundtrip", enriched);
      } catch (error) {
        return rawToolError("workflow_data_roundtrip", `Workflow data roundtrip failed: ${errorMessage(error)}`);
      }
    }
  );
}
