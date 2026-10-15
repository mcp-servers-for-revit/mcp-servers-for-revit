import fs from "fs";
import os from "os";
import path from "path";

const DEFAULT_PORT = 8080;

export interface RevitConnectionInfo {
  port: number;
  token?: string;
}

/**
 * The Revit plugin writes its port and a per-session token here each time its listener starts.
 */
export function getConnectionFilePath(): string {
  const localAppData =
    process.env.LOCALAPPDATA ?? path.join(os.homedir(), "AppData", "Local");
  return path.join(localAppData, "mcp-servers-for-revit", "connection.json");
}

export function readConnectionInfo(): RevitConnectionInfo {
  let raw: any;
  try {
    raw = JSON.parse(
      fs.readFileSync(getConnectionFilePath(), "utf8").replace(/^﻿/, "")
    );
  } catch {
    // No file: the listener isn't running, or the plugin predates tokens and doesn't need one.
    return { port: DEFAULT_PORT };
  }

  return {
    port: Number.isInteger(raw?.port) ? raw.port : DEFAULT_PORT,
    token: typeof raw?.token === "string" ? raw.token : undefined,
  };
}
