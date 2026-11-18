import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerSetElementParametersTool(server: McpServer) {
  server.tool(
    "set_element_parameters",
    "Set a parameter value on one or more elements.",
    {
      requests: z
        .array(
          z.object({
            elementId: z.number().describe("Revit element ID"),
            parameterName: z
              .string()
              .describe("Name of the parameter to set"),
            value: z
              .union([z.string(), z.number(), z.boolean()])
              .describe("Value to set."),
          })
        )
        .describe("Array of parameter set requests"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_element_parameters", {
            requests: args.requests,
          });
        });

        return rawToolResponse("set_element_parameters", response);
      } catch (error) {
        return rawToolError("set_element_parameters", `Set element parameters failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
