import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerManageUnplacedViewsTool(server: McpServer) {
  server.tool(
    "manage_unplaced_views",
    "List or delete views not placed on any sheet.",
    {
      action: z
        .enum(["list", "delete"])
        .optional()
        .default("list")
        .describe("Action to perform."),
      viewTypes: z
        .array(
          z.enum([
            "FloorPlan",
            "CeilingPlan",
            "Section",
            "Elevation",
            "ThreeDimensional",
            "DraftingView",
            "Legend",
            "AreaPlan",
            "StructuralPlan",
            "Detail",
            "Rendering",
            "Walkthrough",
          ])
        )
        .optional()
        .describe("Filter by view types. If omitted, all view types are included."),
      filterName: z
        .string()
        .optional()
        .describe("Only include views whose name contains this text (case-insensitive)."),
      excludeNames: z
        .array(z.string())
        .optional()
        .describe("Exclude views whose name contains any of these strings."),
      dryRun: z
        .boolean()
        .optional()
        .default(true)
        .describe("Preview only, no actual delete. Default: true."),
      maxResults: z
        .number()
        .optional()
        .default(500)
        .describe("Maximum number of views to return in the response."),
    },
    async (args, extra) => {
      const params = {
        action: args.action ?? "list",
        viewTypes: args.viewTypes ?? [],
        filterName: args.filterName ?? "",
        excludeNames: args.excludeNames ?? [],
        dryRun: args.dryRun ?? true,
        maxResults: args.maxResults ?? 500,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "manage_unplaced_views",
            params
          );
        });

        return rawToolResponse("manage_unplaced_views", response);
      } catch (error) {
        return rawToolError("manage_unplaced_views", `Manage unplaced views failed: ${errorMessage(error)}`);
      }
    }
  );
}
