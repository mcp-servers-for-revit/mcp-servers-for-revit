import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerSyncCsvParametersTool(server: McpServer) {
  server.tool(
    "sync_csv_parameters",
    "Sync element parameters with a CSV file (import/export/roundtrip).",
    {
      data: z
        .array(
          z.object({
            elementId: z.number().describe("Revit element ID"),
            parameters: z
              .record(
                z.string(),
                z.union([z.string(), z.number(), z.boolean()])
              )
              .describe("Parameter name → value pairs to set"),
          })
        )
        .describe("Array of element update definitions"),
      dryRun: z
        .boolean()
        .optional()
        .default(true)
        .describe(
          "If true (default), only preview changes without applying. Set to false to apply."
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("sync_csv_parameters", args);
        });
        return rawToolResponse("sync_csv_parameters", response);
      } catch (error) {
        return rawToolError("sync_csv_parameters", `Sync CSV parameters failed: ${errorMessage(error)}`);
      }
    }
  );
}
