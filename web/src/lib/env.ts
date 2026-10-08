import { getCloudflareContext } from "@opennextjs/cloudflare";

export interface Environment {
  APP_URL?: string;
  DATABASE_URL?: string;
  HYPERDRIVE?: { connectionString: string };
  AUTH_SECRET?: string;
  AUTH_GITHUB_ID?: string;
  AUTH_GITHUB_SECRET?: string;
  AUTH_GOOGLE_ID?: string;
  AUTH_GOOGLE_SECRET?: string;
}
export async function environment(): Promise<Environment> {
  // The Worker and initialized dev server already expose this context.
  // Async lookup in plain `next start` would create a Wrangler proxy and
  // hide the real process DATABASE_URL / AUTH_SECRET behind local bindings.
  try { return getCloudflareContext().env as Environment; }
  catch {
    if (process.env.NODE_ENV === "development") {
      try { return (await getCloudflareContext({ async: true })).env as Environment; }
      catch { /* Plain Node development can use process variables. */ }
    }
    return process.env as Environment;
  }
}
export const origin = (env: Environment) => new URL(env.APP_URL || "https://fctrl.app").origin;
