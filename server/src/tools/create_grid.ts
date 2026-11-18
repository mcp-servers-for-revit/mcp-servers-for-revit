import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateGridTool(server: McpServer) {
  server.tool(
    "create_grid",
    "Create grid lines (linear or arc) with optional naming pattern.",
    {
      xCount: z
        .number()
        .int()
        .positive()
        .describe("Number of grid lines along X-axis (vertical grids)"),
      xSpacing: z
        .number()
        .positive()
        .describe("Spacing between X-axis grid lines in millimeters"),
      xStartLabel: z
        .string()
        .default("A")
        .describe("Starting label for X-axis grids (e.g., 'A' or '1')"),
      xNamingStyle: z
        .enum(["alphabetic", "numeric"])
        .default("alphabetic")
        .describe("Naming style for X-axis: 'alphabetic' (A,B,C...) or 'numeric' (1,2,3...)"),
      yCount: z
        .number()
        .int()
        .positive()
        .describe("Number of grid lines along Y-axis (horizontal grids)"),
      ySpacing: z
        .number()
        .positive()
        .describe("Spacing between Y-axis grid lines in millimeters"),
      yStartLabel: z
        .string()
        .default("1")
        .describe("Starting label for Y-axis grids (e.g., '1' or 'A')"),
      yNamingStyle: z
        .enum(["alphabetic", "numeric"])
        .default("numeric")
        .describe("Naming style for Y-axis: 'alphabetic' (A,B,C...) or 'numeric' (1,2,3...)"),
      xExtentMin: z
        .number()
        .default(0)
        .describe("Minimum extent along X-axis in mm (where Y-axis grids start)"),
      xExtentMax: z
        .number()
        .default(50000)
        .describe("Maximum extent along X-axis in mm (where Y-axis grids end)"),
      yExtentMin: z
        .number()
        .default(0)
        .describe("Minimum extent along Y-axis in mm (where X-axis grids start)"),
      yExtentMax: z
        .number()
        .default(50000)
        .describe("Maximum extent along Y-axis in mm (where X-axis grids end)"),
      elevation: z
        .number()
        .default(0)
        .describe("Elevation for grid lines in mm (Z-coordinate)"),
      xStartPosition: z
        .number()
        .default(0)
        .describe("Starting position for first X-axis grid in mm"),
      yStartPosition: z
        .number()
        .default(0)
        .describe("Starting position for first Y-axis grid in mm"),
    },
    async (args, extra) => {
      const params = {
        xCount: args.xCount,
        xSpacing: args.xSpacing,
        xStartLabel: args.xStartLabel,
        xNamingStyle: args.xNamingStyle,
        yCount: args.yCount,
        ySpacing: args.ySpacing,
        yStartLabel: args.yStartLabel,
        yNamingStyle: args.yNamingStyle,
        xExtentMin: args.xExtentMin,
        xExtentMax: args.xExtentMax,
        yExtentMin: args.yExtentMin,
        yExtentMax: args.yExtentMax,
        elevation: args.elevation,
        xStartPosition: args.xStartPosition,
        yStartPosition: args.yStartPosition,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_grid", params);
        });

        return rawToolResponse("create_grid", response);
      } catch (error) {
        return rawToolError("create_grid", `Create grid failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
