import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import { storeProject, getProjectByName } from "../database/service.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerStoreProjectDataTool(server: McpServer) {
  server.tool(
    "store_project_data",
    "Store project metadata in the local Revit MCP database.",
    {
      project_name: z.string().describe("The name of the Revit project"),
      project_path: z.string().optional().describe("File path to the project"),
      project_number: z.string().optional().describe("Project number or identifier"),
      project_address: z.string().optional().describe("Project address or location"),
      client_name: z.string().optional().describe("Client name"),
      project_status: z.string().optional().describe("Project status (e.g., Active, Completed, On Hold)"),
      author: z.string().optional().describe("Project author or creator"),
      metadata: z.record(z.string()).optional().describe("Additional project metadata as key-value pairs")
    },
    async (args) => {
      try {
        const projectId = storeProject(args);
        const project = getProjectByName(args.project_name);

        return rawToolResponse("store_project_data", {
          success: true,
          message: "Project data stored successfully",
          project_id: projectId,
          project
        });
      } catch (error) {
        return rawToolError("store_project_data", `Store project data failed: ${errorMessage(error)}`);
      }
    }
  );
}
