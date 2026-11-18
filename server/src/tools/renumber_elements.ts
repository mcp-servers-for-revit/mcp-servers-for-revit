import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerRenumberElementsTool(server: McpServer) {
  server.tool(
    "renumber_elements",
    "Renumber rooms, doors, windows, or parking by location or name.",
    {
      elementIds: z
        .array(z.number())
        .optional()
        .describe("Specific element IDs to renumber. If omitted, uses targetCategory."),
      targetCategory: z
        .enum(["Rooms", "Doors", "Windows", "Parking"])
        .optional()
        .describe("Category of elements to renumber"),
      parameterName: z
        .string()
        .optional()
        .describe("Parameter to set number on. Default: built-in number param."),
      startNumber: z
        .number()
        .optional()
        .describe("Starting number (default: 1)"),
      prefix: z.string().optional().describe("Prefix before the number (e.g., 'R-')"),
      suffix: z.string().optional().describe("Suffix after the number"),
      increment: z.number().optional().describe("Increment between numbers (default: 1)"),
      sortBy: z
        .enum(["location", "name", "none"])
        .optional()
        .describe("'location', 'name', or 'none'."),
      dryRun: z
        .boolean()
        .optional()
        .describe("If true (default), only preview changes. Set to false to apply."),
    },
    async (args, extra) => {
      const params = {
        elementIds: args.elementIds ?? [],
        targetCategory: args.targetCategory ?? "",
        parameterName: args.parameterName ?? "",
        startNumber: args.startNumber ?? 1,
        prefix: args.prefix ?? "",
        suffix: args.suffix ?? "",
        increment: args.increment ?? 1,
        sortBy: args.sortBy ?? "location",
        dryRun: args.dryRun ?? true,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("renumber_elements", params);
        });
        return rawToolResponse("renumber_elements", response);
      } catch (error) {
        return rawToolError("renumber_elements", `Renumber elements failed: ${errorMessage(error)}`);
      }
    }
  );
}
