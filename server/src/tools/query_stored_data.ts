import { errorMessage } from "../utils/errorUtils.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { z } from "zod";
import {
  getAllProjects,
  getProjectById,
  getProjectByName,
  getRoomsByProjectId,
  getAllRoomsWithProject,
  getStats
} from "../database/service.js";
import { rawToolResponse, rawToolError } from "../utils/compactTool.js";

export function registerQueryStoredDataTool(server: McpServer) {
  server.tool(
    "query_stored_data",
    "Query data previously stored via store_project_data.",
    {
      query_type: z.enum([
        "all_projects",
        "project_by_id",
        "project_by_name",
        "rooms_by_project_id",
        "rooms_by_project_name",
        "all_rooms",
        "stats"
      ]).describe("Type of query to perform"),
      project_id: z.number().optional().describe("Project ID (required for 'project_by_id' and 'rooms_by_project_id')"),
      project_name: z.string().optional().describe("Project name (required for 'project_by_name' and 'rooms_by_project_name')")
    },
    async (args) => {
      try {
        let result: unknown;

        switch (args.query_type) {
          case "all_projects":
            result = getAllProjects();
            break;

          case "project_by_id":
            if (!args.project_id) {
              throw new Error("project_id is required for this query type");
            }
            result = getProjectById(args.project_id);
            if (!result) {
              return rawToolResponse("query_stored_data", {
                success: false,
                error: `Project with ID ${args.project_id} not found`
              });
            }
            break;

          case "project_by_name":
            if (!args.project_name) {
              throw new Error("project_name is required for this query type");
            }
            result = getProjectByName(args.project_name);
            if (!result) {
              return rawToolResponse("query_stored_data", {
                success: false,
                error: `Project "${args.project_name}" not found`
              });
            }
            break;

          case "rooms_by_project_id":
            if (!args.project_id) {
              throw new Error("project_id is required for this query type");
            }
            result = getRoomsByProjectId(args.project_id);
            break;

          case "rooms_by_project_name":
            if (!args.project_name) {
              throw new Error("project_name is required for this query type");
            }
            const project = getProjectByName(args.project_name);
            if (!project) {
              return rawToolResponse("query_stored_data", {
                success: false,
                error: `Project "${args.project_name}" not found`
              });
            }
            result = getRoomsByProjectId(project.id);
            break;

          case "all_rooms":
            result = getAllRoomsWithProject();
            break;

          case "stats":
            result = getStats();
            break;

          default:
            throw new Error(`Unknown query type: ${args.query_type}`);
        }

        return rawToolResponse("query_stored_data", {
          success: true,
          query_type: args.query_type,
          data: result
        });
      } catch (error) {
        return rawToolError("query_stored_data", `Query failed: ${errorMessage(error)}`);
      }
    }
  );
}
