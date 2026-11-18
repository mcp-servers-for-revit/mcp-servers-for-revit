import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateLevelTool(server: McpServer) {
  server.tool(
    "create_level",
    "Create new levels at specified elevations.",
    {
      data: z
        .array(
          z.object({
            name: z
              .string()
              .describe("Name of the level (e.g., 'Level 2', 'Roof', 'Basement')"),
            elevation: z
              .number()
              .describe("Elevation of the level in millimeters (mm) from project origin"),
            description: z
              .string()
              .optional()
              .describe("Optional description of the level"),
            isMainLevel: z
              .boolean()
              .default(true)
              .describe("Whether this is a main level (default: true)"),
            isBuildingStory: z
              .boolean()
              .default(true)
              .describe("Whether this level represents a building story (default: true)"),
            computationHeight: z
              .number()
              .optional()
              .describe("Optional computation height in mm"),
            viewPlanOffset: z
              .number()
              .optional()
              .describe("Optional view plan offset in mm"),
            viewSectionOffset: z
              .number()
              .optional()
              .describe("Optional view section offset in mm"),
            viewElevationOffset: z
              .number()
              .optional()
              .describe("Optional view elevation offset in mm"),
            createFloorPlan: z
              .boolean()
              .default(true)
              .describe("Whether to create a floor plan view for this level (default: true)"),
            createCeilingPlan: z
              .boolean()
              .default(true)
              .describe("Whether to create a ceiling plan view for this level (default: true)"),
          })
        )
        .describe("Array of levels to create"),
    },
    async (args, extra) => {
      const params = {
        data: args.data,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_level", params);
        });

        return rawToolResponse("create_level", response);
      } catch (error) {
        return rawToolError("create_level", `Create level failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
