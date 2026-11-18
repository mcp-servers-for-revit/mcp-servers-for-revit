import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerOverrideGraphicsTool(server: McpServer) {
  server.tool(
    "override_graphics",
    "Override element graphics (color, lineweight, pattern, transparency).",
    {
      elementIds: z
        .array(z.number())
        .describe("Element IDs to override"),
      viewId: z
        .number()
        .optional()
        .describe("View ID (default: active view)"),
      action: z
        .enum(["set", "reset"])
        .optional()
        .describe("set overrides or reset to default (default: set)"),
      projectionLineColor: z
        .object({ r: z.number(), g: z.number(), b: z.number() })
        .optional()
        .describe("Projection line color RGB (0-255)"),
      surfaceForegroundColor: z
        .object({ r: z.number(), g: z.number(), b: z.number() })
        .optional()
        .describe("Surface fill color RGB (0-255)"),
      transparency: z
        .number()
        .optional()
        .describe("Surface transparency (0-100)"),
      halftone: z.boolean().optional().describe("Apply halftone effect"),
      projectionLineWeight: z
        .number()
        .optional()
        .describe("Projection lineweight (1-16)"),
    },
    async (args, extra) => {
      const params: Record<string, unknown> = {
        elementIds: args.elementIds,
        viewId: args.viewId ?? 0,
        action: args.action ?? "set",
      };

      if (args.projectionLineColor) params.projectionLineColor = args.projectionLineColor;
      if (args.surfaceForegroundColor) params.surfaceForegroundColor = args.surfaceForegroundColor;
      if (args.transparency !== undefined) params.transparency = args.transparency;
      if (args.halftone !== undefined) params.halftone = args.halftone;
      if (args.projectionLineWeight !== undefined) params.projectionLineWeight = args.projectionLineWeight;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("override_graphics", params);
        });
        return rawToolResponse("override_graphics", response);
      } catch (error) {
        return rawToolError("override_graphics", `Override graphics failed: ${errorMessage(error)}`);
      }
    }
  );
}
