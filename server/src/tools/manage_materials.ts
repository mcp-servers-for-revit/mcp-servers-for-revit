import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

const patternSchema = z
  .object({
    pattern: z.string().optional().describe("Name of a fill pattern in the project"),
    color: z.string().optional().describe("Pattern color as 'R,G,B' (0-255)"),
  })
  .describe("Fill pattern and/or its color");

const materialSchema = z.object({
  id: z
    .string()
    .optional()
    .describe("Revit ElementId of the existing material, as a string (update only)"),
  mainGroup: z.string().optional().describe("NAAKT main group, e.g. 'beton'"),
  characteristic: z.string().optional().describe("NAAKT characteristic, e.g. 'gewapend'"),
  application: z.string().optional().describe("NAAKT application, e.g. 'ihw'"),
  custom: z
    .string()
    .optional()
    .describe(
      "Free tail of the name (e.g. a color or product name). On update an empty string removes the existing tail."
    ),
  color: z.string().optional().describe("Material color as 'R,G,B' (0-255)"),
  surfaceForeground: patternSchema.optional(),
  surfaceBackground: patternSchema.optional(),
  cutForeground: patternSchema.optional(),
  cutBackground: patternSchema.optional(),
  template: z
    .string()
    .optional()
    .describe(
      "Name or ElementId of an existing material to use as template. create: the template is duplicated as a whole (all assets, identity data, graphics), update: material class, color and patterns are copied. In both cases the appearance, physical and thermal assets are shared with the template, never copied. Explicit values in the same item win over the template."
    ),
});

export function registerManageMaterialsTool(server: McpServer) {
  server.tool(
    "manage_materials",
    "Create, list and update Revit materials according to the NAAKT naming convention. A NAAKT name " +
      "is always 'mainGroup_characteristic_application[_custom]', all lowercase, where the first " +
      "three parts come from fixed lists per main group (e.g. beton_gewapend_ihw, " +
      "hout_clt_prefab-element); the optional custom tail is free text. The tool builds the name " +
      "itself from the separate parts and refuses anything that is not valid NAAKT, returning the " +
      "reason and, where possible, a suggestion. " +
      "Actions: 'options' (no mainGroup: list the main groups; with mainGroup: its valid " +
      "characteristics and applications; set includeFillPatterns=true to also get the fill pattern " +
      "names of the project), 'list' (materials with their NAAKT status, color, patterns, material " +
      "class and asset names; filter: all (default), valid, invalid = everything that is not valid " +
      "NAAKT, non-naakt = invalid names whose first part is not even a main group; optional " +
      "nameContains, offset and limit), 'create' (new materials from 'materials') and 'update' " +
      "(existing materials by 'id' from 'updates': rename to NAAKT and/or change color and patterns; " +
      "name parts that are left out are taken from the current name, so a material that is not " +
      "NAAKT yet needs mainGroup, characteristic and application). " +
      "Recommended workflow for changes: use 'list' (and 'options' for missing parts) first, then " +
      "call create/update with dryRun=true, show the user the planned changes and only repeat the " +
      "call without dryRun after the user confirms. Each item is processed independently: an item " +
      "that fails (invalid NAAKT name, name already in use, unknown fill pattern or template) is " +
      "reported with a reason and rolled back, the other items still go through. The result lists " +
      "per item the old and new name, the status (created, updated, unchanged, failed) and what " +
      "changes. If the call times out, split the lists into smaller batches. Does not synchronize " +
      "workshared models.",
    {
      action: z
        .enum(["options", "list", "create", "update"])
        .describe("What to do: options, list, create or update"),
      mainGroup: z.string().optional().describe("options: main group to get characteristics and applications for"),
      includeFillPatterns: z
        .boolean()
        .optional()
        .describe("options: also return the names of the fill patterns in the project"),
      filter: z
        .enum(["all", "valid", "invalid", "non-naakt"])
        .optional()
        .describe("list: which materials to return (default all)"),
      nameContains: z.string().optional().describe("list: only materials whose name contains this text"),
      offset: z.number().int().min(0).optional().describe("list: skip this many materials"),
      limit: z.number().int().min(0).optional().describe("list: return at most this many materials"),
      dryRun: z
        .boolean()
        .optional()
        .describe("create/update: only report what would happen, change nothing in the model"),
      materials: z
        .array(materialSchema)
        .optional()
        .describe("create: the materials to create (mainGroup, characteristic and application are required)"),
      updates: z
        .array(materialSchema)
        .optional()
        .describe("update: the materials to change; each needs an id"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("manage_materials", args);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          isError: true,
          content: [
            {
              type: "text",
              text: `manage materials failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
