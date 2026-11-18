import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerSetElementPhaseTool(server: McpServer) {
  server.tool(
    "set_element_phase",
    "Assign created/demolished phase to elements.",
    {
      requests: z
        .array(
          z.object({
            elementId: z.number().describe("Revit element ID"),
            createdPhaseId: z
              .number()
              .optional()
              .describe("Phase ID to set as the created phase for the element"),
            demolishedPhaseId: z
              .number()
              .optional()
              .describe(
                "Phase ID to set as the demolished phase for the element"
              ),
          })
        )
        .describe("Array of phase assignment requests."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_element_phase", {
            requests: args.requests,
          });
        });

        return rawToolResponse("set_element_phase", response);
      } catch (error) {
        return rawToolError("set_element_phase", `Set element phase failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
