import type { DatabaseSync } from 'node:sqlite';
import { createRequire } from 'node:module';
import { existsSync, mkdirSync } from 'node:fs';
import { homedir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const legacyFile = join(dirname(fileURLToPath(import.meta.url)), '..', 'revit-data.db');
const filename = existsSync(legacyFile) ? legacyFile : join(homedir(), '.mcp-revit', 'revit-data.db');

function openDatabase(): DatabaseSync {
  // The portable Node runtime includes SQLite. Older Node installations use the base package's driver.
  let Driver;
  try {
    Driver = require('node:sqlite').DatabaseSync;
  } catch {
    Driver = require('better-sqlite3');
  }
  return new Driver(filename);
}

let connection: DatabaseSync | undefined;
export function getDatabase(): DatabaseSync {
  if (!connection) {
    mkdirSync(dirname(filename), { recursive: true });
    const db = openDatabase();
    try {
      db.exec('PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000');
      initializeDatabase(db);
      connection = db;
    } catch (error) {
      db.close();
      throw error;
    }
  }
  return connection;
}

// Initialize database schema
function initializeDatabase(db: DatabaseSync) {
  // Create projects table
  db.exec(`
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

  // Create rooms table
  db.exec(`
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

  // Create index for faster queries
  db.exec(`
    CREATE INDEX IF NOT EXISTS idx_projects_name ON projects(project_name);
    CREATE INDEX IF NOT EXISTS idx_projects_timestamp ON projects(timestamp);
    CREATE INDEX IF NOT EXISTS idx_rooms_project_id ON rooms(project_id);
    CREATE INDEX IF NOT EXISTS idx_rooms_room_number ON rooms(room_number);
  `);
}
