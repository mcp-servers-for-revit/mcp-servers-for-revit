// @ts-ignore - sql.js has no type declarations
import initSqlJs from 'sql.js';
type SqlJsDatabase = any;
import { join } from 'path';
import { fileURLToPath } from 'url';
import { dirname } from 'path';
import { readFileSync, writeFileSync, existsSync } from 'fs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

// Database path (stored in project root)
const DB_PATH = join(__dirname, '..', '..', 'revit-data.db');

let db: SqlJsDatabase;

// Initialize database connection
async function init(): Promise<SqlJsDatabase> {
  const SQL = await initSqlJs();

  if (existsSync(DB_PATH)) {
    const fileBuffer = readFileSync(DB_PATH);
    db = new SQL.Database(fileBuffer);
  } else {
    db = new SQL.Database();
  }

  // Enable foreign keys
  db.run('PRAGMA foreign_keys = ON');

  // Initialize schema
  db.run(`
    CREATE TABLE IF NOT EXISTS projects (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      project_name TEXT NOT NULL,
      project_path TEXT,
      project_number TEXT,
      project_address TEXT,
      client_name TEXT,
      project_status TEXT,
      author TEXT,
      timestamp INTEGER NOT NULL,
      last_updated INTEGER NOT NULL,
      metadata TEXT
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS rooms (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      project_id INTEGER NOT NULL,
      room_id TEXT NOT NULL,
      room_name TEXT,
      room_number TEXT,
      department TEXT,
      level TEXT,
      area REAL,
      perimeter REAL,
      occupancy TEXT,
      comments TEXT,
      timestamp INTEGER NOT NULL,
      metadata TEXT,
      FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
      UNIQUE(project_id, room_id)
    )
  `);

  db.run(`CREATE INDEX IF NOT EXISTS idx_projects_name ON projects(project_name)`);
  db.run(`CREATE INDEX IF NOT EXISTS idx_projects_timestamp ON projects(timestamp)`);
  db.run(`CREATE INDEX IF NOT EXISTS idx_rooms_project_id ON rooms(project_id)`);
  db.run(`CREATE INDEX IF NOT EXISTS idx_rooms_room_number ON rooms(room_number)`);

  return db;
}

// Save database to disk
export function saveDatabase(): void {
  if (db) {
    const data = db.export();
    const buffer = Buffer.from(data);
    writeFileSync(DB_PATH, buffer);
  }
}

// Get the initialized database (lazy init)
let dbPromise: Promise<SqlJsDatabase> | null = null;

export function getDb(): Promise<SqlJsDatabase> {
  if (!dbPromise) {
    dbPromise = init();
  }
  return dbPromise;
}

export { SqlJsDatabase };
