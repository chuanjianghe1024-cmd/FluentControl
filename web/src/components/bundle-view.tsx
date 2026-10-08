import { continuous, parameterNames, type Bundle } from "@/lib/bundle";
import { Icon } from "./icon";
export function BundleView({ bundle }: { bundle: Bundle }) {
  return <div className="scene-list">{bundle.groups.map((group, gi) => <section className="group-section" key={gi}><div className="section-label"><Icon name="layers" size={16} />{group.name}</div>{group.profiles.map((scene, si) => <details className="scene" key={si} open={gi === 0 && si === 0}>
    <summary><span><strong>{scene.name}</strong><span className="scene-meta">{scene.monitors.length} 台屏幕{scene.applications.length ? " · " + scene.applications.join(" / ") : ""}</span></span><span className="expand-mark">＋</span></summary>
    <div className="scene-content">{scene.monitors.map(monitor => <div className="monitor-settings" key={monitor.slot}><div className="monitor-heading"><Icon name="monitor" /><div><strong>{monitor.displayName || monitor.slot}</strong><p>{monitor.brand} · {monitor.modelName || monitor.modelId || "未识别型号"}{monitor.modelName && monitor.modelId ? ` (${monitor.modelId})` : ""}</p></div></div>
      <dl className="values">{Object.entries(monitor.values).map(([key, value]) => <div key={key}><dt>{parameterNames[key] || key}</dt><dd>{value}{continuous.has(key) ? "%" : ""}</dd></div>)}</dl>
      {monitor.brightness?.enabled && <p className="mapping-note">亮度映射 {monitor.brightness.minimum}–{monitor.brightness.maximum}% · 偏移 {monitor.brightness.offset} · 曲线 {monitor.brightness.curve}</p>}
    </div>)}</div>
  </details>)}</section>)}</div>;
}
