import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const serverDir = path.resolve(here, "..");
const entry = path.join(serverDir, "build", "index.js");

const transport = new StdioClientTransport({
  command: process.execPath,
  args: [entry],
  cwd: serverDir,
  stderr: "pipe",
});

const client = new Client({ name: "revit-mcp-smoke", version: "1.0.0" });

let stderr = "";
transport.stderr?.on("data", (chunk) => {
  stderr += chunk.toString();
});

const timeout = setTimeout(() => {
  console.error("Smoke test timed out");
  console.error(stderr);
  process.exit(1);
}, 30000);

try {
  await client.connect(transport);
  const result = await client.listTools();
  const names = new Set(result.tools.map((tool) => tool.name));

  const required = ["say_hello", "get_current_view_info", "send_code_to_revit"];
  const missing = required.filter((name) => !names.has(name));
  if (missing.length > 0) {
    throw new Error(`Missing tools: ${missing.join(", ")}`);
  }
  if (names.size < 25) {
    throw new Error(`Expected at least 25 tools, got ${names.size}`);
  }

  console.log(`PASS: MCP handshake and tools/list (${names.size} tools)`);
  console.log(`PASS: required tools present: ${required.join(", ")}`);
} finally {
  clearTimeout(timeout);
  await client.close().catch(() => {});
}
