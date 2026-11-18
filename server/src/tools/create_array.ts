import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateArrayTool(server: McpServer) {
  server.tool(
    "create_array",
    "Create linear or radial arrays of elements.",
    {
      elementIds: z
        .array(z.number())
        .describe("Element IDs to array"),
      arrayType: z
        .enum(["linear", "radial"])
        .describe("Type of array: 'linear' or 'radial'"),
      count: z
        .number()
        .describe("Number of copies to create (total = original + count)"),
      spacingX: z
        .number()
        .optional()
        .describe("X spacing in mm between copies (for linear array, default: 0)"),
      spacingY: z
        .number()
        .optional()
        .describe("Y spacing in mm between copies (for linear array, default: 0)"),
      spacingZ: z
        .number()
        .optional()
        .describe("Z spacing in mm between copies (for linear array, default: 0)"),
      centerX: z
        .number()
        .optional()
        .describe("X center of rotation in mm (for radial array)"),
      centerY: z
        .number()
        .optional()
        .describe("Y center of rotation in mm (for radial array)"),
      totalAngle: z
        .number()
        .optional()
        .describe("Total angle in degrees for radial array (default: 360)"),
    },
    async (args, extra) => {
      const params = {
        elementIds: args.elementIds,
        arrayType: args.arrayType,
        count: args.count,
        spacingX: args.spacingX ?? 0,
        spacingY: args.spacingY ?? 0,
        spacingZ: args.spacingZ ?? 0,
        centerX: args.centerX ?? 0,
        centerY: args.centerY ?? 0,
        totalAngle: args.totalAngle ?? 360,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_array", params);
        });

        return rawToolResponse("create_array", response);
      } catch (error) {
        return rawToolError("create_array", `Create array failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
