import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import type { ToolHandle } from "../utils/ToolAvailability.js";

export async function registerTools(
  server: McpServer
): Promise<Map<string, ToolHandle>> {
  // Get the current file's directory path
  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);

  // Capture the RegisteredTool handle returned by every server.tool() call so
  // dynamic tool advertisement can enable/disable tools later. Tool files
  // register by calling server.tool(name, ...); wrapping the method keeps the
  // capture in one place instead of threading handles through 40 files.
  const handles = new Map<string, ToolHandle>();
  const originalTool = (server.tool as (...args: unknown[]) => unknown).bind(server);
  (server as unknown as { tool: (...args: unknown[]) => unknown }).tool = (
    ...args: unknown[]
  ) => {
    const handle = originalTool(...args);
    const name = args[0];
    if (typeof name === "string") {
      handles.set(name, handle as ToolHandle);
    }
    return handle;
  };

  // Read all files in the tools directory
  const files = fs.readdirSync(__dirname);

  // Filter .ts or .js files, excluding the index and register files
  const toolFiles = files.filter(
    (file) =>
      (file.endsWith(".ts") || file.endsWith(".js")) &&
      file !== "index.ts" &&
      file !== "index.js" &&
      file !== "register.ts" &&
      file !== "register.js"
  );

  // Dynamically import and register each tool
  for (const file of toolFiles) {
    try {
      // Build the import path
      const importPath = `./${file.replace(/\.(ts|js)$/, ".js")}`;

      // Dynamically import the module
      const module = await import(importPath);

      // Find and execute the register function
      const registerFunctionName = Object.keys(module).find(
        (key) => key.startsWith("register") && typeof module[key] === "function"
      );

      if (registerFunctionName) {
        module[registerFunctionName](server);
        console.error(`Registered tool: ${file}`);
      } else {
        console.warn(`Warning: no register function found in file ${file}`);
      }
    } catch (error) {
      console.error(`Error registering tool ${file}:`, error);
    }
  }

  (server as unknown as { tool: unknown }).tool = originalTool;
  return handles;
}