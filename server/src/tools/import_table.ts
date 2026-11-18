import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerImportTableTool(server: McpServer) {
  server.tool(
    "import_table",
    "Import a table (CSV/JSON) and apply values to element parameters.",
    {
      filePath: z
        .string()
        .describe("Path to CSV or TSV file on disk"),
      delimiter: z
        .enum([",", ";", "\\t"])
        .optional()
        .default(",")
        .describe("Column delimiter"),
      viewType: z
        .enum(["legend", "drafting"])
        .optional()
        .default("drafting")
        .describe("Type of view to create"),
      viewName: z
        .string()
        .optional()
        .describe("Name for the created view. Auto-generated if omitted"),
      scale: z
        .number()
        .optional()
        .default(1)
        .describe("View scale (1:scale)"),
      textSize: z
        .number()
        .optional()
        .default(2.0)
        .describe("Text height in mm"),
      includeHeaders: z
        .boolean()
        .optional()
        .default(true)
        .describe("Treat first row as headers (bold)"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("import_table", args);
        });

        return rawToolResponse("import_table", response);
      } catch (error) {
        return rawToolError("import_table", `Import table failed: ${errorMessage(error)}`);
      }
    }
  );
}
