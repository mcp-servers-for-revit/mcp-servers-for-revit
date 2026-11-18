import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerOperateElementTool(server: McpServer) {
  server.tool(
    "operate_element",
    "Perform operations: select, color, hide, unhide, pin, unpin, etc.",
    {
      data: z
        .object({
          elementIds: z
            .array(z
              .number()
              .describe("A valid Revit element ID to operate on")
            )
            .describe("Array of Revit element IDs to perform the specified action on"),
          action: z
            .string()
            .describe("The operation to perform on elements."),
          transparencyValue: z
            .number()
            .default(50)
            .describe("Transparency value (0-100) for SetTransparency action."),
          colorValue: z
            .array(z.number())
            .default([255, 0, 0])
            .describe("RGB color values for SetColor action. Default is red [255,0,0].")
        })
        .describe("Parameters for operating on Revit elements with specific actions"),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "operate_element",
            params
          );
        });

        return rawToolResponse("operate_element", response);
      } catch (error) {
        return rawToolError("operate_element", `Operate elements failed: ${errorMessage(error)}`);
      }
    }
  );
}
