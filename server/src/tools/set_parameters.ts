import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSetParametersTool(server: McpServer) {
  server.tool(
    "set_parameters",
    "Set one or more parameter values on one or more Revit elements in a single call. Always pass a " +
      "list of elements, even for a single element (use a list with one entry). Parameters are looked " +
      "up by name (same as Revit's Properties panel), only on the given element itself: to change a " +
      "type parameter, pass the type's ElementId (TypeId), which changes it for all elements of that " +
      "type. Numeric values for length/area/volume parameters " +
      "must be given in the model's display units (e.g. millimeters), not Revit's internal feet. Send " +
      "numeric values (numbers, Yes/No as true/false, ElementIds for element references) as JSON " +
      "numbers/booleans, not as strings; send values for text parameters as strings. Returns, " +
      "per element and per parameter, whether the change succeeded and why not if it failed. If the call " +
      "fails with a timeout (error text containing 'time-out' or 'timed out'), split the elements list " +
      "into smaller batches (e.g. 50-100 elements at a time) and retry with those - this is safe even if " +
      "the timed-out call had already applied some changes, since re-setting a parameter to the same " +
      "value is a no-op.",
    {
      elements: z
        .array(
          z.object({
            elementId: z.string().describe("Revit ElementId of the element to update, as a string"),
            parameters: z
              .array(
                z.object({
                  name: z
                    .string()
                    .describe("Parameter name, as shown in Revit's Properties panel"),
                  value: z
                    .union([z.string(), z.number(), z.boolean()])
                    .describe("New value for this parameter"),
                })
              )
              .min(1)
              .describe("Parameter changes to apply to this element"),
          })
        )
        .min(1)
        .describe(
          "Elements to update, each with its own list of parameter changes. Use a single-item list for one element."
        ),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("set_parameters", {
            elements: args.elements,
          });
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        return {
          isError: true,
          content: [
            {
              type: "text",
              text: `set parameters failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
