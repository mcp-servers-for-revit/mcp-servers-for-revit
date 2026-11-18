import { isCommandAvailable, REGISTERED_COMMANDS_METHOD } from "./ToolAvailability.js";
import * as net from "net";
import { randomUUID } from "crypto";

export class RevitClientConnection {
  host: string;
  port: number;
  socket: net.Socket;
  isConnected: boolean = false;
  responseCallbacks: Map<string, (response: string) => void> = new Map();
  private timeoutHandles: Map<string, ReturnType<typeof setTimeout>> = new Map();
  buffer: string = "";
  public defaultTimeout: number = 120000;

  constructor(host: string, port: number) {
    this.host = host;
    this.port = port;
    this.socket = new net.Socket();
    this.socket.setEncoding("utf8");
    this.setupSocketListeners();
  }

  private setupSocketListeners(): void {
    this.socket.on("connect", () => {
      this.isConnected = true;
    });

    this.socket.on("data", (data) => {
      this.buffer += data.toString();
      this.processBuffer();
    });

    this.socket.on("close", () => {
      this.isConnected = false;
      this.cleanupAllPending(new Error("Socket closed"));
    });

    this.socket.on("error", (error) => {
      console.error("RevitClientConnection error:", error);
      this.isConnected = false;
    });
  }

  private processBuffer(): void {
    // Process all complete newline-delimited messages.
    let newlineIndex: number;
    while ((newlineIndex = this.buffer.indexOf("\n")) >= 0) {
      const line = this.buffer.substring(0, newlineIndex).trim();
      this.buffer = this.buffer.substring(newlineIndex + 1);

      if (line.length === 0) continue;

      this.handleResponse(line);
    }

    // Earlier add-ins send one JSON object without a trailing newline.
    if (this.buffer.trimEnd().endsWith("}")) {
      try { JSON.parse(this.buffer); } catch { return; }
      const complete = this.buffer;
      this.buffer = "";
      this.handleResponse(complete);
    }
  }

  public connect(): boolean {
    if (this.isConnected) {
      return true;
    }

    try {
      this.socket.connect(this.port, this.host);
      return true;
    } catch (error) {
      console.error("Failed to connect:", error);
      return false;
    }
  }

  public disconnect(): void {
    this.socket.destroy();
    this.isConnected = false;
    this.cleanupAllPending(new Error("Disconnected from Revit"));
  }

  private cleanupAllPending(error: Error): void {
    for (const timeoutHandle of this.timeoutHandles.values()) {
      clearTimeout(timeoutHandle);
    }
    this.timeoutHandles.clear();

    const pendingCallbacks = Array.from(this.responseCallbacks.values());
    this.responseCallbacks.clear();
    for (const callback of pendingCallbacks) {
      try {
        callback(JSON.stringify({ error: { message: error.message } }));
      } catch {
        // Ignore errors from already-settled promises.
      }
    }
  }

  private generateRequestId(): string {
    return randomUUID();
  }

  private handleResponse(responseData: string): void {
    try {
      const response = JSON.parse(responseData);
      const requestId = response.id || "default";

      const callback = this.responseCallbacks.get(requestId);
      if (callback) {
        // Clear the timeout for this request.
        const timeoutHandle = this.timeoutHandles.get(requestId);
        if (timeoutHandle) {
          clearTimeout(timeoutHandle);
          this.timeoutHandles.delete(requestId);
        }
        callback(responseData);
        this.responseCallbacks.delete(requestId);
      }
    } catch (error) {
      console.error("Error parsing response:", error);
    }
  }

  public sendCommand(command: string, params: any = {}, timeoutMs?: number): Promise<any> {
    const timeout = timeoutMs ?? this.defaultTimeout;
    return new Promise((resolve, reject) => {
      try {
        if (!this.isConnected) {
          this.connect();
        }

        if (
          command !== REGISTERED_COMMANDS_METHOD &&
          !isCommandAvailable(command)
        ) {
          reject(
            new Error(
              `Command '${command}' is not enabled in the current Revit session ` +
                `(command set not loaded or disabled). Enable it in the Revit MCP ` +
                `Settings window or run reload_command_set, then retry.`
            )
          );
          return;
        }

        const requestId = this.generateRequestId();

        const commandObj = {
          jsonrpc: "2.0",
          method: command,
          params: params,
          id: requestId,
        };

        // Store callback
        this.responseCallbacks.set(requestId, (responseData) => {
          try {
            const response = JSON.parse(responseData);
            if (response.error) {
              reject(
                new Error(response.error.message || "Unknown error from Revit")
              );
            } else {
              resolve(response.result);
            }
          } catch (error) {
            if (error instanceof Error) {
              reject(new Error(`Failed to parse response: ${error.message}`));
            } else {
              reject(new Error(`Failed to parse response: ${String(error)}`));
            }
          }
        });

        // Send command with newline delimiter.
        const commandString = JSON.stringify(commandObj) + "\n";
        this.socket.write(commandString);

        // Set timeout with cleanup.
        const timeoutHandle = setTimeout(() => {
          if (this.responseCallbacks.has(requestId)) {
            this.responseCallbacks.delete(requestId);
            this.timeoutHandles.delete(requestId);
            reject(new Error(`Command '${command}' timed out after ${Math.round(timeout / 1000)}s. For large models, try filtering by category or reducing scope.`));
            this.socket.destroy();
          }
        }, timeout);
        this.timeoutHandles.set(requestId, timeoutHandle);
      } catch (error) {
        reject(error);
      }
    });
  }
}
