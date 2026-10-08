"use client";
import { useState, useRef, type DragEvent } from "react";
import { useRouter } from "next/navigation";
import { parseBundle, summarize, type Bundle } from "@/lib/bundle";
import { BundleView } from "./bundle-view";
import { Icon } from "./icon";

export type Existing = { id: string; title: string; description: string; visibility: "public" | "draft"; revision: number; payload: Bundle };
export function PublishForm({ existing }: { existing?: Existing }) {
  const router = useRouter(); const fileInput = useRef<HTMLInputElement>(null);
  const [bundle, setBundle] = useState<Bundle | null>(existing?.payload ?? null);
  const [title, setTitle] = useState(existing?.title ?? ""), [description, setDescription] = useState(existing?.description ?? "");
  const [visibility, setVisibility] = useState(existing?.visibility ?? "public");
  const [error, setError] = useState(""), [busy, setBusy] = useState(false), [dragging, setDragging] = useState(false), [deleting, setDeleting] = useState(false);
  async function load(file?: File) {
    if (!file) return; setError("");
    try { if (file.size > 1024 * 1024) throw new Error("配置包不能超过 1 MiB。"); const value = parseBundle(JSON.parse(await file.text())); setBundle(value); if (!existing) setTitle(value.name); }
    catch (cause) { setError(cause instanceof SyntaxError ? "这不是有效的 JSON 文件。" : cause instanceof Error && !cause.message.startsWith("[") ? cause.message : "配置格式不兼容。请使用桌面端“分享 / 导出”生成的 JSON 文件。"); }
  }
  async function submit(event: React.FormEvent) {
    event.preventDefault(); if (!bundle || busy) return; setBusy(true); setError("");
    try {
      const response = await fetch(`/api/v2/profiles${existing ? "/" + existing.id : ""}`, { method: existing ? "PATCH" : "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ title, description, visibility, bundle, ...(existing ? { expectedRevision: existing.revision } : {}) }) });
      const result = await response.json(); if (!response.ok) throw new Error(result.error || "保存失败，请重试。");
      router.push(`/profiles/${result.id}`); router.refresh();
    } catch (cause) { setError(cause instanceof Error ? cause.message : "网络连接失败。"); setBusy(false); }
  }
  async function remove() {
    if (!existing || busy) return; setBusy(true); setError("");
    try { const response = await fetch(`/api/v2/profiles/${existing.id}`, { method: "DELETE", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ expectedRevision: existing.revision }) }); if (!response.ok) throw new Error((await response.json()).error); router.push("/me"); router.refresh(); }
    catch (cause) { setError(cause instanceof Error ? cause.message : "删除失败。"); setBusy(false); setDeleting(false); }
  }
  function drop(event: DragEvent) { event.preventDefault(); setDragging(false); void load(event.dataTransfer.files[0]); }
  const info = bundle ? summarize(bundle) : null;
  return <div className="publish-layout"><form className="editor-panel" onSubmit={submit}>
    <div className={`dropzone ${dragging ? "dragging" : ""}`} onDragOver={e => { e.preventDefault(); setDragging(true); }} onDragLeave={() => setDragging(false)} onDrop={drop}>
      <span className="upload-symbol"><Icon name="upload" size={30} /></span><h3>{bundle ? "配置包已载入" : "把配置包放到这里"}</h3><p>{bundle ? `${info!.scenes.length} 个场景 · ${info!.models.length} 种型号` : "从 FluentControl 导出的 JSON 文件，最大 1 MiB"}</p>
      <button type="button" className="button subtle" onClick={() => fileInput.current?.click()}>{bundle ? "换一个文件" : "选择文件"}</button>
      <input ref={fileInput} type="file" accept=".json,application/json" hidden onChange={e => { void load(e.target.files?.[0]); e.target.value = ""; }} />
    </div>
    <label className="field">配置包名称<input value={title} onChange={e => setTitle(e.target.value)} maxLength={80} placeholder="例如：我的 MateView · 办公与夜间" required /></label>
    <label className="field">使用说明<textarea value={description} onChange={e => setDescription(e.target.value)} maxLength={2000} rows={5} placeholder="介绍适用场景、显示器模式，以及使用时的建议。" /></label>
    <label className="field">可见范围<select value={visibility} onChange={e => setVisibility(e.target.value as "public" | "draft")}><option value="public">公开分享 · 所有人可以浏览和下载</option><option value="draft">仅自己可见 · 保存为草稿</option></select></label>
    <p className="form-note">应用、型号和品牌由配置文件自动读取。修改这些信息后，可以从桌面端重新导出上传。</p>
    {error && <p className="error" role="alert">{error}</p>}
    <div className="form-actions"><button className="button primary" disabled={!bundle || busy}>{busy ? "正在保存…" : existing ? "保存新版本" : visibility === "draft" ? "保存草稿" : "发布配置包"}<Icon name="arrow" size={17} /></button>{existing && <button className="text-button danger" type="button" disabled={busy} onClick={() => setDeleting(true)}>删除配置包</button>}</div>
  </form><aside className="preview-panel"><div className="preview-title"><span className="eyebrow">内容预览</span><span className="status-dot" />{bundle ? "已通过格式检查" : "等待选择文件"}</div>
    {bundle ? <><div className="tags">{info!.applications.map(app => <span key={app}>{app}</span>)}</div><BundleView bundle={bundle} /></> : <div className="preview-empty"><Icon name="layers" size={42} /><h3>一份配置，多个好用的场景</h3><p>选择文件后，可以在这里逐个查看显示器、参数与亮度映射。</p><div className="preview-lines"><i /><i /><i /></div></div>}
  </aside>{deleting && <div className="modal-backdrop"><div className="modal" role="alertdialog" aria-modal="true" aria-labelledby="delete-title"><h2 id="delete-title">删除“{existing?.title}”？</h2><p>删除后，分享页面及全部版本的下载链接将停止访问。</p><div className="form-actions"><button className="button subtle" onClick={() => setDeleting(false)} disabled={busy}>取消</button><button className="button danger-button" onClick={remove} disabled={busy}>确认删除</button></div></div></div>}</div>;
}
