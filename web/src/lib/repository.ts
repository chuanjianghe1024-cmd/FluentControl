import type { Pool, PoolClient } from "pg";
import { summarize, type Bundle, type Publication } from "./bundle";

export class HttpError extends Error { constructor(public status: number, message: string) { super(message); } }
export type Filters = { q?: string; application?: string; modelId?: string; brand?: string; page?: number };
export interface BundleRecord {
  id: string; owner_id: number; title: string; description: string; visibility: "public" | "draft" | "deleted";
  revision: number; payload: Bundle; scene_count: number;
  models: { modelId: string; modelName: string; brand: string }[]; applications: string[];
  created_at: Date; updated_at: Date; author: string;
}
export type CardRecord = Omit<BundleRecord, "payload">;
const cardColumns = "b.id,b.owner_id,b.title,b.description,b.visibility,b.revision,b.scene_count,b.models,b.applications,b.created_at,b.updated_at,COALESCE(u.name,'FluentControl 用户') AS author";
const escaped = (input: string) => input.replace(/[\\%_]/g, "\\$&");
export async function listBundles(pool: Pool, filters: Filters, owner?: number) {
  const values: unknown[] = [];
  const parameter = (value: unknown) => { values.push(value); return `$${values.length}`; };
  const where = owner ? [`b.owner_id=${parameter(owner)}`, "b.visibility <> 'deleted'"] : ["b.visibility='public'"];
  if (filters.q) { const p = parameter(`%${escaped(filters.q)}%`); where.push(`(b.title ILIKE ${p} OR b.description ILIKE ${p})`); }
  const scene: string[] = ["s.bundle_id=b.id"];
  if (filters.application) scene.push(`EXISTS (SELECT 1 FROM unnest(s.applications) app WHERE app ILIKE ${parameter(`%${escaped(filters.application)}%`)})`);
  const monitor: string[] = [];
  if (filters.modelId) { const p = parameter(`%${escaped(filters.modelId)}%`); monitor.push(`(m->>'modelId' ILIKE ${p} OR m->>'modelName' ILIKE ${p})`); }
  if (filters.brand) monitor.push(`m->>'brand' ILIKE ${parameter(`%${escaped(filters.brand)}%`)}`);
  if (monitor.length) scene.push(`EXISTS (SELECT 1 FROM jsonb_array_elements(s.monitors) m WHERE ${monitor.join(" AND ")})`);
  if (scene.length > 1) where.push(`EXISTS (SELECT 1 FROM bundle_scenes s WHERE ${scene.join(" AND ")})`);
  const page = Math.max(1, Math.min(1000, filters.page || 1));
  values.push((page - 1) * 12);
  const result = await pool.query<CardRecord>(`SELECT ${cardColumns} FROM bundles b JOIN users u ON u.id=b.owner_id WHERE ${where.join(" AND ")} ORDER BY b.updated_at DESC,b.id LIMIT 13 OFFSET $${values.length}`, values);
  return { items: result.rows.slice(0, 12), hasMore: result.rows.length > 12, page };
}
export async function getBundle(pool: Pool, id: string, viewer?: number, revision?: number): Promise<BundleRecord> {
  const result = await pool.query<BundleRecord>(`SELECT ${cardColumns}, b.payload FROM bundles b JOIN users u ON u.id=b.owner_id WHERE b.id=$1 AND b.visibility <> 'deleted' AND (b.visibility='public' OR b.owner_id=$2)`, [id, viewer ?? null]);
  const row = result.rows[0];
  if (!row) throw new HttpError(404, "配置不存在或已停止分享。");
  if (revision && revision !== row.revision) {
    const old = await pool.query("SELECT payload,title,description,revision,created_at FROM bundle_versions WHERE bundle_id=$1 AND revision=$2 AND (published OR $3::boolean)", [id, revision, viewer === row.owner_id]);
    if (!old.rows[0]) throw new HttpError(404, "这个版本不存在。");
    Object.assign(row, old.rows[0]);
    const info = summarize(row.payload); row.scene_count = info.scenes.length; row.models = info.models; row.applications = info.applications;
  }
  return row;
}
async function transaction<T>(pool: Pool, run: (db: PoolClient) => Promise<T>): Promise<T> {
  const db = await pool.connect(); await db.query("BEGIN");
  try { const value = await run(db); await db.query("COMMIT"); return value; }
  catch (error) { await db.query("ROLLBACK"); throw error; }
  finally { db.release(); }
}
export async function saveBundle(pool: Pool, owner: number, publication: Publication, id?: string) {
  const { title, description, visibility, bundle, expectedRevision } = publication;
  const info = summarize(bundle);
  return transaction(pool, async db => {
    // Serialize writes per author for a modest upload limit and consistent revisions.
    const user = await db.query("SELECT id FROM users WHERE id=$1 FOR UPDATE", [owner]);
    if (!user.rowCount) throw new HttpError(401, "请重新登录。");
    const recent = await db.query("SELECT count(*)::int AS n FROM bundle_versions v JOIN bundles b ON b.id=v.bundle_id WHERE b.owner_id=$1 AND v.created_at > now()-interval '24 hours'", [owner]);
    if (recent.rows[0].n >= 100) throw new HttpError(429, "今天发布或更新次数较多，请明天再试。");
    let revision = 1;
    if (id) {
      const previous = await db.query("SELECT revision FROM bundles WHERE id=$1 AND owner_id=$2 AND visibility <> 'deleted' FOR UPDATE", [id, owner]);
      if (!previous.rows[0]) throw new HttpError(404, "配置不存在或你没有编辑权限。");
      if (expectedRevision !== previous.rows[0].revision) throw new HttpError(409, "配置已被更新，请刷新后再编辑，避免覆盖其他修改。");
      revision = previous.rows[0].revision + 1;
      await db.query("UPDATE bundles SET title=$3,description=$4,visibility=$5,revision=$6,payload=$7,scene_count=$8,models=$9,applications=$10,updated_at=now() WHERE id=$1 AND owner_id=$2", [id, owner, title, description, visibility, revision, JSON.stringify(bundle), info.scenes.length, JSON.stringify(info.models), info.applications]);
      await db.query("DELETE FROM bundle_scenes WHERE bundle_id=$1", [id]);
    } else {
      id = crypto.randomUUID();
      await db.query("INSERT INTO bundles(id,owner_id,title,description,visibility,revision,payload,scene_count,models,applications) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)", [id, owner, title, description, visibility, revision, JSON.stringify(bundle), info.scenes.length, JSON.stringify(info.models), info.applications]);
    }
    await db.query("INSERT INTO bundle_versions(bundle_id,revision,title,description,payload,published) VALUES($1,$2,$3,$4,$5,$6)", [id, revision, title, description, JSON.stringify(bundle), visibility === "public"]);
    const sceneRows = info.scenes.map((s, ordinal) => ({ ordinal, name: s.name, applications: s.applications, monitors: s.monitors.map(m => ({ modelId: m.modelId, modelName: m.modelName, brand: m.brand })) }));
    await db.query("INSERT INTO bundle_scenes(bundle_id,ordinal,name,applications,monitors) SELECT $1,x.ordinal,x.name,x.applications,x.monitors FROM jsonb_to_recordset($2::jsonb) AS x(ordinal int,name text,applications text[],monitors jsonb)", [id, JSON.stringify(sceneRows)]);
    return { id, revision };
  });
}
export async function deleteBundle(pool: Pool, id: string, owner: number, revision: number) {
  const result = await pool.query("UPDATE bundles SET visibility='deleted',updated_at=now() WHERE id=$1 AND owner_id=$2 AND revision=$3 AND visibility <> 'deleted' RETURNING id", [id, owner, revision]);
  if (!result.rowCount) throw new HttpError(409, "删除失败：配置已更新、已删除或你没有权限。请刷新后重试。");
}
