import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerAIElementFilterTool(server: McpServer) {
  server.tool(
    "ai_element_filter",
    "An intelligent Revit element querying tool designed specifically for AI assistants to retrieve element information from Revit projects. This tool allows the AI to request elements matching specific criteria (such as category, type, visibility, or spatial location) and then perform further analysis on the returned data to answer user queries about Revit model elements. By default only lightweight base info is returned per element (Id, Name, FamilyName, Category, BuiltInCategory). Omit includeDetails unless the question needs geometry, level or dimension data - extended details make the response much larger. Example 1 (default, no details): When a user asks 'Which door types are used in the project?' or 'How many doors are there?', call this tool with {\"filterCategory\": \"OST_Doors\", \"includeInstances\": true} and group/count the results by Name/FamilyName. Example 2 (details needed): When a user asks 'Find all walls taller than 5m in the project', 1) call this tool with {\"filterCategory\": \"OST_Walls\", \"includeInstances\": true, \"includeDetails\": true}, 2) receive extended information (level, bounding box, thickness/height) about all wall instances, 3) filter walls with height > 5000mm, 4) present the filtered results to the user with relevant details.",
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
            p0: z.object({
              x: z.number().describe("X coordinate of start point"),
              y: z.number().describe("Y coordinate of start point"),
              z: z.number().describe("Z coordinate of start point"),
            }),
            p1: z.object({
              x: z.number().describe("X coordinate of end point"),
              y: z.number().describe("Y coordinate of end point"),
              z: z.number().describe("Z coordinate of end point"),
            }),
          })
          .optional()
          .describe("The minimum point coordinates (in mm) for spatial bounding box filtering. When set along with boundingBoxMax, only elements that intersect with this bounding box will be returned. Set to null to disable this filter."),
        boundingBoxMax: z
          .object({
            p0: z.object({
              x: z.number().describe("X coordinate of start point"),
              y: z.number().describe("Y coordinate of start point"),
              z: z.number().describe("Z coordinate of start point"),
            }),
            p1: z.object({
              x: z.number().describe("X coordinate of end point"),
              y: z.number().describe("Y coordinate of end point"),
              z: z.number().describe("Z coordinate of end point"),
            }),
          })
          .optional()
          .describe("The maximum point coordinates (in mm) for spatial bounding box filtering. When set along with boundingBoxMin, only elements that intersect with this bounding box will be returned. Set to null to disable this filter."),
          maxElements: z
          .number()
          .optional()
          .describe("The maximum number of elements to find in a single tool invocation. Omit for no limit (all matching elements are returned) — advisable to set a limit for large models to avoid performance issues. make sure to set a reasonable limit to get all the elements you need, as the tool will return only up to this number of elements."),
        includeDetails: z
          .boolean()
          .optional()
          .describe("When true, returns extended information per element depending on its kind: level, bounding box (mm), thickness/height, type dimension parameters, room area/volume, view properties, annotation text/dimension values, group/link info. Defaults to false: only Id, Name, FamilyName, Category and BuiltInCategory are returned, which keeps the response small. Only request details when the question actually needs them. FamilyName is omitted when the element itself is a family (its Name already is the family name)."),
        includeInstanceCount: z
          .boolean()
          .optional()
          .describe("Only applies when includeTypes is true. When true, every returned element type gets an InstanceCount: the number of placed instances of that type in the whole project (0 = unused type). The count ignores filterVisibleInCurrentView and bounding box filters. Use this with {\"includeTypes\": true, \"includeInstances\": false} to find which types are actually used (InstanceCount > 0) without fetching every instance, e.g. before running follow-up operations only on used types."),
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
        };
      }
    }
  );
}
