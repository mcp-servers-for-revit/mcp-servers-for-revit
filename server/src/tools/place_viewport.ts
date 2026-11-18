import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerPlaceViewportTool(server: McpServer) {
  server.tool(
    "place_viewport",
    "Place a view on a sheet at specified coordinates.",
    {
      sheetId: z.number().describe("ID of the sheet to place the viewport on"),
      viewId: z.number().describe("ID of the view to place"),
      positionX: z
        .number()
        .describe("X position on the sheet in mm (from sheet origin)"),
      positionY: z
        .number()
        .describe("Y position on the sheet in mm (from sheet origin)"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("place_viewport", args);
        });

        return rawToolResponse("place_viewport", response);
      } catch (error) {
        return rawToolError("place_viewport", `Place viewport failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
