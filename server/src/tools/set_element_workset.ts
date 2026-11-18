import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerSetElementWorksetTool(server: McpServer) {
  server.tool(
    "set_element_workset",
    "Move elements to a different workset.",
    {
      requests: z
        .array(
          z.object({
            elementId: z.number().describe("Revit element ID"),
            worksetName: z
              .string()
              .describe("Name of the target workset to move the element to"),
          })
        )
        .describe("Array of workset assignment requests"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_element_workset", {
            requests: args.requests,
          });
        }, 120000);

        return rawToolResponse("set_element_workset", response);
      } catch (error) {
        return rawToolError("set_element_workset", `Set element workset failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
