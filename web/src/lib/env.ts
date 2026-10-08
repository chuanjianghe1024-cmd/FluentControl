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
  try { return (await getCloudflareContext({ async: true })).env as Environment; }
  catch { return process.env as Environment; }
}
export const origin = (env: Environment) => new URL(env.APP_URL || "https://fctrl.app").origin;
