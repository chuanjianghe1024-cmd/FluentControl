import NextAuth, { type NextAuthConfig } from "next-auth";
import GitHub from "next-auth/providers/github";
import Google from "next-auth/providers/google";
import PostgresAdapter from "@auth/pg-adapter";
import type { Pool } from "pg";
import { environment, origin, type Environment } from "./env";
import { withDatabase } from "./db";

function configuration(pool: Pool, env: Environment): NextAuthConfig {
  if (!env.AUTH_SECRET || env.AUTH_SECRET.length < 32) throw new Error("AUTH_SECRET is not configured");
  const providers: NextAuthConfig["providers"] = [];
  if (env.AUTH_GITHUB_ID && env.AUTH_GITHUB_SECRET) providers.push(GitHub({ clientId: env.AUTH_GITHUB_ID, clientSecret: env.AUTH_GITHUB_SECRET }));
  if (env.AUTH_GOOGLE_ID && env.AUTH_GOOGLE_SECRET) providers.push(Google({ clientId: env.AUTH_GOOGLE_ID, clientSecret: env.AUTH_GOOGLE_SECRET }));
  return {
    adapter: PostgresAdapter(pool), secret: env.AUTH_SECRET, providers,
    trustHost: true, useSecureCookies: origin(env).startsWith("https:"),
    session: { strategy: "database", maxAge: 30 * 24 * 60 * 60 },
    pages: { signIn: "/login", error: "/login" },
    callbacks: {
      session({ session, user }) { session.user.id = String(user.id); return session; },
      redirect({ url }) {
        const base = origin(env);
        try { const target = new URL(url, base); return target.origin === base ? target.href : base; }
        catch { return base; }
      }
    }
  };
}
export async function authHandler(request: Request, method: "GET" | "POST") {
  const env = await environment();
  if (new URL(request.url).origin !== origin(env)) return Response.json({ error: "登录地址与站点不一致。" }, { status: 400 });
  try { return await withDatabase(pool => NextAuth(configuration(pool, env)).handlers[method](request as never), env); }
  catch { console.error("Authentication service unavailable"); return Response.json({ error: "登录暂不可用，请稍后再试。" }, { status: 503 }); }
}
export async function currentUser() {
  const env = await environment();
  if (!env.AUTH_SECRET || (!env.DATABASE_URL && !env.HYPERDRIVE)) return null;
  return withDatabase(async pool => (await NextAuth(configuration(pool, env)).auth())?.user ?? null, env);
}
