import Link from "next/link";
import { redirect } from "next/navigation";
import { currentUser } from "@/lib/auth";
import { withDatabase } from "@/lib/db";
import { listBundles } from "@/lib/repository";
import { Cards } from "@/components/cards";
import { Icon } from "@/components/icon";
export const metadata = { title: "我的配置" };
export default async function Me({ searchParams }: { searchParams: Promise<{ page?: string }> }) {
  const user = await currentUser(); if (!user?.id) redirect("/login?next=/me");
  const query = await searchParams; const page = Math.max(1, Math.min(1000, Number(query.page) || 1));
  const result = await withDatabase(pool => listBundles(pool, { page: Math.floor(page) }, Number(user.id)));
  return <><div className="page-heading heading-actions"><div><div className="eyebrow">YOUR COLLECTION</div><h1>我的配置</h1><p>你的公开分享和私人草稿，都在这里。</p></div><Link className="button primary" href="/publish"><Icon name="upload" size={17} />分享配置</Link></div>{result.items.length ? <Cards items={result.items} /> : <div className="empty-state"><Icon name="layers" size={40} /><h3>把第一个场景留在这里</h3><p>从桌面端导出配置包，然后上传即可。</p><Link href="/publish" className="button primary">上传配置包</Link></div>}<div className="pagination">{page > 1 && <Link className="button subtle" href={`/me?page=${page - 1}`}>上一页</Link>}{result.hasMore && <Link className="button subtle" href={`/me?page=${page + 1}`}>下一页</Link>}</div></>;
}
