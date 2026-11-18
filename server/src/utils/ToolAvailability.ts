import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { RevitClientConnection } from "./SocketClient.js";

/**
 * Dynamic tool advertisement.
 *
 * The plugin exposes exactly one truth: the set of commands registered in
 * Revit (command sets + plugin built-ins), served by the built-in
 * `get_registered_commands` command. This module mirrors that truth onto the
 * MCP tool list: Revit-backed tools whose command is not registered are
 * disabled (hidden from `tools/list`), and calls against them fail with a
 * clear "not enabled" error instead of a bare JSON-RPC Method-not-found.
 *
 * Offline stability: while the plugin has never been reached (`knownCommands
 * === null`), every tool stays advertised - the stdio client UX must not
 * depend on Revit being online at server startup.
 */

export type ToolHandle = ReturnType<McpServer["tool"]>;

/** JSON-RPC method the plugin serves the registry snapshot with. */
export const REGISTERED_COMMANDS_METHOD = "get_registered_commands";

/**
 * MCP tool name -> Revit command name where they differ. Every other
 * Revit-backed tool dispatches a command of the same name. Local tools are
 * served entirely by the server (sqlite) and never filtered.
 */
const TOOL_COMMAND_OVERRIDES: Record<string, string> = {
  color_elements: "color_splash",
  tag_all_rooms: "tag_rooms",
  tag_all_walls: "tag_walls",
};

const LOCAL_TOOLS = new Set([
  "query_stored_data",
  "store_project_data",
  "store_room_data",
]);

/** Command name dispatched by an MCP tool; null = local (always available). */
export function commandForTool(toolName: string): string | null {
  if (LOCAL_TOOLS.has(toolName)) return null;
  return TOOL_COMMAND_OVERRIDES[toolName] ?? toolName;
}

const REFRESH_TTL_MS = 60_000;

let serverRef: McpServer | null = null;
let toolHandles = new Map<string, ToolHandle>();
let knownCommands: Set<string> | null = null;
let lastRefresh = 0;
/** Old plugin without `get_registered_commands`: stop probing, keep full list. */
let pluginTooOld = false;

export function init(server: McpServer, handles: Map<string, ToolHandle>): void {
  serverRef = server;
  toolHandles = handles;
  const unknown = [...handles.keys()].filter((name) => !(name in TOOL_COMMAND_OVERRIDES) && !LOCAL_TOOLS.has(name));
  // Unknown names default to same-name mapping (the common case) - log for visibility.
  if (unknown.length > 0 && process.env.MCP_DEBUG) {
    console.error(`ToolAvailability: unmapped tools assumed same-name: ${unknown.join(", ")}`);
  }
}

/** Whether a Revit command can be dispatched in the current session. */
export function isCommandAvailable(command: string): boolean {
  return knownCommands === null || knownCommands.has(command);
}

/** Registry snapshot arrived from the plugin: fold it into the tool list. */
export function setKnownCommands(names: string[]): void {
  knownCommands = new Set(names);
  lastRefresh = Date.now();
  apply();
}

/** Force a re-fetch on the next connection (after reload_command_set). */
export function invalidate(): void {
  lastRefresh = 0;
}

/** Stop probing: the plugin predates `get_registered_commands`. */
function markPluginTooOld(): void {
  pluginTooOld = true;
}

function apply(): void {
  if (knownCommands === null || serverRef === null) return;
  let changed = false;
  for (const [name, handle] of toolHandles) {
    const command = commandForTool(name);
    const shouldEnable = command === null || knownCommands.has(command);
    if (shouldEnable !== handle.enabled) {
      // Set the enabled flag directly instead of handle.enable()/disable():
      // those wrap update() and each emit their own tools/list_changed, which
      // floods the client when many tools toggle at once. One notification is
      // sent below after the batch.
      handle.enabled = shouldEnable;
      changed = true;
    }
  }
  if (changed) {
    try {
      serverRef.sendToolListChanged();
    } catch {
      // No client connected yet - the next tools/list is served from state anyway.
    }
  }
}

/**
 * Pull the registry snapshot from the plugin over an established connection
 * when the cached one is missing/stale. Never throws: a failure leaves the
 * full tool list advertised (previous behavior).
 */
export async function refreshVia(client: RevitClientConnection): Promise<void> {
  if (pluginTooOld) return;
  if (knownCommands !== null && Date.now() - lastRefresh < REFRESH_TTL_MS) return;
  try {
    const result = (await client.sendCommand(REGISTERED_COMMANDS_METHOD, {})) as
      | { commands?: unknown }
      | undefined;
    const names = result?.commands;
    if (Array.isArray(names)) {
      setKnownCommands(names.map(String));
    } else {
      markPluginTooOld();
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (message.includes("not found")) {
      markPluginTooOld();
    }
    // Transient failure: keep the current advertisement, retry after TTL.
    lastRefresh = Date.now();
  }
}