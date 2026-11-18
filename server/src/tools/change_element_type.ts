import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerChangeElementTypeTool(server: McpServer) {
  server.tool(
    "change_element_type",
    "Change the family type of one or more elements.",
    {
      elementIds: z
        .array(z.number())
        .describe("Element IDs to change type for"),
      targetTypeId: z
        .number()
        .optional()
        .describe("Target type element ID to change to"),
      targetTypeName: z
        .string()
        .optional()
        .describe("Target type name to search for (used if targetTypeId not provided)"),
      targetFamilyName: z
        .string()
        .optional()
        .describe("Target family name to narrow type search"),
    },
    async (args, extra) => {
      const params = {
        elementIds: args.elementIds,
        targetTypeId: args.targetTypeId ?? 0,
        targetTypeName: args.targetTypeName ?? "",
        targetFamilyName: args.targetFamilyName ?? "",
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("change_element_type", params);
        });
        return rawToolResponse("change_element_type", response);
      } catch (error) {
        return rawToolError("change_element_type", `Change element type failed: ${errorMessage(error)}`);
      }
    }
  );
}
