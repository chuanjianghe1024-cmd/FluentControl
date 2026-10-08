import { z } from "zod";
import { environment, origin } from "./env";
import { currentUser } from "./auth";
import { HttpError } from "./repository";
import type { Filters } from "./repository";
import { BundleError } from "./bundle";

export const uuid = z.string().uuid();
export function filtersFrom(params: URLSearchParams): Filters {
  return z.object({ q: z.string().trim().max(120).optional(), application: z.string().trim().max(80).optional(), modelId: z.string().trim().max(128).optional(), brand: z.string().trim().max(80).optional(), page: z.coerce.number().int().min(1).max(1000).default(1) }).parse(Object.fromEntries(params));
}
export async function readJson(request: Request) {
  if (!request.headers.get("content-type")?.toLowerCase().startsWith("application/json")) throw new HttpError(415, "请上传 JSON 配置。");
  const max = 1024 * 1024 + 8192;
  if (Number(request.headers.get("content-length")) > max) throw new HttpError(413, "文件太大，配置包最多 1 MiB。");
  const reader = request.body?.getReader(); if (!reader) throw new HttpError(400, "请求内容为空。");
  const chunks: Uint8Array[] = []; let size = 0;
  try {
    while (true) { const part = await reader.read(); if (part.done) break; size += part.value.byteLength; if (size > max) { await reader.cancel(); throw new HttpError(413, "文件太大，配置包最多 1 MiB。"); } chunks.push(part.value); }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  try { return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)); }
  catch { throw new HttpError(400, "JSON 文件格式无效。"); }
}
export async function writeIdentity(request: Request) {
  if (request.headers.get("origin") !== origin(await environment())) throw new HttpError(403, "请从本站提交操作。");
  const user = await currentUser();
  if (!user?.id) throw new HttpError(401, "请先登录。");
  return Number(user.id);
}
export function errorResponse(error: unknown) {
  if (error instanceof BundleError) return Response.json({ error: error.message }, { status: 400 });
  if (error instanceof HttpError) return Response.json({ error: error.message }, { status: error.status });
  if (error instanceof z.ZodError) return Response.json({ error: "配置格式无效，请使用 FluentControl 导出的显示器分享文件。", details: error.issues.slice(0, 5).map(i => ({ path: i.path.join("."), message: i.message })) }, { status: 400 });
  // Avoid exposing SQL, connection strings or OAuth credentials in responses/logs.
  console.error("Community request failed");
  return Response.json({ error: "服务暂不可用，请稍后重试。" }, { status: 503 });
}
