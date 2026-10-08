import Link from "next/link";
import { notFound } from "next/navigation";
import { currentUser } from "@/lib/auth";
import { withDatabase } from "@/lib/db";
import { getBundle, HttpError } from "@/lib/repository";
import { uuid } from "@/lib/http";
import { environment, origin } from "@/lib/env";
import { BundleView } from "@/components/bundle-view";
import { CopyLink } from "@/components/account";
import { Icon } from "@/components/icon";
import { date } from "@/components/cards";
export default async function Detail({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params; if (!uuid.safeParse(id).success) notFound();
  const user = await currentUser();
  const { item, versions } = await withDatabase(async pool => {
    try { const item = await getBundle(pool, id, user?.id ? Number(user.id) : undefined); const versions = (await pool.query<{ revision: number; created_at: Date }>("SELECT revision,created_at FROM bundle_versions WHERE bundle_id=$1 AND published ORDER BY revision DESC LIMIT 30", [id])).rows; return { item, versions }; }
    catch (error) { if (error instanceof HttpError && error.status === 404) notFound(); throw error; }
  });
  const download = `${origin(await environment())}/api/v2/profiles/${id}?revision=${item.revision}`;
  return <><Link className="back-link" href="/">← 返回配置社区</Link><div className="detail-heading"><span className="eyebrow">{item.models.map(m => m.brand || "未知品牌").join(" / ")} · {item.visibility === "draft" ? "私人草稿" : "公开配置包"}</span><h1>{item.title}</h1><div className="detail-author"><span className="avatar">{item.author.slice(0, 1)}</span>{item.author}<span>更新于 {date(item.updated_at)}</span><span>版本 {item.revision}</span></div><div className="tags">{item.applications.map(app => <Link key={app} href={`/?application=${encodeURIComponent(app)}#explore`}>{app}</Link>)}</div></div>
    <div className="detail-layout"><div><div className="description-box"><h2>关于这份配置</h2><p>{item.description || "作者还没有添加使用说明。你可以在下方预览完整参数。"}</p></div><div className="section-heading compact"><h2>配置内容</h2><span className="muted">{item.scene_count} 个场景 · {item.models.length} 种型号</span></div><BundleView bundle={item.payload} /></div><aside className="download-panel"><div className="download-icon"><Icon name="download" size={28} /></div><h2>把好画面带回桌面</h2><p>下载后在 FluentControl 中导入，或粘贴导入链接。</p>{item.visibility === "public" ? <><a className="button primary wide" href={download}><Icon name="download" size={18} />下载全部场景</a><CopyLink value={download} /><input className="copy-url" readOnly value={download} aria-label="导入链接" /></> : <p className="notice">这是私人草稿。公开发布后，就能生成分享下载链接。</p>}{Number(user?.id) === item.owner_id && <Link className="button subtle wide" href={`/profiles/${id}/edit`}>编辑配置 / 发布新版本</Link>}<hr /><h3>适用显示器</h3>{item.models.map((m, i) => <div className="target-model" key={i}><Icon name="monitor" size={18} /><div><strong>{m.modelName || m.modelId || "未识别型号"}</strong><small>{m.brand} {m.modelId}</small></div></div>)}<p className="form-note">同型号的固件、HDR 和显示模式可能不同。导入后由客户端检查可用参数，不会自动改变屏幕设置。</p>{item.visibility === "public" && <details className="versions"><summary>历史版本</summary>{versions.map(v => <a key={v.revision} href={`/api/v2/profiles/${id}?revision=${v.revision}`}>v{v.revision}<span>{date(v.created_at)}</span><Icon name="download" size={14} /></a>)}</details>}</aside></div></>;
}
