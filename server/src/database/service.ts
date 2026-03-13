import { getDb, saveDatabase } from './db.js';

// Project data interface
export interface ProjectData {
  project_name: string;
  project_path?: string;
  project_number?: string;
  project_address?: string;
  client_name?: string;
  project_status?: string;
  author?: string;
  metadata?: Record<string, any>;
}

// Room data interface
export interface RoomData {
  room_id: string;
  room_name?: string;
  room_number?: string;
  department?: string;
  level?: string;
  area?: number;
  perimeter?: number;
  occupancy?: string;
  comments?: string;
  metadata?: Record<string, any>;
}

// Helper to get a single row from a query
function getOne(db: any, sql: string, params: any[] = []): any {
  const stmt = db.prepare(sql);
  stmt.bind(params);
  let row: any = undefined;
  if (stmt.step()) {
    const columns = stmt.getColumnNames();
    const values = stmt.get();
    row = {} as any;
    columns.forEach((col: string, i: number) => { row[col] = values[i]; });
  }
  stmt.free();
  return row;
}

// Helper to get all rows from a query
function getAll(db: any, sql: string, params: any[] = []): any[] {
  const stmt = db.prepare(sql);
  stmt.bind(params);
  const rows: any[] = [];
  while (stmt.step()) {
    const columns = stmt.getColumnNames();
    const values = stmt.get();
    const row = {} as any;
    columns.forEach((col: string, i: number) => { row[col] = values[i]; });
    rows.push(row);
  }
  stmt.free();
  return rows;
}

// Store or update project data
export async function storeProject(data: ProjectData): Promise<number> {
  const db = await getDb();
  const timestamp = Date.now();
  const metadata = data.metadata ? JSON.stringify(data.metadata) : null;

  const existingProject = getOne(db,
    'SELECT id FROM projects WHERE project_name = ?',
    [data.project_name]
  );

  if (existingProject) {
    db.run(`
      UPDATE projects SET
        project_path = ?,
        project_number = ?,
        project_address = ?,
        client_name = ?,
        project_status = ?,
        author = ?,
        last_updated = ?,
        metadata = ?
      WHERE id = ?
    `, [
      data.project_path || null,
      data.project_number || null,
      data.project_address || null,
      data.client_name || null,
      data.project_status || null,
      data.author || null,
      timestamp,
      metadata,
      existingProject.id
    ]);
    saveDatabase();
    return existingProject.id;
  } else {
    db.run(`
      INSERT INTO projects (
        project_name, project_path, project_number, project_address,
        client_name, project_status, author, timestamp, last_updated, metadata
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `, [
      data.project_name,
      data.project_path || null,
      data.project_number || null,
      data.project_address || null,
      data.client_name || null,
      data.project_status || null,
      data.author || null,
      timestamp,
      timestamp,
      metadata
    ]);
    const row = getOne(db, 'SELECT last_insert_rowid() as id');
    saveDatabase();
    return row.id;
  }
}

// Store or update room data
export async function storeRoom(projectId: number, data: RoomData): Promise<number> {
  const db = await getDb();
  const timestamp = Date.now();
  const metadata = data.metadata ? JSON.stringify(data.metadata) : null;

  const existingRoom = getOne(db,
    'SELECT id FROM rooms WHERE project_id = ? AND room_id = ?',
    [projectId, data.room_id]
  );

  if (existingRoom) {
    db.run(`
      UPDATE rooms SET
        room_name = ?,
        room_number = ?,
        department = ?,
        level = ?,
        area = ?,
        perimeter = ?,
        occupancy = ?,
        comments = ?,
        timestamp = ?,
        metadata = ?
      WHERE id = ?
    `, [
      data.room_name || null,
      data.room_number || null,
      data.department || null,
      data.level || null,
      data.area || null,
      data.perimeter || null,
      data.occupancy || null,
      data.comments || null,
      timestamp,
      metadata,
      existingRoom.id
    ]);
    saveDatabase();
    return existingRoom.id;
  } else {
    db.run(`
      INSERT INTO rooms (
        project_id, room_id, room_name, room_number, department,
        level, area, perimeter, occupancy, comments, timestamp, metadata
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `, [
      projectId,
      data.room_id,
      data.room_name || null,
      data.room_number || null,
      data.department || null,
      data.level || null,
      data.area || null,
      data.perimeter || null,
      data.occupancy || null,
      data.comments || null,
      timestamp,
      metadata
    ]);
    const row = getOne(db, 'SELECT last_insert_rowid() as id');
    saveDatabase();
    return row.id;
  }
}

// Store multiple rooms at once
export async function storeRoomsBatch(projectId: number, rooms: RoomData[]): Promise<number> {
  let count = 0;
  for (const room of rooms) {
    await storeRoom(projectId, room);
    count++;
  }
  return count;
}

// Get all projects
export async function getAllProjects() {
  const db = await getDb();
  const projects = getAll(db, `
    SELECT
      id, project_name, project_path, project_number, project_address,
      client_name, project_status, author, timestamp, last_updated, metadata
    FROM projects
    ORDER BY last_updated DESC
  `);

  return projects.map((p: any) => ({
    ...p,
    metadata: p.metadata ? JSON.parse(p.metadata) : null,
    timestamp: new Date(p.timestamp).toISOString(),
    last_updated: new Date(p.last_updated).toISOString()
  }));
}

// Get project by ID
export async function getProjectById(projectId: number) {
  const db = await getDb();
  const project = getOne(db, `
    SELECT
      id, project_name, project_path, project_number, project_address,
      client_name, project_status, author, timestamp, last_updated, metadata
    FROM projects
    WHERE id = ?
  `, [projectId]);

  if (!project) return null;

  return {
    ...project,
    metadata: project.metadata ? JSON.parse(project.metadata) : null,
    timestamp: new Date(project.timestamp).toISOString(),
    last_updated: new Date(project.last_updated).toISOString()
  };
}

// Get project by name
export async function getProjectByName(projectName: string) {
  const db = await getDb();
  const project = getOne(db, `
    SELECT
      id, project_name, project_path, project_number, project_address,
      client_name, project_status, author, timestamp, last_updated, metadata
    FROM projects
    WHERE project_name = ?
  `, [projectName]);

  if (!project) return null;

  return {
    ...project,
    metadata: project.metadata ? JSON.parse(project.metadata) : null,
    timestamp: new Date(project.timestamp).toISOString(),
    last_updated: new Date(project.last_updated).toISOString()
  };
}

// Get rooms by project ID
export async function getRoomsByProjectId(projectId: number) {
  const db = await getDb();
  const rooms = getAll(db, `
    SELECT
      id, project_id, room_id, room_name, room_number, department,
      level, area, perimeter, occupancy, comments, timestamp, metadata
    FROM rooms
    WHERE project_id = ?
    ORDER BY room_number
  `, [projectId]);

  return rooms.map((r: any) => ({
    ...r,
    metadata: r.metadata ? JSON.parse(r.metadata) : null,
    timestamp: new Date(r.timestamp).toISOString()
  }));
}

// Get all rooms with project info
export async function getAllRoomsWithProject() {
  const db = await getDb();
  const rooms = getAll(db, `
    SELECT
      r.id, r.project_id, r.room_id, r.room_name, r.room_number,
      r.department, r.level, r.area, r.perimeter, r.occupancy,
      r.comments, r.timestamp, r.metadata,
      p.project_name, p.project_number
    FROM rooms r
    JOIN projects p ON r.project_id = p.id
    ORDER BY p.project_name, r.room_number
  `);

  return rooms.map((r: any) => ({
    ...r,
    metadata: r.metadata ? JSON.parse(r.metadata) : null,
    timestamp: new Date(r.timestamp).toISOString()
  }));
}

// Delete project (and all its rooms due to CASCADE)
export async function deleteProject(projectId: number): Promise<boolean> {
  const db = await getDb();
  db.run('DELETE FROM projects WHERE id = ?', [projectId]);
  const changes = db.getRowsModified();
  if (changes > 0) saveDatabase();
  return changes > 0;
}

// Get database statistics
export async function getStats() {
  const db = await getDb();
  const projectCount = getOne(db, 'SELECT COUNT(*) as count FROM projects');
  const roomCount = getOne(db, 'SELECT COUNT(*) as count FROM rooms');

  return {
    total_projects: projectCount.count,
    total_rooms: roomCount.count
  };
}
