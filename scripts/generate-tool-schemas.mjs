#!/usr/bin/env node
/**
 * generate-tool-schemas.mjs
 *
 * Auto-generates plugin/tool_schemas.json by parsing C# source files.
 * Reads command.json for names/descriptions, then parses Command classes
 * and Model classes to extract parameter schemas.
 *
 * Usage: node scripts/generate-tool-schemas.mjs
 */

import { readFileSync, writeFileSync, readdirSync, statSync } from "fs";
import { join, resolve, basename } from "path";

const ROOT = resolve(import.meta.dirname, "..");
const COMMANDS_DIR = join(ROOT, "commandset", "Commands");
const MODELS_DIR = join(ROOT, "commandset", "Models");
const COMMAND_JSON = join(ROOT, "command.json");
const OUTPUT = join(ROOT, "plugin", "tool_schemas.json");

// ── Helpers ──────────────────────────────────────────────────────────────────

/** Recursively collect all .cs files under a directory */
function collectCsFiles(dir) {
  const results = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      results.push(...collectCsFiles(full));
    } else if (entry.name.endsWith(".cs")) {
      results.push(full);
    }
  }
  return results;
}

// ── Load all source files ────────────────────────────────────────────────────

const commandFiles = collectCsFiles(COMMANDS_DIR);
const modelFiles = collectCsFiles(MODELS_DIR);

/** Cache: filename -> content */
const fileContentCache = new Map();
function readCached(path) {
  if (!fileContentCache.has(path)) {
    fileContentCache.set(path, readFileSync(path, "utf-8"));
  }
  return fileContentCache.get(path);
}

// ── Load command.json ────────────────────────────────────────────────────────

const commandJson = JSON.parse(readFileSync(COMMAND_JSON, "utf-8"));
const commandDefs = new Map();
for (const cmd of commandJson.commands) {
  commandDefs.set(cmd.commandName, cmd.description);
}

// ── Find command file by CommandName ─────────────────────────────────────────

/** Map commandName -> file path by scanning CommandName => "xxx" */
const commandNameToFile = new Map();
for (const file of commandFiles) {
  const content = readCached(file);
  // Match: CommandName => "some_name"  or CommandName => "some_name";
  const m = content.match(/CommandName\s*=>\s*"([^"]+)"/);
  if (m) {
    commandNameToFile.set(m[1], file);
  }
}

// ── Find model file by class name ────────────────────────────────────────────

/** Map className -> file path */
const modelClassToFile = new Map();
for (const file of modelFiles) {
  const content = readCached(file);
  // Match all class declarations in the file
  const classMatches = content.matchAll(/\bclass\s+(\w+)/g);
  for (const cm of classMatches) {
    modelClassToFile.set(cm[1], file);
  }
}

// Also scan command files for inner/helper classes
for (const file of commandFiles) {
  const content = readCached(file);
  const classMatches = content.matchAll(/\bclass\s+(\w+)/g);
  for (const cm of classMatches) {
    if (!modelClassToFile.has(cm[1])) {
      modelClassToFile.set(cm[1], file);
    }
  }
}

/** Map: using aliases (e.g. LevelCreationInfo -> LevelInfo) found in command files */
const usingAliases = new Map();
for (const file of commandFiles) {
  const content = readCached(file);
  // Match: using Alias = Namespace.ClassName;
  const aliasMatches = content.matchAll(
    /using\s+(\w+)\s*=\s*[\w.]+\.(\w+)\s*;/g,
  );
  for (const am of aliasMatches) {
    usingAliases.set(am[1], am[2]);
  }
}

// ── C# type to JSON Schema type mapping ──────────────────────────────────────

function csTypeToJsonSchema(csType) {
  if (!csType) return { type: "string" };

  // Strip nullable
  const t = csType.replace(/\?\s*$/, "").trim();

  // Primitive types
  if (t === "string") return { type: "string" };
  if (t === "int" || t === "long" || t === "Int32" || t === "Int64")
    return { type: "integer" };
  if (t === "double" || t === "float" || t === "decimal")
    return { type: "number" };
  if (t === "bool" || t === "Boolean" || t === "boolean")
    return { type: "boolean" };
  if (t === "object") return {};
  if (t === "JArray") return { type: "array" };
  if (t === "JObject") return { type: "object" };

  // Strip fully-qualified namespace prefixes (e.g. System.Collections.Generic.List -> List)
  const stripped = t.replace(/^[\w.]+\.(?=List|Dictionary|IList|IEnumerable|ICollection)/, "");

  // Arrays
  const arrayMatch = stripped.match(
    /^(?:List|IList|IEnumerable|ICollection)\s*<\s*(.+)\s*>$/,
  );
  if (arrayMatch) {
    const inner = arrayMatch[1].trim();
    const innerSchema = csTypeToJsonSchema(inner);
    // If inner is a known model, resolve it
    const resolved = resolveModelType(inner);
    if (resolved) {
      return { type: "array", items: resolved };
    }
    return { type: "array", items: innerSchema };
  }

  // int[] / long[] / string[] etc
  if (t.endsWith("[]")) {
    const inner = t.slice(0, -2).trim();
    return { type: "array", items: csTypeToJsonSchema(inner) };
  }

  // Dictionary types
  const dictMatch = stripped.match(
    /^(?:Dictionary|IDictionary)\s*<\s*(\w+)\s*,\s*(.+)\s*>$/,
  );
  if (dictMatch) {
    return { type: "object" };
  }

  // Nullable<T>
  const nullableMatch = t.match(/^Nullable\s*<\s*(.+)\s*>$/);
  if (nullableMatch) {
    return csTypeToJsonSchema(nullableMatch[1].trim());
  }

  // Known model type (also try alias resolution)
  const modelSchema = resolveModelType(t);
  if (modelSchema) return modelSchema;
  const aliased = usingAliases.get(t);
  if (aliased) {
    const aliasSchema = resolveModelType(aliased);
    if (aliasSchema) return aliasSchema;
  }

  // Unknown -> object
  return { type: "object" };
}

// ── Model type resolution (with recursion guard) ─────────────────────────────

const modelResolutionStack = new Set();

function resolveModelType(className) {
  if (modelResolutionStack.has(className)) {
    return { type: "object" }; // circular reference guard
  }

  // Resolve using aliases (e.g. LevelCreationInfo -> LevelInfo)
  const resolvedName = usingAliases.get(className) || className;

  const file = modelClassToFile.get(resolvedName);
  if (!file) return null;

  modelResolutionStack.add(resolvedName);
  try {
    const schema = parseModelClass(resolvedName, readCached(file));
    return schema;
  } finally {
    modelResolutionStack.delete(resolvedName);
  }
}

// ── Parse a model class to extract JSON schema ──────────────────────────────

function parseModelClass(className, fileContent) {
  // Find the class body
  const classRegex = new RegExp(
    `\\bclass\\s+${className}\\b[^{]*\\{`,
    "s",
  );
  const classMatch = classRegex.exec(fileContent);
  if (!classMatch) return { type: "object" };

  // Extract class body by brace matching
  const startIdx = classMatch.index + classMatch[0].length;
  let depth = 1;
  let endIdx = startIdx;
  for (let i = startIdx; i < fileContent.length && depth > 0; i++) {
    if (fileContent[i] === "{") depth++;
    else if (fileContent[i] === "}") depth--;
    endIdx = i;
  }
  const classBody = fileContent.substring(startIdx, endIdx);

  const properties = {};
  const required = [];

  // Pattern: [JsonProperty("name")] followed by property declaration
  // Handle both single-line and multi-line patterns
  const propRegex =
    /\[JsonProperty\("(\w+)"\)\]\s*(?:public\s+)?([\w<>,\[\]\s?]+?)\s+(\w+)\s*(?:\{[^}]*\}|\s*=\s*[^;]+;|;)/g;

  let propMatch;
  while ((propMatch = propRegex.exec(classBody)) !== null) {
    const jsonName = propMatch[1];
    const csType = propMatch[2].trim();
    const propName = propMatch[3];

    const schema = csTypeToJsonSchema(csType);

    // Check for default value in property initializer
    const defaultVal = extractPropertyDefault(classBody, propName, csType);

    if (defaultVal !== undefined) {
      schema.default = defaultVal;
    } else {
      // No default -> required (unless nullable type)
      if (!csType.endsWith("?") && !csType.startsWith("Nullable")) {
        required.push(jsonName);
      }
    }

    // Extract description from XML comment above this property
    const desc = extractPropertyDescription(classBody, jsonName, propName);
    if (desc) schema.description = desc;

    properties[jsonName] = schema;
  }

  // Also handle field declarations (not properties):
  // [JsonProperty("elementIds")] public List<int> ElementIds = new List<int>();
  const fieldRegex =
    /\[JsonProperty\("(\w+)"\)\]\s*(?:public\s+)?([\w<>,\[\]\s?]+?)\s+(\w+)\s*=\s*([^;]+);/g;
  let fieldMatch;
  while ((fieldMatch = fieldRegex.exec(classBody)) !== null) {
    const jsonName = fieldMatch[1];
    if (properties[jsonName]) continue; // already found as property
    const csType = fieldMatch[2].trim();
    const defaultExpr = fieldMatch[4].trim();

    const schema = csTypeToJsonSchema(csType);
    const defaultVal = parseDefaultExpression(defaultExpr, csType);
    if (defaultVal !== undefined) {
      schema.default = defaultVal;
    }

    const desc = extractPropertyDescription(classBody, jsonName, fieldMatch[3]);
    if (desc) schema.description = desc;

    properties[jsonName] = schema;
  }

  const result = {
    type: "object",
    properties,
    additionalProperties: false,
  };

  if (required.length > 0) {
    result.required = required;
  }

  return result;
}

function extractPropertyDescription(classBody, jsonName, propName) {
  // Look for XML summary comment directly before the [JsonProperty("jsonName")] attribute
  // The pattern matches: /// <summary> ... /// text ... /// </summary> [JsonProperty("jsonName")]
  // Use a specific regex that finds the CLOSEST summary block before this specific JsonProperty
  const pattern = new RegExp(
    `///\\s*<summary>[\\s\\S]*?///\\s*(.+?)\\s*\\r?\\n\\s*///\\s*</summary>\\s*\\r?\\n\\s*\\[JsonProperty\\("${jsonName}"\\)\\]`,
  );
  const m = pattern.exec(classBody);
  if (m) {
    // Clean up: remove leading "/// " prefix if present
    let desc = m[1].trim().replace(/^\/\/\/\s*/, "");
    return desc;
  }
  return null;
}

function extractPropertyDefault(classBody, propName, csType) {
  // Match: PropName { get; set; } = defaultValue;
  const pattern = new RegExp(
    `\\b${propName}\\s*\\{[^}]*\\}\\s*=\\s*([^;]+);`,
  );
  const m = pattern.exec(classBody);
  if (!m) return undefined;

  return parseDefaultExpression(m[1].trim(), csType);
}

function parseDefaultExpression(expr, csType) {
  if (!expr) return undefined;

  // new List<...>() or new Dictionary<...>() or new ...() -> skip (not a simple default)
  if (/^new\s+/.test(expr)) return undefined;

  // null
  if (expr === "null") return undefined;

  // string.Empty or ""
  if (expr === 'string.Empty' || expr === '""' || expr === "String.Empty")
    return "";

  // Quoted string
  const strMatch = expr.match(/^"([^"]*)"$/);
  if (strMatch) return strMatch[1];

  // Boolean
  if (expr === "true") return true;
  if (expr === "false") return false;

  // Numeric
  const num = Number(expr);
  if (!isNaN(num) && expr !== "") return num;

  // Array initializer like new int[] { 255, 0, 0 }
  const arrayInit = expr.match(/^new\s+\w+\[\]\s*\{([^}]+)\}/);
  if (arrayInit) {
    return arrayInit[1].split(",").map((s) => {
      const n = Number(s.trim());
      return isNaN(n) ? s.trim() : n;
    });
  }

  return undefined;
}

// ── Extract ToObject<T> accesses with balanced angle brackets ────────────────

/**
 * Find all parameters?["key"]?.ToObject<Type>() patterns, handling nested generics.
 * Returns array of { key, csType, defaultExpr }.
 */
function findToObjectKeyAccesses(body) {
  const results = [];
  // Match the prefix: parameters?["key"]?.ToObject< or parameters["key"].ToObject<
  const prefixRegex = /parameters\??\["(\w+)"\]\??\.?\s*ToObject</g;
  let m;
  while ((m = prefixRegex.exec(body)) !== null) {
    const key = m[1];
    const typeStart = m.index + m[0].length;
    // Extract balanced angle bracket content
    const csType = extractBalancedAngleBrackets(body, typeStart);
    if (!csType) continue;

    // Check for default expression after ()
    const afterType = body.substring(typeStart + csType.length + 1); // +1 for closing >
    const afterMatch = afterType.match(/^\s*\(\)\s*(\?\?\s*(.+?))?(?=\s*[;,)])/);
    const defaultExpr = afterMatch?.[2]?.trim();

    results.push({ key, csType, defaultExpr });
  }
  return results;
}

/** Extract type from balanced angle brackets starting at pos (right after <) */
function extractBalancedAngleBrackets(str, pos) {
  let depth = 1;
  let i = pos;
  while (i < str.length && depth > 0) {
    if (str[i] === "<") depth++;
    else if (str[i] === ">") depth--;
    if (depth === 0) break;
    i++;
  }
  if (depth !== 0) return null;
  return str.substring(pos, i);
}

// ── Parse Execute method to extract parameters ──────────────────────────────

function parseExecuteMethod(commandName, fileContent) {
  // Find Execute method body
  const execMatch = fileContent.match(
    /public\s+override\s+object\s+Execute\s*\(\s*JObject\s+parameters[^)]*\)\s*\{/s,
  );
  if (!execMatch) return { type: "object", properties: {}, additionalProperties: false };

  const startIdx = execMatch.index + execMatch[0].length;
  let depth = 1;
  let endIdx = startIdx;
  for (let i = startIdx; i < fileContent.length && depth > 0; i++) {
    if (fileContent[i] === "{") depth++;
    else if (fileContent[i] === "}") depth--;
    endIdx = i;
  }
  const body = fileContent.substring(startIdx, endIdx);

  // ── Pattern C: Full object deserialization ──
  // parameters.ToObject<T>() or parameters?.ToObject<T>()
  const fullDeserMatch = body.match(
    /parameters\??\.ToObject<(\w+)>\s*\(\)/,
  );
  if (fullDeserMatch) {
    const typeName = fullDeserMatch[1];
    const resolved = resolveModelType(typeName);
    if (resolved) {
      return ensureSchema(resolved);
    }
  }

  // Now extract individual parameters
  const properties = {};
  const required = [];

  // ── Pattern A: parameters?["key"]?.Value<type>() ──
  const valuePatterns = [
    // parameters?["key"]?.Value<type>() ?? default
    /parameters\??\["\s*(\w+)\s*"\]\??\.\s*Value<(\w+)>\s*\(\)\s*(?:\?\?\s*(.+?))?(?:;|\))/g,
    // parameters["key"].Value<type>()
    /parameters\["\s*(\w+)\s*"\]\.Value<(\w+)>\s*\(\)\s*(?:\?\?\s*(.+?))?(?:;|\))/g,
    // parameters["key"].ToString()
    /parameters\??\["\s*(\w+)\s*"\]\??\.\s*ToString\s*\(\)/g,
    // parameters["key"].ToObject<bool>()
    /parameters\??\["\s*(\w+)\s*"\]\??\.\s*ToObject<(\w+)>\s*\(\)/g,
  ];

  // Pattern A: Value<type> with optional defaults
  const valueRegex =
    /parameters\??\["\s*(\w+)\s*"\]\??\.\s*Value<(\w+)>\s*\(\)\s*(\?\?\s*(.+?))?(?=\s*[;,)])/g;
  let vm;
  while ((vm = valueRegex.exec(body)) !== null) {
    const key = vm[1];
    const csType = vm[2];
    const defaultExpr = vm[4]?.trim();

    if (properties[key]) continue;
    const schema = csTypeToJsonSchema(csType);

    if (defaultExpr !== undefined) {
      const defVal = parseDefaultExpression(defaultExpr, csType);
      if (defVal !== undefined) {
        schema.default = defVal;
      }
    } else {
      required.push(key);
    }

    // dryRun safety override
    if (key === "dryRun") {
      schema.default = true;
    }

    properties[key] = schema;
  }

  // Pattern: parameters?.Value<type>("key") ?? default (JObject extension method syntax)
  const valueMethodRegex =
    /parameters\??\.Value<(\w+\??)>\s*\(\s*"(\w+)"\s*\)\s*(\?\?\s*(.+?))?(?=\s*[;,)])/g;
  let vmm;
  while ((vmm = valueMethodRegex.exec(body)) !== null) {
    const csType = vmm[1];
    const key = vmm[2];
    const defaultExpr = vmm[4]?.trim();

    if (properties[key]) continue;
    const schema = csTypeToJsonSchema(csType);

    if (defaultExpr !== undefined) {
      const defVal = parseDefaultExpression(defaultExpr, csType);
      if (defVal !== undefined) {
        schema.default = defVal;
      }
    } else {
      required.push(key);
    }

    if (key === "dryRun") {
      schema.default = true;
    }

    properties[key] = schema;
  }

  // Pattern: parameters["key"].ToString() or parameters?["key"]?.ToString()
  const toStringRegex =
    /parameters\??\["\s*(\w+)\s*"\]\??\.\s*ToString\s*\(\)/g;
  let tsm;
  while ((tsm = toStringRegex.exec(body)) !== null) {
    const key = tsm[1];
    if (properties[key]) continue;

    properties[key] = { type: "string" };
    // Determine if optional: has if-guard WITHOUT else-throw
    // Don't mark as required here; the post-processing optionalKeys handles it
    required.push(key);
  }

  // Pattern: parameters?["key"]?.ToObject<T>() — handles nested generics via extractToObjectType
  const toObjectKeyPositions = findToObjectKeyAccesses(body);
  for (const { key, csType, defaultExpr } of toObjectKeyPositions) {

    if (properties[key]) continue;

    const schema = csTypeToJsonSchema(csType);

    if (defaultExpr !== undefined) {
      const defVal = parseDefaultExpression(defaultExpr, csType);
      if (defVal !== undefined) {
        schema.default = defVal;
      }
    } else {
      required.push(key);
    }

    if (key === "dryRun") {
      schema.default = true;
    }

    properties[key] = schema;
  }

  // Pattern: parameters?["key"] as JArray — try to infer item schema from foreach blocks
  const jArrayRegex = /parameters\??\["\s*(\w+)\s*"\]\s*as\s+JArray/g;
  let jam;
  while ((jam = jArrayRegex.exec(body)) !== null) {
    const key = jam[1];
    if (properties[key]) continue;

    // Try to infer item properties from foreach item["key"]?.Value<type>() patterns
    const itemProps = inferJArrayItemProperties(body, key);
    if (itemProps) {
      properties[key] = {
        type: "array",
        items: {
          type: "object",
          properties: itemProps,
          additionalProperties: false,
        },
      };
    } else {
      properties[key] = { type: "array" };
    }
  }

  // Pattern: parameters.ContainsKey("key") — implies required
  const containsKeyRegex = /parameters\.ContainsKey\("(\w+)"\)/g;
  let ckm;
  while ((ckm = containsKeyRegex.exec(body)) !== null) {
    const key = ckm[1];
    if (!required.includes(key) && !properties[key]) {
      required.push(key);
    }
  }

  // Ensure dryRun always defaults to true if present
  if (properties.dryRun) {
    properties.dryRun.default = true;
  }

  // Post-processing: keys accessed within if (parameters["key"] != null) guards are optional
  // UNLESS the else branch throws (meaning it's actually required)
  const ifNullGuardRegex = /if\s*\(\s*parameters\??\["\s*(\w+)\s*"\]\s*!=\s*null\s*\)/g;
  let ing;
  const optionalKeys = new Set();
  while ((ing = ifNullGuardRegex.exec(body)) !== null) {
    const key = ing[1];
    // Check if there's an else { throw } after this guard
    const afterGuard = body.substring(ing.index);
    const hasElseThrow = /if\s*\([^)]*\)\s*\{[^}]*\}\s*else\s*\{[^}]*throw\b/.test(afterGuard);
    if (!hasElseThrow) {
      optionalKeys.add(key);
    }
  }

  // Look for variable declarations with initializers preceding parameter access
  // e.g.: string message = "Hello MCP!"; ... if (parameters?["message"] != null) { message = ... }
  // These imply defaults. Must match the variable that is ASSIGNED from the parameter.
  for (const optKey of optionalKeys) {
    if (properties[optKey] && properties[optKey].default === undefined) {
      // Find the variable name that gets assigned from this parameter
      // Pattern: varName = parameters["key"].Something
      const varNameRegex = new RegExp(
        `(\\w+)\\s*=\\s*parameters\\??\\["${optKey}"\\]`,
      );
      const vnm = varNameRegex.exec(body);
      if (vnm) {
        const varName = vnm[1];
        // Now find initialization: type varName = defaultExpr;
        const varInitRegex = new RegExp(
          `(?:string|int|bool|double|long|float|JArray)\\s+${varName}\\s*=\\s*(.+?)\\s*;`,
        );
        const vim = varInitRegex.exec(body);
        if (vim) {
          const defVal = parseDefaultExpression(vim[1].trim(), "");
          if (defVal !== undefined) {
            properties[optKey].default = defVal;
          }
        }
      }
    }
  }

  const result = {
    type: "object",
    properties,
    additionalProperties: false,
  };

  // Filter required to only include keys that are in properties,
  // have no default, and are not guarded by null checks
  const filteredRequired = required.filter(
    (k) =>
      properties[k] &&
      properties[k].default === undefined &&
      !optionalKeys.has(k),
  );
  if (filteredRequired.length > 0) {
    result.required = [...new Set(filteredRequired)];
  }

  return result;
}

function inferJArrayItemProperties(body, arrayKey) {
  // Look for item["key"]?.Value<type>() patterns after the array access
  const props = {};
  const itemRegex = /item\["\s*(\w+)\s*"\]\??\.\s*Value<(\w+)>\s*\(\)/g;
  let m;
  while ((m = itemRegex.exec(body)) !== null) {
    props[m[1]] = csTypeToJsonSchema(m[2]);
  }
  return Object.keys(props).length > 0 ? props : null;
}

function ensureSchema(schema) {
  if (!schema.additionalProperties && schema.type === "object") {
    schema.additionalProperties = false;
  }
  return schema;
}

// ── Generate schemas ─────────────────────────────────────────────────────────

const tools = [];
const warnings = [];

for (const [cmdName, description] of commandDefs) {
  const file = commandNameToFile.get(cmdName);
  if (!file) {
    warnings.push(`WARNING: No command file found for "${cmdName}"`);
    // Still add it with empty schema
    tools.push({
      name: cmdName,
      description,
      input_schema: {
        type: "object",
        properties: {},
        additionalProperties: false,
      },
    });
    continue;
  }

  const content = readCached(file);
  const inputSchema = parseExecuteMethod(cmdName, content);

  tools.push({
    name: cmdName,
    description,
    input_schema: inputSchema,
  });
}

// Sort alphabetically
tools.sort((a, b) => a.name.localeCompare(b.name));

// ── Write output ─────────────────────────────────────────────────────────────

writeFileSync(OUTPUT, JSON.stringify(tools, null, 2) + "\n", "utf-8");

// ── Report ───────────────────────────────────────────────────────────────────

console.log(`Generated ${tools.length} tool schemas -> ${OUTPUT}`);
console.log(
  `Commands in command.json: ${commandDefs.size}`,
);
console.log(
  `Command files found: ${commandNameToFile.size}`,
);

if (warnings.length > 0) {
  console.log(`\nWarnings:`);
  for (const w of warnings) console.log(`  ${w}`);
}

// Summary of tools with vs without parameters
const withParams = tools.filter(
  (t) => Object.keys(t.input_schema.properties).length > 0,
);
const noParams = tools.filter(
  (t) => Object.keys(t.input_schema.properties).length === 0,
);
console.log(`\nTools with parameters: ${withParams.length}`);
console.log(`Tools without parameters: ${noParams.length}`);
if (noParams.length > 0) {
  console.log(`  No-param tools: ${noParams.map((t) => t.name).join(", ")}`);
}
