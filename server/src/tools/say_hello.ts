import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerSayHelloTool(server: McpServer) {
  server.tool(
    "say_hello",
    "Display a greeting dialog in Revit. Tests MCP connection.",
    {
      message: z
        .string()
        .optional()
        .describe("Optional custom message to display in the dialog. Defaults to 'Hello MCP!'"),
    },
    async (args, extra) => {
      const params = args;
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("say_hello", params);
        }, 30000);

        return rawToolResponse("say_hello", response);
      } catch (error) {
        return rawToolError("say_hello", `Say hello failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
