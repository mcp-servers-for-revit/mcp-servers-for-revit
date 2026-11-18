import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerLoadSelectionTool(server: McpServer) {
  server.tool(
    "load_selection",
    "Load a previously saved element selection by name.",
    {
      name: z
        .string()
        .optional()
        .describe("Name of the saved selection to load. If omitted, lists all saved selections"),
      selectInView: z
        .boolean()
        .optional()
        .default(true)
        .describe("If true, select the elements in the current Revit view"),
    },
    async (args, extra) => {
      const params = {
        name: args.name,
        selectInView: args.selectInView ?? true,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("load_selection", params);
        }, 30000);

        return rawToolResponse("load_selection", response);
      } catch (error) {
        return rawToolError("load_selection", `load selection failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
