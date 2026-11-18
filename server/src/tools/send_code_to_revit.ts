import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

const transactionModeSchema = z
  .enum(["auto", "none"])
  .default("auto")
  .describe(
    "How the snippet should interact with Revit transactions. Use 'auto' to wrap the snippet in a transaction, or 'none' when the called code manages its own transactions."
  );

export function registerSendCodeToRevitTool(server: McpServer) {
  server.tool(
    "send_code_to_revit",
    [
      "Send C# code to be compiled and executed inside Revit. Preferred for ad-hoc model queries and mutations not covered by a purpose-built tool.",
      "",
      "Execution contract (the snippet is wrapped into a class member of a generated assembly):",
      "- A Document variable named `document` (lowercase) is in scope — e.g. `document.GetElement(id)`. There is no `doc` or `Document` instance variable; `Document` is only the type name.",
      "- An `object[] parameters` array is in scope when the optional `parameters` argument is supplied.",
      "- Return a value with a plain `return ...` statement (string, number, or anonymous/serializable object); it is JSON-serialized and sent back.",
      "- Common usings are pre-imported (System, System.Linq, System.Collections.Generic, Autodesk.Revit.DB, Autodesk.Revit.UI). Reference other namespaces explicitly.",
      "- Any element or type modification requires a transaction: use transactionMode 'auto' (default) unless the code manages its own transaction.",
      "- Units: geometry and all length-like values (coordinates, elevations, widths, wall heights) are in Revit internal units = US feet; inches = feet*12. For other quantity types do not hardcode multipliers — use UnitUtils.ConvertToInternalUnits / ConvertFromInternalUnits.",
      "- Keep code complete and self-contained on first attempt: it is compiled fresh each call, so unresolved identifiers fail compilation.",
      "",
      "Compile errors are reported with 1-based line numbers relative to the submitted snippet.",
      "",
      "If the result carries a 'warnings' field mentioning assembly version conflicts, or the error begins with 'Compile subsystem unavailable', the compile chain collided with another add-in's preloaded assemblies: do NOT keep retrying - fall back to purpose-built tools and tell the user to restart Revit.",
    ].join("\n"),
    {
      code: z
        .string()
        .describe(
          [
            "The C# code to execute inside the generated class member.",
            "In scope: `Document document` (use lowercase `document`) and optional `object[] parameters`.",
            "Return a value with `return` to receive JSON-serialized output.",
          ].join("\n")
        ),
      parameters: z
        .array(z.string())
        .optional()
        .describe(
          "Optional execution parameters that will be passed to your code"
        ),
      transactionMode: transactionModeSchema,
    },
    async (args, extra) => {
      const params = {
        code: args.code,
        parameters: args.parameters || [],
        transactionMode: args.transactionMode,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("send_code_to_revit", params);
        });

        const success =
          response &&
          typeof response === "object" &&
          (response as Record<string, unknown>).success === true;

        const errMsg = !success && response && typeof response === "object"
          ? String((response as Record<string, unknown>).errorMessage ?? "")
          : "";
        const compileSubsystemDown = errMsg.startsWith("Compile subsystem unavailable");

        return {
          content: [
            {
              type: "text",
              text: `${
                success
                  ? "Code execution successful!"
                  : compileSubsystemDown
                    ? "Compile subsystem unavailable - assembly conflict. STOP calling send_code_to_revit for the rest of this Revit session and use purpose-built tools or the off-axis/dwg tools instead."
                    : "Code failed to execute."
              }\nResult: ${JSON.stringify(response, null, 2)}`,
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Code execution failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
