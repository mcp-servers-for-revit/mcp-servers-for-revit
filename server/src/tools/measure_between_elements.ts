import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

const PointSchema = z.object({
  x: z.number().describe("X coordinate in mm"),
  y: z.number().describe("Y coordinate in mm"),
  z: z.number().describe("Z coordinate in mm"),
});

export function registerMeasureBetweenElementsTool(server: McpServer) {
  server.tool(
    "measure_between_elements",
    "Measure distance between two elements or points.",
    {
      elementId1: z.number().optional().describe("First element ID"),
      elementId2: z.number().optional().describe("Second element ID"),
      point1: PointSchema.optional().describe("First point in mm (alternative to elementId1)"),
      point2: PointSchema.optional().describe("Second point in mm (alternative to elementId2)"),
      measureType: z
        .enum(["center_to_center", "closest_points", "bounding_box"])
        .optional()
        .default("center_to_center")
        .describe("Measurement method (default: center_to_center)"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("measure_between_elements", args);
        });
        return rawToolResponse("measure_between_elements", response);
      } catch (error) {
        return rawToolError("measure_between_elements", `Measure failed: ${errorMessage(error)}`);
      }
    }
  );
}
