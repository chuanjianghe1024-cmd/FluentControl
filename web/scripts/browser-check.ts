import { chromium, type Browser } from "playwright";
import { spawn } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import assert from "node:assert/strict";
import { Pool } from "pg";
import { publication } from "../tests/fixtures";
import { saveBundle } from "../src/lib/repository";

const base = "http://127.0.0.1:3100";
const pool = process.env.DATABASE_URL ? new Pool({ connectionString: process.env.DATABASE_URL }) : null;
let userId: number | undefined;
const sessionToken = crypto.randomUUID();
const output = "test-results";
await mkdir(output, { recursive: true });
const server = spawn(process.execPath, ["node_modules/next/dist/bin/next", "start", "--hostname", "127.0.0.1", "--port", "3100"], {
  env: { ...process.env, APP_URL: base, AUTH_SECRET: "browser-test-only-secret-not-for-production-use" }, stdio: ["ignore", "pipe", "pipe"]
});
let logs = ""; server.stdout.on("data", d => { logs += d; }); server.stderr.on("data", d => { logs += d; });
let browser: Browser | undefined;
try {
  browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHANNEL, executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH, args: ["--no-sandbox"] });
  for (let i = 0; i < 80; i++) {
    try { if ((await fetch(base)).ok) break; } catch {}
    if (i === 79) throw new Error("Next.js preview did not start: " + logs);
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  if (pool) {
    userId = (await pool.query("INSERT INTO users(name,email) VALUES('界面测试作者',$1) RETURNING id", [crypto.randomUUID()+"@example.test"])).rows[0].id;
    const demo = publication(); demo.title = "双屏办公与游戏";
    await saveBundle(pool, userId!, demo);
    await pool.query('INSERT INTO sessions("userId",expires,"sessionToken") VALUES($1,$2,$3)', [userId, new Date(Date.now()+600000), sessionToken]);
  }
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  context.setDefaultTimeout(15000);
  context.setDefaultNavigationTimeout(15000);
  const page = await context.newPage(); const pageErrors: string[] = [];
  page.on("pageerror", error => pageErrors.push(error.message));
  await page.goto(base, { waitUntil: "networkidle" });
  await page.screenshot({ path: `${output}/desktop.png`, fullPage: true });
  assert.ok((await page.locator("h1").innerText()).includes("好画面"));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: `${output}/mobile.png`, fullPage: true });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, "Mobile viewport overflow");
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(`${base}/login`, { waitUntil: "networkidle" });
  await page.screenshot({ path: `${output}/login.png`, fullPage: true });
  if (pool) {
    await page.goto(`${base}/?application=Test+Game&modelId=HWV1234`);
    assert.equal(await page.locator(".profile-card").count(), 0, "Cross-scene filter mismatch");
    await context.addCookies([{ name: "authjs.session-token", value: sessionToken, url: base, httpOnly: true, sameSite: "Lax" }]);
    await page.goto(`${base}/publish`, { waitUntil: "networkidle" });
    assert.equal(new URL(page.url()).pathname, "/publish", "Database-backed session did not authenticate");
    assert.equal(await page.locator('input[type="file"]').count(), 1, "Publish form missing: " + await page.locator("body").innerText());
    await page.locator('input[type="file"]').setInputFiles({ name: "分享包.json", mimeType: "application/json", buffer: Buffer.from(JSON.stringify(publication().bundle)) });
    await page.getByLabel("配置包名称", { exact: true }).fill("浏览器发布测试");
    await page.screenshot({ path: `${output}/upload.png`, fullPage: true });
    await page.getByRole("button", { name: "发布配置包", exact: true }).click();
    await page.waitForURL(/\/profiles\/[a-f0-9-]+$/);
    await page.screenshot({ path: `${output}/detail.png`, fullPage: true });
    const id = page.url().split("/").pop()!;
    const json = await (await page.request.get(`${base}/api/v2/profiles/${id}`)).json();
    assert.equal(json.schema, "fluentcontrol.monitor-bundle"); assert.equal(json.groups[0].profiles.length, 2);
    const foreignWrite = await page.request.post(`${base}/api/v2/profiles`, { headers: { Origin: "https://other.example" }, data: publication() });
    assert.equal(foreignWrite.status(), 403);
    await page.getByRole("link", { name: "编辑配置 / 发布新版本" }).click();
    await page.getByLabel("可见范围").selectOption("draft");
    await page.getByRole("button", { name: "保存新版本", exact: true }).click();
    await page.waitForURL(new RegExp(`/profiles/${id}$`));
    assert.equal((await fetch(`${base}/api/v2/profiles/${id}?revision=1`)).status, 404, "Withdrawn versions must not be available anonymously");
    console.log("PASS: authenticated browser upload, preview, raw download, CSRF and withdrawal using real PostgreSQL.");
  }
  assert.deepEqual(pageErrors, []);
  console.log("PASS: desktop/mobile layout and login page; no browser errors or horizontal overflow.");
} catch (error) {
  console.error("Preview server diagnostics:\n" + logs);
  for (const [i, page] of (browser?.contexts().flatMap(context => context.pages()) ?? []).entries()) {
    await page.screenshot({ path: `${output}/failure-${i}.png`, fullPage: true }).catch(() => {});
    await writeFile(`${output}/failure-${i}.html`, await page.content()).catch(() => {});
  }
  throw error;
} finally {
  await browser?.close(); server.kill("SIGTERM");
  if (pool) {
    if (userId) { await pool.query("DELETE FROM bundles WHERE owner_id=$1", [userId]); await pool.query("DELETE FROM users WHERE id=$1", [userId]); }
    await pool.end();
  }
}
