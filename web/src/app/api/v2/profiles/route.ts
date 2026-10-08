import { withDatabase } from "@/lib/db";
import { listBundles, saveBundle } from "@/lib/repository";
import { parsePublication } from "@/lib/bundle";
import { errorResponse, filtersFrom, readJson, writeIdentity } from "@/lib/http";
export const dynamic = "force-dynamic";
export async function GET(request: Request) {
  try { const filters = filtersFrom(new URL(request.url).searchParams); return Response.json(await withDatabase(pool => listBundles(pool, filters)), { headers: { "Cache-Control": "no-store" } }); }
  catch (error) { return errorResponse(error); }
}
export async function POST(request: Request) {
  try {
    const owner = await writeIdentity(request), publication = parsePublication(await readJson(request));
    const result = await withDatabase(pool => saveBundle(pool, owner, publication));
    return Response.json({ ...result, url: `/profiles/${result.id}` }, { status: 201 });
  } catch (error) { return errorResponse(error); }
}
