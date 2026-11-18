import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerSetMaterialPropertiesTool(server: McpServer) {
  server.tool(
    "set_material_properties",
    "Set identity and product information on Revit materials (Comments, Description, Manufacturer, Model, URL, Cost, Mark, Keynote, Name). Preview with dryRun=true.",
    {
      requests: z
        .array(
          z.object({
            materialId: z.number().describe("Material ElementId"),
            comments: z.string().optional().describe("Material comments/notes"),
            description: z
              .string()
              .optional()
              .describe("Material description / product info"),
            manufacturer: z
              .string()
              .optional()
              .describe("Manufacturer name"),
            model: z.string().optional().describe("Product model name"),
            url: z.string().optional().describe("Product URL"),
            cost: z.number().optional().describe("Material cost"),
            mark: z.string().optional().describe("Mark identifier"),
            keynote: z.string().optional().describe("Keynote value"),
            name: z.string().optional().describe("Rename the material"),
          })
        )
        .describe(
          "Array of materials to update with their new property values"
        ),
      dryRun: z
        .boolean()
        .optional()
        .default(true)
        .describe(
          "If true (default), preview changes without applying. Set to false to execute."
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_material_properties", {
            requests: args.requests,
            dryRun: args.dryRun ?? true,
          });
        });
        return rawToolResponse("set_material_properties", response);
      } catch (error) {
        return rawToolError(
          "set_material_properties",
          `Set material properties failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
