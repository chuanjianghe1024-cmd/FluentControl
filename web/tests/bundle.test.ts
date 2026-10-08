import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { parseBundle, parsePublication, continuous, summarize } from "../src/lib/bundle";
import { fixture } from "./fixtures";

test("v2 round trip keeps multi-model scenes, aliases, Unicode and brightness mapping", () => {
  const value = parseBundle(JSON.parse(JSON.stringify(fixture())));
  assert.deepEqual(value, fixture()); assert.equal(summarize(value).scenes.length, 2);
});
test("legacy v1 becomes a v2 bundle usable by the desktop importer", () => {
  const scene = fixture().groups[0].profiles[0];
  const value = parseBundle({ ...scene, schema: "fluentcontrol.monitor-profile", version: 1 });
  assert.equal(value.version, 2); assert.deepEqual(value.groups[0].profiles[0], scene);
});
test("unknown model metadata and disconnected slots remain present", () => {
  const value = fixture(); value.groups[0].profiles[0].monitors[0].modelId = "";
  assert.equal(parseBundle(value).groups[0].profiles[0].monitors.length, 2);
});
test("rejects executable, private and unsupported control fields", () => {
  for (const key of ["power", "factory-reset", "osd", "firmware", "usage", "mccs", "run", "__proto__"]) {
    const value = fixture(); value.groups[0].profiles[0].monitors[0].values = JSON.parse(`{"${key}":1}`);
    assert.throws(() => parseBundle(value), key);
  }
  const value = fixture(); Object.assign(value.groups[0].profiles[0].monitors[0], { serialNumber: "not-public" });
  assert.throws(() => parseBundle(value));
});
test("validates ranges, duplicate slots, consistent model identity and limits", () => {
  const edits: ((value: ReturnType<typeof fixture>) => void)[] = [
    v => { v.groups[0].profiles[0].monitors[0].values.brightness = 101; },
    v => { v.groups[0].profiles[0].monitors[0].values.brightness = Infinity; },
    v => { v.groups[0].profiles[0].monitors[0].brightness!.minimum = 95; },
    v => { v.groups[0].profiles[0].monitors[1].slot = "left"; },
    v => { v.groups[0].profiles[1].monitors[0].modelId = "DEL9999"; },
    v => { v.name = ""; },
    v => { v.groups[0].profiles = Array(513).fill(v.groups[0].profiles[0]); }
  ];
  for (const edit of edits) { const value = fixture(); edit(value); assert.throws(() => parseBundle(value)); }
  assert.throws(() => parseBundle({ ...fixture(), name: "x".repeat(1024 * 1024) }));
});
test("publication metadata changes only the bundle title", () => {
  const value = parsePublication({ title: "新的名称", bundle: fixture() });
  assert.equal(value.bundle.name, "新的名称"); assert.deepEqual(value.bundle.groups, fixture().groups);
  assert.equal(parsePublication(fixture()).visibility, "public");
});
test("server control allowlist stays aligned with the desktop VCP catalogue", () => {
  const source = readFileSync(new URL("../../src/FluentControl/Services/VcpCatalog.cs", import.meta.url), "utf8");
  const lines = [...source.matchAll(/new\(0x([0-9A-Fa-f]+),"([^"]+)"[^\n]*?/g)];
  assert.ok(lines.length > 20);
  const supported = new Set([...continuous, "temperature", "color-preset", "gamma", "input", "monitor-mute", "scaling", "display-mode", "osd-language"]);
  for (const [_, code, key] of lines) {
    if (["CA", "D6", "04", "C0", "C9", "DF"].includes(code)) { assert.ok(!supported.has(key)); continue; }
    assert.ok(supported.has(key), `Missing desktop feature: ${key}`);
  }
});
