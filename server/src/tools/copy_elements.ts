import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCopyElementsTool(server: McpServer) {
  server.tool(
    "copy_elements",
    "Copy elements with translation offset, optionally to another level.",
    {
      elementIds: z
        .array(z.number())
        .describe("Array of element IDs to copy"),
      sourceViewId: z
        .number()
        .describe("Source view ID where elements currently exist"),
      targetViewId: z
        .number()
        .describe("Target view ID to copy elements to"),
      offsetX: z
        .number()
        .optional()
        .describe("X offset in mm for the copied elements (default: 0)"),
      offsetY: z
        .number()
        .optional()
        .describe("Y offset in mm for the copied elements (default: 0)"),
      offsetZ: z
        .number()
        .optional()
        .describe("Z offset in mm for the copied elements (default: 0)"),
    },
    async (args, extra) => {
      const params = {
        elementIds: args.elementIds,
        sourceViewId: args.sourceViewId,
        targetViewId: args.targetViewId,
        offsetX: args.offsetX ?? 0,
        offsetY: args.offsetY ?? 0,
        offsetZ: args.offsetZ ?? 0,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("copy_elements", params);
        });

        return rawToolResponse("copy_elements", response);
      } catch (error) {
        return rawToolError("copy_elements", `Copy elements failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
