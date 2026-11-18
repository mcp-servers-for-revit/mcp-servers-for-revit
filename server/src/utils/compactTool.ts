import { compactResponse } from "./responseCompactor.js";
import { logTokenUsage } from "./tokenLogger.js";

/**
 * Standard tool response wrapper with compaction and token logging.
 * Use in place of the raw JSON.stringify return in every tool.
 */
export function toolResponse(toolName: string, response: any, args?: { compact?: boolean }) {
  let result = response;

  const compacted = compactResponse(result, {
    compact: args?.compact ?? false,
    stripNulls: true,
    maxArrayItems: 100,
  });
  const text = JSON.stringify(compacted);
  logTokenUsage(toolName, text, false);
  return {
    content: [{ type: "text" as const, text }],
  };
}

export function toolError(toolName: string, message: string) {
  logTokenUsage(toolName, message, true);
  return {
    content: [{ type: "text" as const, text: message }],
    isError: true,
  };
}

/**
 * Drop-in replacement for tools that use raw JSON.stringify.
 * Logs token usage and returns the standard MCP response format.
 */
export function rawToolResponse(toolName: string, response: any) {
  const text = JSON.stringify(response);
  logTokenUsage(toolName, text, false);
  return {
    content: [{ type: "text" as const, text }],
  };
}

export function rawToolError(toolName: string, message: string) {
  logTokenUsage(toolName, message, true);
  return {
    content: [{ type: "text" as const, text: message }],
    isError: true,
  };
}
