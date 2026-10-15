import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerOperateElementTool(server: McpServer) {
  server.tool(
    "operate_element",
    "Operate on Revit elements by performing actions such as select, selectionBox, setColor, setTransparency, delete, hide, etc.",
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
            .describe("The operation to perform on elements. Valid values: Select, SelectionBox, SetColor, SetTransparency, Delete, Hide, TempHide, Isolate, Unhide, ResetIsolate, Highlight. Select enables direct element selection in the active view. SelectionBox allows selection of elements by drawing a rectangular window in the view. SetColor changes the color of elements (requires elementColor parameter). SetTransparency adjusts element transparency (requires transparencyValue parameter). Highlight is a convenience operation that sets elements to red color (internally calls SetColor with red). Delete permanently removes elements from the project. Hide makes elements invisible in the current view until explicitly shown. TempHide temporarily hides elements in the current view. Isolate displays only selected elements while hiding all others. Unhide reveals previously hidden elements. ResetIsolate restores normal visibility to the view."),
          transparencyValue: z
            .number()
            .default(50)
            .describe("Transparency value (0-100) for SetTransparency action. Higher values increase transparency."),
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

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
          isError: reportsFailure(response),
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Operate elements failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
