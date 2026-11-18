import { appendFileSync, mkdirSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";

// build/utils/ -> ../../ = server/
const __dirname = dirname(fileURLToPath(import.meta.url));
const LOG_DIR = join(__dirname, "..", "..", "logs");
const LOG_FILE = join(LOG_DIR, "token-usage.jsonl");

// Ensure log directory exists on first import
try {
  mkdirSync(LOG_DIR, { recursive: true });
} catch (e) {
  console.error("[tokenLogger] Failed to create log dir:", LOG_DIR, e);
}

interface TokenLogEntry {
  timestamp: string;
  toolName: string;
  responseChars: number;
  estimatedTokens: number;
  isError: boolean;
}

/**
 * Log a tool response for token usage analysis.
 * Appends one JSON line per call to logs/token-usage.jsonl
 */
export function logTokenUsage(toolName: string, responseText: string, isError: boolean = false): void {
  const chars = responseText.length;
  const entry: TokenLogEntry = {
    timestamp: new Date().toISOString(),
    toolName,
    responseChars: chars,
    estimatedTokens: Math.ceil(chars / 4),
    isError,
  };

  try {
    appendFileSync(LOG_FILE, JSON.stringify(entry) + "\n");
  } catch (e) {
    console.error("[tokenLogger] Failed to write log:", LOG_FILE, e);
  }
}
