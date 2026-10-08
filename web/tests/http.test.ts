import test from "node:test";
import assert from "node:assert/strict";
import { readJson, writeIdentity, filtersFrom } from "../src/lib/http";
import { HttpError } from "../src/lib/repository";

test("JSON upload enforces actual streamed size, even without Content-Length", async () => {
  const request = new Request("https://fctrl.app/api/v2/profiles", { method: "POST", headers: { "Content-Type": "application/json" }, body: " ".repeat(1024 * 1024 + 8193) });
  await assert.rejects(() => readJson(request), (e: unknown) => e instanceof HttpError && e.status === 413);
});
test("rejects non-JSON and invalid UTF-8 uploads", async () => {
  await assert.rejects(() => readJson(new Request("https://fctrl.app/api/v2/profiles", { method: "POST", body: "{}" })), (e: unknown) => e instanceof HttpError && e.status === 415);
  await assert.rejects(() => readJson(new Request("https://fctrl.app/api/v2/profiles", { method: "POST", headers: { "Content-Type": "application/json" }, body: new Uint8Array([0xff, 0xfe]) })), (e: unknown) => e instanceof HttpError && e.status === 400);
});
test("cross-origin writes fail before looking up a session", async () => {
  await assert.rejects(() => writeIdentity(new Request("https://fctrl.app/api/v2/profiles", { method: "POST", headers: { Origin: "https://other.example" } })), (e: unknown) => e instanceof HttpError && e.status === 403);
});
test("filter paging and maximum lengths are bounded", () => {
  assert.throws(() => filtersFrom(new URLSearchParams("page=0")));
  assert.throws(() => filtersFrom(new URLSearchParams({ q: "x".repeat(121) })));
  assert.equal(filtersFrom(new URLSearchParams("application=Photoshop&brand=Dell")).page, 1);
});
