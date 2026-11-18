import { build } from 'esbuild';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));

await build({
  entryPoints: ['src/index.ts'],
  bundle: true,
  platform: 'node',
  target: 'node18',
  format: 'esm',
  outfile: 'build/index.js',
  banner: {
    js: [
      // ESM shims for __dirname and require() (needed by bundled code)
      'import { createRequire as __createRequire } from "module";',
      'const require = __createRequire(import.meta.url);',
    ].join('\n'),
  },
  // Exclude node built-ins from bundle
  external: ['better-sqlite3', 'fs', 'path', 'os', 'url', 'module', 'crypto', 'events', 'stream', 'util', 'net', 'tls', 'http', 'https', 'zlib', 'buffer', 'string_decoder', 'child_process', 'worker_threads', 'node:*'],
  sourcemap: false,
  minify: false, // Keep readable for debugging
});

console.log("Built MCP server");
