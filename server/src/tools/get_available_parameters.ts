import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerGetAvailableParametersTool(server: McpServer) {
  server.tool(
    "get_available_parameters",
    "List which parameters one or more Revit elements have (names and metadata only, no values), " +
      "split into instance parameters and type parameters. Only parameters visible in Revit's " +
      "Properties panel / Type Properties dialog are included. Always pass a list of element IDs, even " +
      "for a single element. Use this first to discover parameter names, then call get_parameters to " +
      "read only the values you need, or set_parameters to change them (parameters with IsReadOnly = " +
      "true cannot be set). Result: 'Elements' holds, per element, its InstanceParameters and a TypeId; " +
      "'Types' holds the TypeParameters once per unique TypeId (elements of the same type share them). " +
      "If an element ID is itself a type, all its parameters appear under Types.",
    {
      elementIds: z
        .array(z.string().describe("Revit ElementId, as a string"))
        .min(1)
        .describe("Elements to inspect. Use a single-item list for one element."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_available_parameters", {
            elementIds: args.elementIds,
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
              text: `get available parameters failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
