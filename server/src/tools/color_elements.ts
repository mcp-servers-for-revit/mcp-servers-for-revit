import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerColorElementsTool(server: McpServer) {
  server.tool(
      "color_elements",
      "Colorize elements by parameter value with auto or custom colors.",
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

          // Format the response into a more user-friendly output
          if (response.success) {
            const coloredGroups = response.results || [];

            let resultText = `Successfully colored ${response.totalElements} elements across ${response.coloredGroups} groups.\n\n`;
            resultText += "Parameter Value Groups:\n";

            coloredGroups.forEach((group: any) => {
              const rgb = group.color;
              resultText += `- "${group.parameterValue}": ${group.count} elements colored with RGB(${rgb.r}, ${rgb.g}, ${rgb.b})\n`;
            });

            return rawToolResponse("color_elements", {
              success: true,
              totalElements: response.totalElements,
              coloredGroups: response.coloredGroups,
              results: coloredGroups,
              summary: resultText,
            });
          } else {
            return rawToolError("color_elements", `Color operation failed: ${response.message}`);
          }
        } catch (error) {
          return rawToolError("color_elements", `Color operation failed: ${
                    errorMessage(error)
                }`);
        }
      }
  );
}