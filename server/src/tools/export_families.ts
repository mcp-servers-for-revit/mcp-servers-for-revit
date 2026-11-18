import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerExportFamiliesTool(server: McpServer) {
  server.tool(
    "export_families",
    "Export loaded families as .rfa files to a folder.",
    {
      outputDirectory: z
        .string()
        .describe("Folder path to export .rfa files"),
      categories: z
        .array(z.string())
        .optional()
        .describe("Filter by category names (e.g. ['Doors', 'Windows'])."),
      groupByCategory: z
        .boolean()
        .optional()
        .default(true)
        .describe("Create subfolders per category (default: true)"),
      overwrite: z
        .boolean()
        .optional()
        .default(false)
        .describe("Overwrite existing .rfa files (default: false)"),
    },
    async (args, extra) => {
      const params = {
        outputDirectory: args.outputDirectory,
        categories: args.categories ?? [],
        groupByCategory: args.groupByCategory ?? true,
        overwrite: args.overwrite ?? false,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("export_families", params);
        });

        return rawToolResponse("export_families", response);
      } catch (error) {
        return rawToolError("export_families", `Export families failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
