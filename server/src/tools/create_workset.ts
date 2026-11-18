import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { toolResponse, toolError } from "../utils/compactTool.js";

export function registerCreateWorksetTool(server: McpServer) {
  server.tool(
    "create_workset",
    "Create a new user workset in the current workshared project.",
    {
      name: z
        .string()
        .describe(
          "Name for the new workset. Cannot contain { } [ ] | ; characters."
        ),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_workset", {
            name: args.name,
          });
        }, 120000);

        return toolResponse("create_workset", response);
      } catch (error) {
        return toolError(
          "create_workset",
          `Create workset failed: ${errorMessage(error)}`
        );
      }
    }
  );
}
