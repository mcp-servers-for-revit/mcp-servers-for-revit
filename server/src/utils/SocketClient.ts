import * as net from "net";
import { isCommandAvailable, REGISTERED_COMMANDS_METHOD } from "./ToolAvailability.js";

export class RevitClientConnection {
  host: string;
  port: number;
  socket: net.Socket;
  isConnected: boolean = false;
  responseCallbacks: Map<string, (response: string) => void> = new Map();
  buffer: string = "";

  constructor(host: string, port: number) {
    this.host = host;
    this.port = port;
    this.socket = new net.Socket();
    this.setupSocketListeners();
  }

  private setupSocketListeners(): void {
    this.socket.on("connect", () => {
      this.isConnected = true;
    });

    this.socket.on("data", (data) => {
      // Append the received data to the buffer
      const dataString = data.toString();
      this.buffer += dataString;

      // Try to parse the complete JSON response
      this.processBuffer();
    });

    this.socket.on("close", () => {
      this.isConnected = false;
    });

    this.socket.on("error", (error) => {
      console.error("RevitClientConnection error:", error);
      this.isConnected = false;
    });
  }

  private processBuffer(): void {
    try {
      // Try to parse JSON
      const response = JSON.parse(this.buffer);
      // If parsing succeeds, handle the response and clear the buffer
      this.handleResponse(this.buffer);
      this.buffer = "";
    } catch (e) {
      // If parsing fails, the data may be incomplete; keep waiting for more data
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
    this.socket.end();
    this.isConnected = false;
  }

  private generateRequestId(): string {
    return Date.now().toString() + Math.random().toString().substring(2, 8);
  }

  private handleResponse(responseData: string): void {
    try {
      const response = JSON.parse(responseData);
      // Extract the ID from the response
      const requestId = response.id || "default";

      const callback = this.responseCallbacks.get(requestId);
      if (callback) {
        callback(responseData);
        this.responseCallbacks.delete(requestId);
      }
    } catch (error) {
      console.error("Error parsing response:", error);
    }
  }

  public sendCommand(command: string, params: any = {}): Promise<any> {
    return new Promise((resolve, reject) => {
      try {
        // Dynamic tool advertisement: don't dispatch commands the plugin has
        // reported as unregistered - fail with an actionable message instead
        // of a bare JSON-RPC Method-not-found from the plugin.
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

        if (!this.isConnected) {
          this.connect();
        }

        // Generate a request ID
        const requestId = this.generateRequestId();

        // Create a JSON-RPC compliant request object
        const commandObj = {
          jsonrpc: "2.0",
          method: command,
          params: params,
          id: requestId,
        };

        // Store the callback function
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

        // Send the command
        const commandString = JSON.stringify(commandObj);
        this.socket.write(commandString);

        // Set a timeout
        setTimeout(() => {
          if (this.responseCallbacks.has(requestId)) {
            this.responseCallbacks.delete(requestId);
            reject(new Error(`Command timed out after 2 minutes: ${command}`));
          }
        }, 120000); // 2 minute timeout
      } catch (error) {
        reject(error);
      }
    });
  }
}
