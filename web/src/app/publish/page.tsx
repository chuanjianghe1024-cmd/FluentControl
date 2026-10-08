import { redirect } from "next/navigation";
import { currentUser } from "@/lib/auth";
import { PublishForm } from "@/components/publish-form";
export const metadata = { title: "分享配置" };
export default async function Publish() {
  const user = await currentUser(); if (!user) redirect("/login?next=/publish");
  return <><div className="page-heading"><div className="eyebrow">SHARE YOUR SETUP</div><h1>分享你的屏幕配方</h1><p>一次上传多个场景，让相同型号的屏幕有更多可能。</p></div><PublishForm /></>;
}
