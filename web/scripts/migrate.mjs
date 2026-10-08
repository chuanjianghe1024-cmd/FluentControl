import { Pool } from "pg";
import { readdir, readFile } from "node:fs/promises";
import { createHash } from "node:crypto";
if (!process.env.DATABASE_URL) throw new Error("Set DATABASE_URL to the direct PostgreSQL migration connection.");
const pool = new Pool({ connectionString: process.env.DATABASE_URL, max: 1 });
const db = await pool.connect();
try {
  await db.query("SELECT pg_advisory_lock(781234501)");
  await db.query("CREATE TABLE IF NOT EXISTS schema_migrations(name TEXT PRIMARY KEY, sha256 TEXT NOT NULL, applied_at TIMESTAMPTZ NOT NULL DEFAULT now())");
  const dir = new URL("../migrations/", import.meta.url);
  for (const name of (await readdir(dir)).filter(n => n.endsWith(".sql")).sort()) {
    const sql = await readFile(new URL(name, dir), "utf8");
    const hash = createHash("sha256").update(sql).digest("hex");
    const previous = await db.query("SELECT sha256 FROM schema_migrations WHERE name=$1", [name]);
    if (previous.rowCount) { if (previous.rows[0].sha256 !== hash) throw new Error(`Applied migration changed: ${name}`); continue; }
    await db.query("BEGIN");
    try { await db.query(sql); await db.query("INSERT INTO schema_migrations(name, sha256) VALUES($1,$2)", [name, hash]); await db.query("COMMIT"); console.log(`Applied ${name}`); }
    catch (error) { await db.query("ROLLBACK"); throw error; }
  }
} finally { await db.query("SELECT pg_advisory_unlock(781234501)"); db.release(); await pool.end(); }
