import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerCreateViewTemplateTool(server: McpServer) {
  server.tool(
    "create_view_template",
    "Create a new view template from an existing view. Copies all view settings (scale, detail level, visibility, overrides).",
    {
      templateName: z.string().describe("Name for the new view template."),
      sourceViewId: z.number().optional().describe("Source view ID to create template from. Default: active view."),
      sourceViewName: z.string().optional().describe("Source view name (partial match). Alternative to sourceViewId."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_view_template", {
            templateName: args.templateName,
            sourceViewId: args.sourceViewId,
            sourceViewName: args.sourceViewName,
          });
        }, 120000);
        return toolResponse("create_view_template", response);
      } catch (error) {
        return toolError("create_view_template", `Create view template failed: ${errorMessage(error)}`);
      }
    }
  );
}
