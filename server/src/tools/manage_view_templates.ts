import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerManageViewTemplatesTool(server: McpServer) {
  server.tool(
    "manage_view_templates",
    "List, create, apply, or modify view templates.",
    {
      action: z.enum(["list", "duplicate", "delete", "rename", "batch_rename"])
        .describe("Operation to perform."),
      templateIds: z.array(z.number()).optional()
        .describe("Template element IDs (required for duplicate/delete/rename)."),
      newName: z.string().optional()
        .describe("New name for duplicate or rename operations."),
      findText: z.string().optional()
        .describe("Text to find (for batch_rename)."),
      replaceText: z.string().optional()
        .describe("Replacement text (for batch_rename)."),
      filterViewType: z.string().optional()
        .describe("Filter by view type when listing (e.g. 'FloorPlan', 'Section', 'ThreeD')."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("manage_view_templates", {
            action: args.action,
            templateIds: args.templateIds ?? [],
            newName: args.newName ?? "",
            findText: args.findText ?? "",
            replaceText: args.replaceText ?? "",
            filterViewType: args.filterViewType ?? "",
          });
        });
        return rawToolResponse("manage_view_templates", response);
      } catch (error) {
        return rawToolError("manage_view_templates", `Manage view templates failed: ${errorMessage(error)}`);
      }
    }
  );
}
