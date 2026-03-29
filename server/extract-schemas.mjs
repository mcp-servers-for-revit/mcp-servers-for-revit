import { z } from "zod";
import { zodToJsonSchema } from "zod-to-json-schema";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const tools = [];

const mockServer = {
    tool(name, descOrSchema, schemaOrHandler, handler) {
        let description, schema;
        if (typeof descOrSchema === "string") {
            description = descOrSchema;
            if (typeof schemaOrHandler === "object" && schemaOrHandler !== null && !Array.isArray(schemaOrHandler)) {
                schema = schemaOrHandler;
            }
        } else if (typeof descOrSchema === "object") {
            schema = descOrSchema;
        }

        let inputSchema = { type: "object", properties: {} };
        if (schema) {
            try {
                const firstKey = Object.keys(schema)[0];
                const firstValue = firstKey ? schema[firstKey] : null;
                if (firstValue && firstValue._def) {
                    // Plain object of Zod fields
                    const zodObj = z.object(schema);
                    inputSchema = zodToJsonSchema(zodObj);
                } else if (schema._def) {
                    // Direct Zod schema
                    inputSchema = zodToJsonSchema(schema);
                }
                delete inputSchema.$schema;
                delete inputSchema.additionalProperties;
            } catch(e) {
                console.error(`  Schema error for ${name}: ${e.message}`);
            }
        }

        tools.push({ name, description: description || name, input_schema: inputSchema });
    }
};

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const toolDir = path.join(__dirname, "build", "tools");
const files = fs.readdirSync(toolDir).filter(f =>
    f.endsWith(".js") && f !== "register.js" && f !== "index.js"
);

for (const file of files) {
    try {
        const mod = await import(`./build/tools/${file}`);
        const regFn = Object.values(mod).find(v => typeof v === "function");
        if (regFn) regFn(mockServer);
    } catch(e) {
        console.error(`Error loading ${file}: ${e.message}`);
    }
}

console.log(JSON.stringify(tools, null, 2));
