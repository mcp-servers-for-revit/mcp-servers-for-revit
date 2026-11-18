#!/usr/bin/env node
/**
 * Generates tool-schemas.txt from the MCP server's registered tools.
 * Cross-platform: uses spawn + stdin piping (no shell dependency).
 * Output: ../tool-schemas.txt (project root)
 */
import { spawn } from "child_process";
import { writeFileSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const serverEntry = join(__dirname, "build", "index.js");
const outputPath = join(__dirname, "..", "tool-schemas.txt");
const jsonOutputPath = join(__dirname, "..", "plugin", "tool_schemas.json");

const child = spawn(process.execPath, [serverEntry, "--offline"], {
  stdio: ["pipe", "pipe", "pipe"],
});

child.stderr.pipe(process.stderr);
let stdout = "";
child.stdout.on("data", (chunk) => (stdout += chunk));

// MCP requires initialize handshake before tools/list
const initialize = JSON.stringify({
  jsonrpc: "2.0",
  id: 0,
  method: "initialize",
  params: {
    protocolVersion: "2024-11-05",
    capabilities: {},
    clientInfo: { name: "schema-gen", version: "1.0.0" },
  },
});

const toolsList = JSON.stringify({
  jsonrpc: "2.0",
  id: 1,
  method: "tools/list",
  params: {},
});

child.stdin.write(initialize + "\n");
child.stdin.write(toolsList + "\n");
child.stdin.end();

child.on("close", () => {
  // stdout may contain multiple JSON-RPC responses (one per line or concatenated)
  const responses = stdout
    .split("\n")
    .filter((l) => l.trim().startsWith("{"))
    .map((l) => JSON.parse(l));

  const toolsResponse = responses.find(
    (r) => r.id === 1 && r.result?.tools
  );
  if (!toolsResponse) {
    console.error("Failed to get tools list from server");
    process.exit(1);
  }

  const lines = toolsResponse.result.tools
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((t) => {
      const props = t.inputSchema?.properties || {};
      const required = new Set(t.inputSchema?.required || []);

      const params = Object.entries(props)
        .map(([k, v]) => {
          let sig;
          if (v.type === "object" && v.properties) {
            const sub = Object.entries(v.properties)
              .map(([pk, pv]) => `${pk}:${pv.type || "?"}`)
              .join(",");
            sig = `${k}:{${sub}}`;
          } else if (v.enum) {
            sig = `${k}:${v.enum.join("|")}`;
          } else {
            sig = `${k}:${v.type || "?"}`;
          }
          if (required.has(k)) sig += "!";
          return sig;
        })
        .join(", ");

      return `${t.name}(${params})`;
    });

  writeFileSync(outputPath, lines.join("\n") + "\n");
  console.log(`Generated ${outputPath} with ${lines.length} tools`);

  // Also generate plugin/tool_schemas.json (full schema for Revit chat panel)
  const jsonSchemas = toolsResponse.result.tools
    .sort((a, b) => a.name.localeCompare(b.name))
    .map((t) => ({
      name: t.name,
      description: t.description || "",
      input_schema: t.inputSchema || { type: "object", properties: {} },
    }));

  writeFileSync(jsonOutputPath, JSON.stringify(jsonSchemas, null, 2) + "\n");
  console.log(`Generated ${jsonOutputPath} with ${jsonSchemas.length} tools`);
});
