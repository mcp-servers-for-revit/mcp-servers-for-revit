import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateSheetTool(server: McpServer) {
  server.tool(
    "create_sheet",
    "Create a new sheet with a title block.",
    {
      sheetNumber: z
        .string()
        .optional()
        .describe("Sheet number (e.g. 'A101')"),
      sheetName: z.string().optional().describe("Sheet name"),
      titleBlockFamilyName: z
        .string()
        .optional()
        .describe(
          "Title block family name. If not specified, uses the first available title block"
        ),
      titleBlockTypeName: z
        .string()
        .optional()
        .describe("Title block type name within the family"),
      titleBlockTypeId: z
        .number()
        .optional()
        .describe("Title block type ID (alternative to family/type name)"),
    },
    async (args, extra) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_sheet", args);
        });

        return rawToolResponse("create_sheet", response);
      } catch (error) {
        return rawToolError("create_sheet", `Create sheet failed: ${
                errorMessage(error)
              }`);
      }
    }
  );
}
