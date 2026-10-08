import test from "node:test";
import assert from "node:assert/strict";
import { Pool } from "pg";
import PostgresAdapter from "@auth/pg-adapter";
import { saveBundle, getBundle, listBundles, deleteBundle, HttpError } from "../src/lib/repository";
import { parseBundle } from "../src/lib/bundle";
import { publication } from "./fixtures";

test("PostgreSQL integration: login storage, correlated filters, ownership, revisions and withdrawal", { skip: !process.env.DATABASE_URL }, async () => {
  const pool = new Pool({ connectionString: process.env.DATABASE_URL });
  let owner: number | undefined, stranger: number | undefined;
  try {
    const adapter = PostgresAdapter(pool);
    const user = await adapter.createUser!({ id: "generated-by-postgres", name: "作者", email: crypto.randomUUID()+"@example.test", emailVerified: null, image: null });
    owner = Number(user.id);
    stranger = (await pool.query("INSERT INTO users(name,email) VALUES('另一位作者',$1) RETURNING id", [crypto.randomUUID()+"@example.test"])).rows[0].id;
    await adapter.linkAccount!({ userId: String(owner), provider: "github", providerAccountId: "test-"+owner, type: "oauth" });
    assert.equal(Number((await adapter.getUserByAccount!({ provider: "github", providerAccountId: "test-"+owner }))?.id), owner);
    const sessionToken = crypto.randomUUID();
    await adapter.createSession!({ userId: String(owner), sessionToken, expires: new Date(Date.now()+60000) });
    assert.equal(Number((await adapter.getSessionAndUser!(sessionToken))?.user.id), owner);
    await adapter.deleteSession!(sessionToken); assert.equal(await adapter.getSessionAndUser!(sessionToken), null);

    const input = publication(); const first = await saveBundle(pool, owner, input);
    assert.deepEqual(parseBundle((await getBundle(pool, first.id)).payload), input.bundle);
    assert.equal((await listBundles(pool, { modelId: "HWV1234", brand: "Huawei", application: "Photoshop" })).items.some(b=>b.id===first.id), true);
    assert.equal((await listBundles(pool, { modelId: "HWV1234", brand: "Dell" })).items.some(b=>b.id===first.id), false, "Model and brand must belong to the same monitor");
    assert.equal((await listBundles(pool, { modelId: "HWV1234", application: "Test Game" })).items.some(b=>b.id===first.id), false, "Application and model must belong to the same scene");
    assert.equal((await listBundles(pool, { application: "photoshop", modelId: "mateview" })).items.some(b=>b.id===first.id), true);
    assert.equal((await listBundles(pool, { q: "%' OR 1=1 --" })).items.length, 0);
    await assert.rejects(() => saveBundle(pool, stranger!, { ...input, expectedRevision: 1 }, first.id), (e: unknown) => e instanceof HttpError && e.status===404);
    const update = { ...input, title: "新版本", expectedRevision: 1 };
    const competing = await Promise.allSettled([saveBundle(pool, owner, update, first.id), saveBundle(pool, owner, update, first.id)]);
    assert.equal(competing.filter(r=>r.status==="fulfilled").length, 1);
    assert.equal((await getBundle(pool, first.id, undefined, 1)).title, input.title);
    await saveBundle(pool, owner, { ...input, visibility: "draft", expectedRevision: 2 }, first.id);
    await assert.rejects(() => getBundle(pool, first.id, undefined, 1), (e: unknown) => e instanceof HttpError && e.status===404);
    assert.equal((await getBundle(pool, first.id, owner)).visibility, "draft");
    await saveBundle(pool, owner, { ...input, expectedRevision: 3 }, first.id);
    await assert.rejects(() => getBundle(pool, first.id, undefined, 3), (e: unknown) => e instanceof HttpError && e.status===404, "Previously private drafts must never leak through version downloads");
    assert.equal((await getBundle(pool, first.id, undefined, 1)).revision, 1);
    await assert.rejects(() => deleteBundle(pool, first.id, stranger!, 4));
    await deleteBundle(pool, first.id, owner, 4);
    await assert.rejects(() => getBundle(pool, first.id, undefined, 1));
    assert.ok(!(await listBundles(pool, {}, owner)).items.some(b=>b.id===first.id));
  } finally {
    if (owner) { await pool.query("DELETE FROM bundles WHERE owner_id=$1", [owner]); await pool.query("DELETE FROM users WHERE id=$1", [owner]); }
    if (stranger) await pool.query("DELETE FROM users WHERE id=$1", [stranger]);
    await pool.end();
  }
});
