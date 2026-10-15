import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { reportsFailure } from "../utils/toolResults.js";

export function registerAIElementFilterTool(server: McpServer) {
  server.tool(
    "ai_element_filter",
    "An intelligent Revit element querying tool designed specifically for AI assistants to retrieve detailed element information from Revit projects. This tool allows the AI to request elements matching specific criteria (such as category, type, visibility, or spatial location) and then perform further analysis on the returned data to answer complex user queries about Revit model elements. Example: When a user asks 'Find all walls taller than 5m in the project', the AI would: 1) Call this tool with parameters: {\"filterCategory\": \"OST_Walls\", \"includeInstances\": true}, 2) Receive detailed information about all wall instances in the project, 3) Process the returned data to filter walls with height > 5000mm, 4) Present the filtered results to the user with relevant details.",
    {
      data: z.object({
        filterCategory: z
          .string()
          .optional()
          .describe("Enumeration of built-in element categories in Revit used for filtering and identifying specific element types (e.g., OST_Walls, OST_Floors, OST_GenericModel). Note that furniture elements may be classified as either OST_Furniture or OST_GenericModel categories, requiring flexible selection approaches"),
        filterElementType: z
          .string()
          .optional()
          .describe("The Revit element type name used for filtering specific elements by their class or type (e.g., 'Wall', 'Floor', 'Autodesk.Revit.DB.Wall'). Gets or sets the name of the Revit element type to be filtered."),
        filterFamilySymbolId: z
          .number()
          .optional()
          .describe("The ElementId of a specific FamilySymbol (type) in Revit used for filtering elements by their type (e.g., '123456', '789012'). Gets or sets the ElementId of the FamilySymbol to be used as a filter criterion. Use '-1' if no specific FamilySymbol filtering is needed."),
        includeTypes: z
          .boolean()
          .default(false)
          .describe("Determines whether to include element types (such as wall types, door types, etc.) in the selection results. When set to true, element types will be included; when false, they will be excluded."),
        includeInstances: z
          .boolean()
          .default(true)
          .describe("Determines whether to include element instances (such as placed walls, doors, etc.) in the selection results. When set to true, element instances will be included; when false, they will be excluded."),
        filterVisibleInCurrentView: z
          .boolean()
          .optional()
          .describe("Determines whether to only return elements that are visible in the current view. When set to true, only elements visible in the current view will be returned. Note: This filter only applies to element instances, not type elements."),
        boundingBoxMin: z
          .object({
            x: z.number().describe("X coordinate in mm"),
            y: z.number().describe("Y coordinate in mm"),
            z: z.number().describe("Z coordinate in mm"),
          })
          .optional()
          .describe("The minimum point coordinates (in mm) for spatial bounding box filtering. When set along with boundingBoxMax, only elements that intersect with this bounding box will be returned. Set to null to disable this filter."),
        boundingBoxMax: z
          .object({
            x: z.number().describe("X coordinate in mm"),
            y: z.number().describe("Y coordinate in mm"),
            z: z.number().describe("Z coordinate in mm"),
          })
          .optional()
          .describe("The maximum point coordinates (in mm) for spatial bounding box filtering. When set along with boundingBoxMin, only elements that intersect with this bounding box will be returned. Set to null to disable this filter."),
          maxElements: z
          .number()
          .optional()
          .describe("The maximum number of elements to find in a single tool invocation. Default is 50. Values exceeding 50 are not recommended for performance reasons."),
      })
        .describe("Configuration parameters for the Revit element filter tool. These settings determine which elements will be selected from the Revit project based on various filtering criteria. Multiple filters can be combined to achieve precise element selection. All spatial coordinates should be provided in millimeters."),
    },
    async (args, extra) => {
      const params = args;

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand(
            "ai_element_filter",
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
              text: `Get element information failed: ${error instanceof Error ? error.message : String(error)
                }`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
