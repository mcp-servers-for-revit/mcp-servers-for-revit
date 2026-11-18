import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

const pointSchema = z.object({
  x: z.number().describe("X coordinate in mm"),
  y: z.number().describe("Y coordinate in mm"),
  z: z.number().describe("Z coordinate in mm"),
});

export function registerModifyElementTool(server: McpServer) {
  server.tool(
    "modify_element",
    "Move, rotate, or mirror elements in the model.",
    {
      data: z.object({
        elementIds: z
          .array(z.number())
          .describe("Array of element IDs to modify"),
        action: z
          .enum(["move", "rotate", "mirror", "copy"])
          .describe("The modification action to perform"),
        translation: pointSchema
          .optional()
          .describe("Translation vector in mm (required for 'move' action)."),
        rotationCenter: pointSchema
          .optional()
          .describe(
            "Center point of rotation in mm (required for 'rotate' action)"
          ),
        rotationAngle: z
          .number()
          .optional()
          .describe("Rotation angle in degrees (required for 'rotate' action)"),
        mirrorPlaneOrigin: pointSchema
          .optional()
          .describe(
            "Origin point of the mirror plane in mm (required for 'mirror' action)"
          ),
        mirrorPlaneNormal: pointSchema
          .optional()
          .describe("Normal vector of the mirror plane (required for 'mirror' action)."),
        copyOffset: pointSchema
          .optional()
          .describe(
            "Offset vector for copy in mm (required for 'copy' action)"
          ),
      }),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("modify_element", {
            data: args.data,
          });
        });

        return rawToolResponse("modify_element", response);
      } catch (error) {
        return rawToolError("modify_element", `Modify element failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
