import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerManageProjectParametersTool(server: McpServer) {
  server.tool(
    "manage_project_parameters",
    "Add, list, or delete project parameters with category bindings.",
    {
      action: z
        .enum(["list", "create", "delete", "modify"])
        .describe(
          "list, create, delete, or modify category bindings."
        ),
      parameterName: z
        .string()
        .optional()
        .describe(
          "Parameter name. Required for create/delete/modify."
        ),
      dataType: z
        .enum(["Text", "Integer", "Number", "Length", "Area", "Volume", "YesNo", "URL"])
        .optional()
        .default("Text")
        .describe('Parameter data type. Only used for action="create". Default: "Text".'),
      groupUnder: z
        .string()
        .optional()
        .default("PG_IDENTITY_DATA")
        .describe(
          "Properties panel group. Default: PG_IDENTITY_DATA."
        ),
      isInstance: z
        .boolean()
        .optional()
        .default(true)
        .describe(
          "Instance (true, default) or type (false) parameter."
        ),
      categories: z
        .array(z.string())
        .optional()
        .describe(
          "Category names to bind to (e.g. ['Walls', 'Floors'])."
        ),
      isShared: z
        .boolean()
        .optional()
        .default(false)
        .describe(
          "Reserved for future use. Default: false."
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("manage_project_parameters", {
            action: args.action,
            parameterName: args.parameterName ?? "",
            dataType: args.dataType ?? "Text",
            groupUnder: args.groupUnder ?? "PG_IDENTITY_DATA",
            isInstance: args.isInstance ?? true,
            categories: args.categories ?? [],
            isShared: args.isShared ?? false,
          });
        });
        return rawToolResponse("manage_project_parameters", response);
      } catch (error) {
        return rawToolError("manage_project_parameters", `Manage project parameters failed: ${errorMessage(error)}`);
      }
    }
  );
}
