/**
 * Global response compactor utility for MCP server.
 * Reduces token usage in tool responses by stripping empty values,
 * truncating large arrays, and providing compact summaries.
 */

/**
 * Strip null, undefined, empty string, and empty array values from an object recursively.
 * Reduces JSON size significantly for sparse Revit data.
 * Does not strip 0 or false (they are valid values).
 */
function stripEmpty(obj: any): any {
  if (obj === null || obj === undefined) return undefined;
  if (Array.isArray(obj)) {
    const cleaned = obj
      .map((item) => stripEmpty(item))
      .filter((item) => item !== undefined);
    return cleaned;
  }
  if (typeof obj === "object" && obj !== null) {
    const result: Record<string, any> = {};
    for (const key of Object.keys(obj)) {
      const value = obj[key];
      // Skip null, undefined, empty string, empty arrays
      if (value === null || value === undefined) continue;
      if (value === "") continue;
      if (Array.isArray(value) && value.length === 0) continue;
      const cleaned = stripEmpty(value);
      if (cleaned !== undefined) {
        result[key] = cleaned;
      }
    }
    return Object.keys(result).length > 0 ? result : undefined;
  }
  return obj;
}

/**
 * Check if an array contains only primitive values (strings, numbers, booleans).
 */
function isPrimitiveArray(arr: any[]): boolean {
  return arr.every(
    (item) =>
      typeof item === "string" ||
      typeof item === "number" ||
      typeof item === "boolean"
  );
}

/**
 * Truncate arrays in the response to maxItems, adding _truncated and _totalCount metadata.
 * Works recursively on nested objects.
 * Skips arrays of primitives under 20 items (usually parameter lists, not data).
 */
function truncateArrays(obj: any, maxItems: number = 100): any {
  if (obj === null || obj === undefined) return obj;
  if (Array.isArray(obj)) {
    // Process each item recursively first
    const processed = obj.map((item) => truncateArrays(item, maxItems));
    // Don't truncate small primitive arrays
    if (isPrimitiveArray(processed) && processed.length < 20) {
      return processed;
    }
    if (processed.length > maxItems) {
      return processed.slice(0, maxItems);
    }
    return processed;
  }
  if (typeof obj === "object" && obj !== null) {
    const result: Record<string, any> = {};
    for (const key of Object.keys(obj)) {
      const value = obj[key];
      if (Array.isArray(value)) {
        const processed = value.map((item) => truncateArrays(item, maxItems));
        // Skip small primitive arrays
        if (isPrimitiveArray(processed) && processed.length < 20) {
          result[key] = processed;
          continue;
        }
        if (processed.length > maxItems) {
          result[key] = processed.slice(0, maxItems);
          result[`_truncated`] = true;
          result[`_totalCount_${key}`] = value.length;
        } else {
          result[key] = processed;
        }
      } else {
        result[key] = truncateArrays(value, maxItems);
      }
    }
    return result;
  }
  return obj;
}

/**
 * Compact mode: replace data arrays with summary counts.
 * e.g., { elements: [{...}, {...}] } -> { elements: "50 items", elementCount: 50 }
 * Keeps primitive arrays and small arrays (< 5 items) intact.
 */
function compactSummary(obj: any): any {
  if (obj === null || obj === undefined) return obj;
  if (Array.isArray(obj)) {
    return obj.map((item) => compactSummary(item));
  }
  if (typeof obj === "object" && obj !== null) {
    const result: Record<string, any> = {};
    for (const key of Object.keys(obj)) {
      const value = obj[key];
      if (Array.isArray(value)) {
        // Keep small arrays and primitive arrays intact
        if (value.length < 5 || isPrimitiveArray(value)) {
          result[key] = value.map((item) => compactSummary(item));
        } else {
          // Replace with summary
          result[key] = `${value.length} items`;
          result[`${key}Count`] = value.length;
        }
      } else {
        result[key] = compactSummary(value);
      }
    }
    return result;
  }
  return obj;
}

/**
 * Main wrapper: apply all optimizations based on options.
 * Use this in every tool's response path.
 */
export function compactResponse(
  response: any,
  options?: {
    compact?: boolean;
    maxArrayItems?: number;
    stripNulls?: boolean;
  }
): any {
  const opts = {
    compact: options?.compact ?? false,
    maxArrayItems: options?.maxArrayItems ?? 100,
    stripNulls: options?.stripNulls ?? true,
  };

  let result = response;

  // Step 1: strip nulls/empties
  if (opts.stripNulls) {
    result = stripEmpty(result);
  }

  // Step 2: compact summary or truncate arrays
  if (opts.compact) {
    result = compactSummary(result);
  } else {
    result = truncateArrays(result, opts.maxArrayItems);
  }

  return result;
}
