/**
 * Some Revit commands report a failure inside a normal response rather than as an error:
 * { Success: false, Message } (AIResult) or { success: false, message }.
 * Tools use this to set isError so the AI client knows the call failed.
 */
export function reportsFailure(response: unknown): boolean {
  if (typeof response !== "object" || response === null) {
    return false;
  }

  const result = response as Record<string, unknown>;
  return result.Success === false || result.success === false;
}
