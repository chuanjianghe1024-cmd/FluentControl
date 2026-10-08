import { withDatabase } from "@/lib/db";
import { deleteBundle, getBundle, saveBundle } from "@/lib/repository";
import { parsePublication } from "@/lib/bundle";
import { errorResponse, readJson, uuid, writeIdentity } from "@/lib/http";
import { z } from "zod";
export const dynamic = "force-dynamic";
type Context = { params: Promise<{ id: string }> };
export async function GET(request: Request, context: Context) {
  try {
    const id = uuid.parse((await context.params).id), requested = new URL(request.url).searchParams.get("revision");
    const revision = requested ? z.coerce.number().int().positive().parse(requested) : undefined;
    const record = await withDatabase(pool => getBundle(pool, id, undefined, revision));
    return new Response(JSON.stringify(record.payload, null, 2), { headers: {
      "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store",
      "Content-Disposition": `attachment; filename="fluentcontrol-${id}-v${record.revision}.json"; filename*=UTF-8''${encodeURIComponent(record.title + "-v" + record.revision + ".json")}`
    } });
  } catch (error) { return errorResponse(error); }
}
export async function PATCH(request: Request, context: Context) {
  try {
    const owner = await writeIdentity(request), id = uuid.parse((await context.params).id), publication = parsePublication(await readJson(request));
    return Response.json(await withDatabase(pool => saveBundle(pool, owner, publication, id)));
  } catch (error) { return errorResponse(error); }
}
export async function DELETE(request: Request, context: Context) {
  try {
    const owner = await writeIdentity(request), id = uuid.parse((await context.params).id);
    const { expectedRevision } = z.object({ expectedRevision: z.number().int().positive() }).parse(await readJson(request));
    await withDatabase(pool => deleteBundle(pool, id, owner, expectedRevision)); return new Response(null, { status: 204 });
  } catch (error) { return errorResponse(error); }
}
