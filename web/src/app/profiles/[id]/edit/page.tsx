import { notFound, redirect } from "next/navigation";
import { currentUser } from "@/lib/auth";
import { withDatabase } from "@/lib/db";
import { getBundle, HttpError } from "@/lib/repository";
import { uuid } from "@/lib/http";
import { PublishForm } from "@/components/publish-form";
export const metadata = { title: "编辑配置" };
export default async function Edit({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; if (!uuid.safeParse(id).success) notFound();
  const user = await currentUser(); if (!user?.id) redirect(`/login?next=/profiles/${id}/edit`);
  const item = await withDatabase(pool => getBundle(pool, id, Number(user.id))).catch(error => { if (error instanceof HttpError && error.status === 404) notFound(); throw error; });
  if (item.owner_id !== Number(user.id) || item.visibility === "deleted") notFound();
  return <><div className="page-heading"><div className="eyebrow">REFINE YOUR SETUP</div><h1>把配置，再调好一点</h1><p>保存会创建新版本。已有下载链接仍指向原版本，停止分享后所有链接都会关闭。</p></div><PublishForm existing={{ id: item.id, title: item.title, description: item.description, visibility: item.visibility, revision: item.revision, payload: item.payload }} /></>;
}
