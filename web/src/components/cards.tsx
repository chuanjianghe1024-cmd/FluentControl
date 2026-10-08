import Link from "next/link";
import type { CardRecord } from "@/lib/repository";
import { Icon } from "./icon";
export function date(value: Date | string) { return new Date(value).toLocaleDateString("zh-CN", { year: "numeric", month: "short", day: "numeric", timeZone: "Asia/Shanghai" }); }
export function Cards({ items }: { items: CardRecord[] }) {
  return <div className="card-grid">{items.map((item, index) => <Link key={item.id} className="profile-card" href={`/profiles/${item.id}`}>
    <div className={`card-art tint-${index % 3}`}><span className="card-art-mark"><Icon name="monitor" size={58} /><span className="mini-sliders"><i /><i /><i /></span></span><span className="glass-tag">{item.scene_count} 个场景</span></div>
    <div className="card-body"><div className="eyebrow">{item.models.slice(0, 2).map(m => m.brand || "未知品牌").join(" / ")}<span>v{item.revision}{item.visibility === "draft" ? " · 草稿" : ""}</span></div>
      <h3>{item.title}</h3><p className="model-line">{item.models.map(m => m.modelName || m.modelId || "未识别型号").join(" · ")}</p>
      <p className="card-description">{item.description || "发现适合你的显示器参数组合。"}</p>
      <div className="tags">{item.applications.slice(0, 3).map(app => <span key={app}>{app}</span>)}{item.applications.length > 3 && <span>+{item.applications.length - 3}</span>}</div>
      <div className="card-foot"><span className="author"><b>{item.author.slice(0, 1)}</b>{item.author}</span><span>{date(item.updated_at)}</span></div>
    </div></Link>)}</div>;
}
