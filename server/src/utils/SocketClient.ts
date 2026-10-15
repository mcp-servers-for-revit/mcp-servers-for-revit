import * as net from "net";

interface PendingRequest {
  resolve: (result: any) => void;
  reject: (error: Error) => void;
  timer: NodeJS.Timeout;
}

export class RevitClientConnection {
  host: string;
  port: number;
  token?: string;
  socket: net.Socket;
  isConnected: boolean = false;
  pendingRequests: Map<string, PendingRequest> = new Map();
  buffer: string = "";

  constructor(host: string, port: number, token?: string) {
    this.host = host;
    this.port = port;
    this.token = token;
    this.socket = new net.Socket();
    this.setupSocketListeners();
  }

  private setupSocketListeners(): void {
    // Decode across chunk boundaries so multi-byte characters split between packets survive.
    this.socket.setEncoding("utf8");

    this.socket.on("connect", () => {
      this.isConnected = true;
    });

    this.socket.on("data", (chunk: string) => {
      // 将接收到的数据添加到缓冲区
      this.buffer += chunk;
      this.processBuffer(chunk.includes("\n"));
    });

    this.socket.on("close", () => {
      this.isConnected = false;
      this.rejectAll(new Error("Revit closed the connection before responding"));
    });

    this.socket.on("error", (error) => {
      console.error("RevitClientConnection error:", error);
      this.isConnected = false;
    });
  }

  private processBuffer(hasNewline: boolean): void {
    // Each response is one line of JSON.
    if (hasNewline) {
      let newlineIndex: number;
      while ((newlineIndex = this.buffer.indexOf("\n")) !== -1) {
        const line = this.buffer.slice(0, newlineIndex).trim();
        this.buffer = this.buffer.slice(newlineIndex + 1);
        if (!line) {
          continue;
        }

        let response: any;
        try {
          response = JSON.parse(line);
        } catch (error) {
          this.rejectAll(
            new Error(
              `Revit sent a response that isn't valid JSON: ${
                error instanceof Error ? error.message : String(error)
              }`
            )
          );
          continue;
        }
        this.handleResponse(response);
      }
    }

    // Plugins released before newline framing send one JSON object with no line break.
    const pending = this.buffer.trim();
    if (pending.endsWith("}")) {
      let response: any;
      try {
        response = JSON.parse(pending);
      } catch {
        // 数据不完整，继续等待更多数据
        return;
      }
      this.buffer = "";
      this.handleResponse(response);
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

  private handleResponse(response: any): void {
    const error = response.error
      ? new Error(response.error.message || "Unknown error from Revit")
      : undefined;

    // Errors for requests Revit couldn't read (bad JSON, oversized) come back without an id.
    if (response.id == null) {
      if (error) {
        this.rejectAll(error);
      }
      return;
    }

    const requestId = String(response.id);
    const pending = this.pendingRequests.get(requestId);
    if (!pending) {
      return;
    }

    this.pendingRequests.delete(requestId);
    clearTimeout(pending.timer);
    if (error) {
      pending.reject(error);
    } else {
      pending.resolve(response.result);
    }
  }

  private rejectAll(error: Error): void {
    for (const pending of this.pendingRequests.values()) {
      clearTimeout(pending.timer);
      pending.reject(error);
    }
    this.pendingRequests.clear();
  }

  public sendCommand(command: string, params: any = {}): Promise<any> {
    return new Promise((resolve, reject) => {
      // 生成请求ID
      const requestId = this.generateRequestId();

      try {
        if (!this.isConnected) {
          this.connect();
        }

        // 创建符合JSON-RPC标准的请求对象
        // "auth" isn't part of JSON-RPC; the plugin checks it before dispatching, and older plugins ignore it.
        const commandObj = {
          jsonrpc: "2.0",
          method: command,
          params: params,
          id: requestId,
          auth: this.token,
        };

        // 设置超时
        const timer = setTimeout(() => {
          if (this.pendingRequests.delete(requestId)) {
            reject(new Error(`Command timed out after 2 minutes: ${command}`));
          }
        }, 120000); // 2分钟超时

        this.pendingRequests.set(requestId, { resolve, reject, timer });

        // 发送命令
        this.socket.write(JSON.stringify(commandObj) + "\n");
      } catch (error) {
        const pending = this.pendingRequests.get(requestId);
        if (pending) {
          clearTimeout(pending.timer);
          this.pendingRequests.delete(requestId);
        }
        reject(error);
      }
    });
  }
}
