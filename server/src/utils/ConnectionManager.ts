import { RevitClientConnection } from "./SocketClient.js";
import { readConnectionInfo } from "./connectionInfo.js";

// Mutex to serialize all Revit connections - prevents race conditions
// when multiple requests are made in parallel
let connectionMutex: Promise<void> = Promise.resolve();

/**
 * 连接到Revit客户端并执行操作
 * @param operation 连接成功后要执行的操作函数
 * @returns 操作的结果
 */
export async function withRevitConnection<T>(
  operation: (client: RevitClientConnection) => Promise<T>
): Promise<T> {
  // Wait for any pending connection to complete before starting a new one
  const previousMutex = connectionMutex;
  let releaseMutex: () => void;
  connectionMutex = new Promise<void>((resolve) => {
    releaseMutex = resolve;
  });
  await previousMutex;

  // Read on every call: the plugin issues a new token each time its listener starts.
  const { port, token } = readConnectionInfo();

  // The plugin listens on IPv4 loopback only; "localhost" can resolve to ::1 first.
  const revitClient = new RevitClientConnection("127.0.0.1", port, token);

  try {
    // 连接到Revit客户端
    if (!revitClient.isConnected) {
      await new Promise<void>((resolve, reject) => {
        const cleanup = () => {
          clearTimeout(timer);
          revitClient.socket.removeListener("connect", onConnect);
          revitClient.socket.removeListener("error", onError);
        };

        const onConnect = () => {
          cleanup();
          resolve();
        };

        const onError = () => {
          cleanup();
          reject(
            new Error(
              `Could not connect to Revit on port ${port}. Make sure Revit is open and the MCP service is switched on (Revit MCP Switch button).`
            )
          );
        };

        const timer = setTimeout(onError, 5000);

        revitClient.socket.on("connect", onConnect);
        revitClient.socket.on("error", onError);

        revitClient.connect();
      });
    }

    // 执行操作
    return await operation(revitClient);
  } finally {
    // 断开连接
    revitClient.disconnect();
    // Release the mutex so the next request can proceed
    releaseMutex!();
  }
}
