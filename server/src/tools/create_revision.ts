import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerCreateRevisionTool(server: McpServer) {
  server.tool(
    "create_revision",
    "List, create, or assign revisions to sheets.",
    {
      action: z
        .enum(["list", "create", "add_to_sheets"])
        .optional()
        .describe("'list', 'create', or 'add_to_sheets'. Default: list."),
      date: z.string().optional().describe("Revision date string (for create)"),
      description: z.string().optional().describe("Revision description (for create)"),
      issuedBy: z.string().optional().describe("Issued by name (for create)"),
      issuedTo: z.string().optional().describe("Issued to name (for create)"),
      sheetIds: z
        .array(z.number())
        .optional()
        .describe("Sheet IDs to add revision to (for add_to_sheets)"),
    },
    async (args, extra) => {
      const params = {
        action: args.action ?? "list",
        date: args.date ?? "",
        description: args.description ?? "",
        issuedBy: args.issuedBy ?? "",
        issuedTo: args.issuedTo ?? "",
        sheetIds: args.sheetIds ?? [],
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_revision", params);
        });
        return rawToolResponse("create_revision", response);
      } catch (error) {
        return rawToolError("create_revision", `Revision operation failed: ${errorMessage(error)}`);
      }
    }
  );
}
