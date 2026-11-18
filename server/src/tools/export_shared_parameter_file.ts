import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerExportSharedParameterFileTool(server: McpServer) {
  server.tool(
    "export_shared_parameter_file",
    "Export the shared parameter file contents as structured data.",
    {
      filePath: z.string().optional()
        .describe("Output .txt path. Default: Desktop/SharedParameters_<timestamp>.txt"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("export_shared_parameter_file", {
            filePath: args.filePath ?? "",
          });
        });
        return rawToolResponse("export_shared_parameter_file", response);
      } catch (error) {
        return rawToolError("export_shared_parameter_file", `Export shared parameter file failed: ${errorMessage(error)}`);
      }
    }
  );
}
