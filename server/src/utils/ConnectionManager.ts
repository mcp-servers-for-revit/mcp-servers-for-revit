import { RevitClientConnection } from "./SocketClient.js";
import { refreshVia } from "./ToolAvailability.js";

// Mutex to serialize all Revit connections - prevents race conditions
// when multiple requests are made in parallel
let connectionMutex: Promise<void> = Promise.resolve();

/**
 * Connect to the Revit client and perform an operation
 * @param operation The operation function to execute after connecting
 * @returns The result of the operation
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

  const revitClient = new RevitClientConnection("localhost", 8080);

  try {
    // Connect to the Revit client
    if (!revitClient.isConnected) {
      await new Promise<void>((resolve, reject) => {
        const onConnect = () => {
          revitClient.socket.removeListener("connect", onConnect);
          revitClient.socket.removeListener("error", onError);
          resolve();
        };

        const onError = (error: any) => {
          revitClient.socket.removeListener("connect", onConnect);
          revitClient.socket.removeListener("error", onError);
          reject(new Error("connect to revit client failed"));
        };

        revitClient.socket.on("connect", onConnect);
        revitClient.socket.on("error", onError);

        revitClient.connect();

        setTimeout(() => {
          revitClient.socket.removeListener("connect", onConnect);
          revitClient.socket.removeListener("error", onError);
          reject(new Error("Failed to connect to Revit client"));
        }, 5000);
      });
    }

    // Dynamic tool advertisement: pull a registry snapshot when the cached one
    // is missing/stale. Never fatal - on failure the full tool list stays
    // advertised and the operation proceeds as before.
    await refreshVia(revitClient);

    // Perform the operation
    return await operation(revitClient);
  } finally {
    // Disconnect
    revitClient.disconnect();
    // Release the mutex so the next request can proceed
    releaseMutex!();
  }
}
