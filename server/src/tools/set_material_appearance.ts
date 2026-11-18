import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerSetMaterialAppearanceTool(server: McpServer) {
  server.tool(
    "set_material_appearance",
    "Modify the visual appearance of a material: shading color, transparency, shininess, smoothness, pattern colors, and rendering properties (diffuse color, glossiness).",
    {
      materialId: z
        .number()
        .optional()
        .describe("Material element ID. Provide this OR materialName."),
      materialName: z
        .string()
        .optional()
        .describe(
          "Material name (case-insensitive). Use with materialId as alternative."
        ),
      // Graphic/shading properties
      colorR: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Shading color red (0-255)"),
      colorG: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Shading color green (0-255)"),
      colorB: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Shading color blue (0-255)"),
      transparency: z
        .number()
        .min(0)
        .max(100)
        .optional()
        .describe("Transparency (0=opaque, 100=transparent)"),
      shininess: z
        .number()
        .min(0)
        .max(128)
        .optional()
        .describe("Shininess (0-128)"),
      smoothness: z
        .number()
        .min(0)
        .max(100)
        .optional()
        .describe("Smoothness (0-100)"),
      useRenderAppearanceForShading: z
        .boolean()
        .optional()
        .describe("Use render appearance for realistic shading"),
      // Pattern colors
      surfacePatternColorR: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Surface pattern color red"),
      surfacePatternColorG: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Surface pattern color green"),
      surfacePatternColorB: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Surface pattern color blue"),
      cutPatternColorR: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Cut pattern color red"),
      cutPatternColorG: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Cut pattern color green"),
      cutPatternColorB: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Cut pattern color blue"),
      // Rendering appearance
      renderColorR: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Render diffuse color red"),
      renderColorG: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Render diffuse color green"),
      renderColorB: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Render diffuse color blue"),
      renderTransparency: z
        .number()
        .min(0)
        .max(1)
        .optional()
        .describe("Render transparency (0.0-1.0)"),
      renderGlossiness: z
        .number()
        .min(0)
        .max(1)
        .optional()
        .describe("Render glossiness (0.0-1.0)"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "set_material_appearance",
            args
          );
        }, 120000);
        return toolResponse("set_material_appearance", response);
      } catch (error) {
        return toolError(
          "set_material_appearance",
          `Set material appearance failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
