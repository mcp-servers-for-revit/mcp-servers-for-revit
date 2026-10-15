import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerColorElementsTool(server: McpServer) {
  server.tool(
      "color_elements",
      "Color elements in the current view based on a category and parameter value. Each unique parameter value gets assigned a distinct color.",
      {
        categoryName: z
            .string()
            .describe("The name of the Revit category to color (e.g., 'Walls', 'Doors', 'Rooms')"),
        parameterName: z
            .string()
            .describe("The name of the parameter to use for grouping and coloring elements"),
        useGradient: z
            .boolean()
            .optional()
            .default(false)
            .describe("Whether to use a gradient color scheme instead of random colors"),
        customColors: z
            .array(
                z.object({
                  r: z.number().int().min(0).max(255),
                  g: z.number().int().min(0).max(255),
                  b: z.number().int().min(0).max(255),
                })
            )
            .optional()
            .describe("Optional array of custom RGB colors to use for specific parameter values"),
      },
      async (args, extra) => {
        const params = args;
        try {
          const response = await withRevitConnection(async (revitClient) => {
            return await revitClient.sendCommand("color_splash", params);
          });

          // Same { Success, Message, Response } shape as the other tools; the add-in returns lowercase fields.
          const { success, message, ...details } = response;
          const result = {
            Success: success === true,
            Message:
              message ??
              (success
                ? `Colored ${details.totalElements} elements in ${details.coloredGroups} groups`
                : "Color operation failed"),
            Response: success ? details : null,
          };

          return {
            content: [
              {
                type: "text",
                text: JSON.stringify(result, null, 2),
              },
            ],
            isError: !result.Success,
          };
        } catch (error) {
          return {
            content: [
              {
                type: "text",
                text: `Color operation failed: ${
                    error instanceof Error ? error.message : String(error)
                }`,
              },
            ],
            isError: true,
          };
        }
      }
  );
}