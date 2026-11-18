import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerSetActiveWorksetTool(server: McpServer) {
  server.tool(
    "set_active_workset",
    "Set the active workset. New elements will be created in this workset.",
    {
      worksetName: z
        .string()
        .describe("Name of the workset to set as active"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_active_workset", {
            worksetName: args.worksetName,
          });
        }, 120000);

        return toolResponse("set_active_workset", response);
      } catch (error) {
        return toolError(
          "set_active_workset",
          `Set active workset failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
