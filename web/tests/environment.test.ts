import test from "node:test";
import assert from "node:assert/strict";
import { environment, origin } from "../src/lib/env";

test("plain Next production retains the supplied database, session secret and origin", async () => {
  const values = {
    NODE_ENV: "production", NEXT_RUNTIME: "nodejs", APP_URL: "http://127.0.0.1:3100",
    DATABASE_URL: "postgresql://test-only@example.invalid/test",
    AUTH_SECRET: "environment-test-only-never-a-production-secret"
  };
  const previous = Object.fromEntries(Object.keys(values).map(key => [key, process.env[key]]));
  Object.assign(process.env, values);
  try {
    const env = await environment();
    assert.equal(env.DATABASE_URL, values.DATABASE_URL);
    assert.equal(env.AUTH_SECRET, values.AUTH_SECRET);
    assert.equal(origin(env), values.APP_URL);
  } finally {
    for (const [key, value] of Object.entries(previous)) {
      if (value === undefined) delete process.env[key]; else process.env[key] = value;
    }
  }
});
