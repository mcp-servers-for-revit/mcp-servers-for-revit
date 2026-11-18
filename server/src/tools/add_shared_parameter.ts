import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerAddSharedParameterTool(server: McpServer) {
  server.tool(
    "add_shared_parameter",
    "Add a shared parameter from the shared parameter file to categories.",
    {
      parameterName: z
        .string()
        .describe("Name of the shared parameter to add."),
      groupName: z
        .string()
        .describe("Group name in the shared parameter file."),
      categories: z
        .array(z.string())
        .describe("Category names to bind to (e.g. ['Walls', 'Doors'])."),
      isInstance: z
        .boolean()
        .optional()
        .default(true)
        .describe("If true, binds as an instance parameter."),
      parameterGroup: z
        .string()
        .optional()
        .describe("Display group for the parameter in the element properties panel."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("add_shared_parameter", {
            parameterName: args.parameterName,
            groupName: args.groupName,
            categories: args.categories,
            isInstance: args.isInstance,
            parameterGroup: args.parameterGroup,
          });
        });

        return rawToolResponse("add_shared_parameter", response);
      } catch (error) {
        return rawToolError("add_shared_parameter", `Add shared parameter failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
