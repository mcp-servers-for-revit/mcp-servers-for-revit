import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerCreateMaterialTool(server: McpServer) {
  server.tool(
    "create_material",
    "Create a new material or duplicate an existing one. Optionally set color, transparency, and class.",
    {
      name: z.string().describe("Name for the new material"),
      duplicateFrom: z
        .string()
        .optional()
        .describe(
          "Name of existing material to duplicate. If omitted, creates a blank material."
        ),
      colorR: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Red component (0-255)"),
      colorG: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Green component (0-255)"),
      colorB: z
        .number()
        .min(0)
        .max(255)
        .optional()
        .describe("Blue component (0-255)"),
      transparency: z
        .number()
        .min(0)
        .max(100)
        .optional()
        .describe("Transparency (0-100)"),
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
      materialClass: z
        .string()
        .optional()
        .describe("Material class (e.g. 'Concrete', 'Metal', 'Wood')"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_material", {
            name: args.name,
            duplicateFrom: args.duplicateFrom,
            colorR: args.colorR,
            colorG: args.colorG,
            colorB: args.colorB,
            transparency: args.transparency,
            shininess: args.shininess,
            smoothness: args.smoothness,
            materialClass: args.materialClass,
          });
        }, 120000);
        return toolResponse("create_material", response);
      } catch (error) {
        return toolError(
          "create_material",
          `Create material failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
