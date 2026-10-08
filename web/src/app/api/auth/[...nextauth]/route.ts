import { authHandler } from "@/lib/auth";
export const dynamic = "force-dynamic";
export const GET = (request: Request) => authHandler(request, "GET");
export const POST = (request: Request) => authHandler(request, "POST");
