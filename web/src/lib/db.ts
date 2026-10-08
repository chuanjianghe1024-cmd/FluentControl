import { Pool } from "pg";
import { environment, type Environment } from "./env";

// A pool belongs to ONE request. Hyperdrive pools upstream connections across requests.
export async function withDatabase<T>(run: (pool: Pool) => Promise<T>, env?: Environment): Promise<T> {
  const config = env ?? await environment();
  const connectionString = config.HYPERDRIVE?.connectionString || config.DATABASE_URL;
  if (!connectionString) throw new Error("Database is not configured");
  const pool = new Pool({ connectionString, max: 3, connectionTimeoutMillis: 8000, idleTimeoutMillis: 5000, query_timeout: 15000 });
  try { return await run(pool); }
  finally { await pool.end(); }
}
