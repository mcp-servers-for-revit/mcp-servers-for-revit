import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerListSchedulableFieldsTool(server: McpServer) {
  server.tool(
    "list_schedulable_fields",
    "List all available schedule fields for a given Revit category. Use before creating schedules to discover valid field names.",
    {
      categoryName: z
        .string()
        .describe(
          "BuiltInCategory name, e.g. 'OST_Rooms', 'OST_Doors', 'OST_Walls'"
        ),
      scheduleType: z
        .enum(["regular", "material_takeoff", "key_schedule"])
        .optional()
        .default("regular")
        .describe("Schedule type to query fields for. Default: regular"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("list_schedulable_fields", {
            categoryName: args.categoryName,
            scheduleType: args.scheduleType ?? "regular",
          });
        });
        return rawToolResponse("list_schedulable_fields", response);
      } catch (error) {
        return rawToolError(
          "list_schedulable_fields",
          `List schedulable fields failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
