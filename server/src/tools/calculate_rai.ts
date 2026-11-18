import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerCalculateRaiTool(server: McpServer) {
  server.tool(
    "calculate_rai",
    "Calculate Rapporto Aeroilluminante (RAI) — window-to-floor area ratio per Italian building code DM 5/7/1975. Checks compliance against minimum threshold (default 1/8 = 0.125). Returns per-room breakdown with window details, areas in m², and compliance status.",
    {
      roomIds: z
        .array(z.number())
        .optional()
        .describe("Room ElementIds to analyze. Omit for all rooms."),
      roomNumbers: z
        .array(z.string())
        .optional()
        .describe("Room numbers to query (alternative to roomIds)."),
      levelName: z
        .string()
        .optional()
        .describe("Filter rooms by level name (partial match)."),
      minRatio: z
        .number()
        .optional()
        .default(0.125)
        .describe(
          "Minimum acceptable ratio (default 1/8 = 0.125 per DM 5/7/1975). Use 0.1 for offices (1/10)."
        ),
      includeServiceRooms: z
        .boolean()
        .optional()
        .default(false)
        .describe(
          "Include rooms typically exempt from RAI (bagni, corridoi, ripostigli). Default false."
        ),
      phaseName: z
        .string()
        .optional()
        .describe(
          "Phase name for window-room association (partial match). Default: active view phase, then last project phase."
        ),
      ratioOverrides: z
        .record(z.string(), z.number())
        .optional()
        .describe(
          'Per-room-type ratio overrides. Key = keyword in room name, value = minimum ratio. Example: {"cucina": 0.125, "ufficio": 0.1, "camera": 0.125}. First keyword match wins; unmatched rooms use minRatio.'
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("calculate_rai", {
            roomIds: args.roomIds ?? [],
            roomNumbers: args.roomNumbers ?? [],
            levelName: args.levelName ?? "",
            minRatio: args.minRatio ?? 0.125,
            includeServiceRooms: args.includeServiceRooms ?? false,
            phaseName: args.phaseName ?? "",
            ratioOverrides: args.ratioOverrides,
          });
        });
        return toolResponse("calculate_rai", response);
      } catch (error) {
        return toolError(
          "calculate_rai",
          `RAI calculation failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
