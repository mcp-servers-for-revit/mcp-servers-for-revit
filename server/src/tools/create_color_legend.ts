import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateColorLegendTool(server: McpServer) {
  server.tool(
    "create_color_legend",
    "Color-code elements by parameter and place a color legend on a sheet.",
    {
      parameterName: z.string().describe("Parameter to group and colorize by (e.g. 'Department', 'Area')."),
      categories: z.array(z.string()).optional().describe(
        "Categories to include (e.g. Rooms, Walls, Doors). Default: Rooms."
      ),
      colorScheme: z.enum(["auto", "gradient", "custom"]).optional().describe(
        "'auto' (default), 'gradient' (blue-red ramp), or 'custom'."
      ),
      customColors: z.array(z.object({
        value: z.string().describe("Parameter value string to match."),
        r: z.number().int().min(0).max(255).describe("Red channel 0-255."),
        g: z.number().int().min(0).max(255).describe("Green channel 0-255."),
        b: z.number().int().min(0).max(255).describe("Blue channel 0-255."),
      })).optional().describe("Color mappings for colorScheme='custom'."),
      createLegendView: z.boolean().optional().describe("Create a Legend view showing color swatches and values. Default: true."),
      legendTitle: z.string().optional().describe("Title text for the legend view. Default: 'Color Legend'."),
      targetViewId: z.number().optional().describe("Element ID of the view to apply overrides in. If omitted, uses the active view."),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_color_legend", {
            parameterName: args.parameterName,
            categories: args.categories ?? ["Rooms"],
            colorScheme: args.colorScheme ?? "auto",
            customColors: args.customColors ?? [],
            createLegendView: args.createLegendView ?? true,
            legendTitle: args.legendTitle ?? "Color Legend",
            targetViewId: args.targetViewId ?? 0,
          });
        });
        return rawToolResponse("create_color_legend", response);
      } catch (error) {
        return rawToolError("create_color_legend", `Create color legend failed: ${errorMessage(error)}`);
      }
    }
  );
}
