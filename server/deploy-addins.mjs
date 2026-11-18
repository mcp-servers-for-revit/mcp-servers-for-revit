/**
 * Post-build step: copies server build + tool-schemas to all Revit Addins folders (2020-2027).
 * Run npm run deploy:addins after building to update local installations.
 */
import { cpSync, existsSync, mkdirSync } from "fs";
import { join } from "path";

const APPDATA = process.env.APPDATA || "";
const YEARS = ["2020", "2021", "2022", "2023", "2024", "2025", "2026", "2027"];
const ADDINS_BASE = join(APPDATA, "Autodesk", "Revit", "Addins");
const RELATIVE_PATH = join("revit_mcp_plugin", "Commands", "RevitMCPCommandSet");

const SOURCE_BUILD = join(import.meta.dirname, "build", "index.js");
const SOURCE_PACKAGE_JSON = join(import.meta.dirname, "package.json");
const SOURCE_SCHEMAS = join(import.meta.dirname, "..", "tool-schemas.txt");
const SOURCE_JSON_SCHEMAS = join(import.meta.dirname, "..", "plugin", "tool_schemas.json");

// Standalone runs before `npm run build` used to seed an empty server/build in
// the add-in and then report "No Revit addins folders found". Fail early instead.
// This check depends only on the source build, never on target Addins folders
// (CI runners have none and must still exit 0).
if (!existsSync(SOURCE_BUILD)) {
  console.error("server/build/index.js missing — run `npm run build` first");
  process.exit(1);
}

let found = 0; // plugin + command set present for this year
let deployed = 0; // copy succeeded

for (const year of YEARS) {
  const pluginRoot = join(ADDINS_BASE, year, "revit_mcp_plugin");
  const targetDir = join(ADDINS_BASE, year, RELATIVE_PATH);
  if (!existsSync(targetDir)) {
    if (existsSync(pluginRoot)) {
      console.error(
        `Warning: plugin found for ${year} but command set not deployed ` +
          `(missing Commands/RevitMCPCommandSet) — skipping`
      );
    }
    continue;
  }
  found++;

  // Everything below is best-effort per year: a failure for one Revit version
  // must not fail `npm run build` after esbuild has already succeeded.
  try {
    // Seed server/build/ if the plugin is deployed but the server is not yet —
    // the from-source case (release ZIPs bundle the server; Release build output
    // does not). Without it the plugin's health check fails: "index.js not found".
    const targetBuildDir = join(targetDir, "server", "build");
    if (!existsSync(targetBuildDir)) mkdirSync(targetBuildDir, { recursive: true });

    cpSync(SOURCE_BUILD, join(targetBuildDir, "index.js"));
    // Always refresh package.json alongside index.js; its load-bearing field is
    // "type":"module", without which Node cannot load the ESM build.
    cpSync(SOURCE_PACKAGE_JSON, join(targetDir, "server", "package.json"));
    if (existsSync(SOURCE_SCHEMAS)) {
      cpSync(SOURCE_SCHEMAS, join(targetDir, "tool-schemas.txt"));
    }
    // Deploy tool_schemas.json to plugin root (sibling of Commands/)
    if (existsSync(SOURCE_JSON_SCHEMAS)) {
      cpSync(SOURCE_JSON_SCHEMAS, join(pluginRoot, "tool_schemas.json"));
    }
    deployed++;
    console.error(`Deployed to Revit ${year} addins`);
  } catch (err) {
    console.error(`Warning: Could not deploy to Revit ${year}: ${err.message}`);
  }
}

if (found === 0) {
  console.error("No Revit plugin folders with a deployed command set found — skipping deploy");
} else if (deployed === 0) {
  console.error(`Found ${found} Revit plugin folder(s) but deploy failed for all of them`);
} else if (deployed < found) {
  console.error(`Deployed server build to ${deployed} of ${found} Revit version(s)`);
} else {
  console.error(`Deployed server build to ${deployed} Revit version(s)`);
}
