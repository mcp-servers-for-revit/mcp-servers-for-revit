import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerGetRoomOpeningsTool(server: McpServer) {
  server.tool(
    "get_room_openings",
    "Find doors and/or windows in rooms with full parameter extraction. Fast phase-aware lookup via FromRoom/ToRoom.",
    {
      roomIds: z
        .array(z.number())
        .optional()
        .describe("Room ElementIds to query. Omit for all rooms."),
      roomNumbers: z
        .array(z.string())
        .optional()
        .describe(
          "Room numbers to query (alternative to roomIds). Omit for all rooms."
        ),
      levelName: z
        .string()
        .optional()
        .describe(
          "Filter rooms by level name (partial match, e.g. 'L2'). Combinable with roomNumbers."
        ),
      elementType: z
        .enum(["doors", "windows", "both"])
        .optional()
        .default("both")
        .describe("Type of openings to find: doors, windows, or both."),
      includeRoomParams: z
        .boolean()
        .optional()
        .default(false)
        .describe("Include room parameters in output."),
      includeElementParams: z
        .boolean()
        .optional()
        .default(false)
        .describe("Include door/window parameters in output."),
      parameterNames: z
        .array(z.string())
        .optional()
        .describe(
          "Specific parameter names to extract (empty = all key parameters)."
        ),
      maxElementsPerRoom: z
        .number()
        .optional()
        .default(100)
        .describe("Max doors/windows per room (default 100)."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_room_openings", {
            roomIds: args.roomIds ?? [],
            roomNumbers: args.roomNumbers ?? [],
            levelName: args.levelName ?? "",
            elementType: args.elementType ?? "both",
            includeRoomParams: args.includeRoomParams ?? false,
            includeElementParams: args.includeElementParams ?? false,
            parameterNames: args.parameterNames ?? [],
            maxElementsPerRoom: args.maxElementsPerRoom ?? 100,
          });
        });
        return rawToolResponse("get_room_openings", response);
      } catch (error) {
        return rawToolError(
          "get_room_openings",
          `Get room openings failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
