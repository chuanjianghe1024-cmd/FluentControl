"use client";
import { signIn, signOut } from "next-auth/react";
import { useState } from "react";
import { Icon } from "./icon";

export function LoginButtons({ providers, destination }: { providers: string[]; destination: string }) {
  const [busy, setBusy] = useState(""); const [error, setError] = useState("");
  async function login(provider: string) {
    setBusy(provider); setError("");
    try { await signIn(provider, { callbackUrl: destination }); }
    catch { setError("暂时无法登录，请稍后重试。"); setBusy(""); }
  }
  return <div className="login-actions">{["github", "google"].map(provider => <button key={provider} className="button provider" disabled={!providers.includes(provider) || !!busy} onClick={() => login(provider)}>
    {provider === "github" ? <Icon name="github" /> : <span className="google-g" aria-hidden="true">G</span>}
    {busy === provider ? "正在跳转…" : `使用 ${provider === "github" ? "GitHub" : "Google"} 继续`}
  </button>)}{error && <p className="error" role="alert">{error}</p>}{providers.length === 0 && <p className="muted">登录正在准备中。你可以先浏览与下载公开配置。</p>}</div>;
}
export function Logout() { return <button className="text-button" onClick={() => signOut({ callbackUrl: "/" })}>退出</button>; }
export function CopyLink({ value }: { value: string }) {
  const [label, setLabel] = useState("复制导入链接");
  return <button className="button subtle" onClick={async () => { try { await navigator.clipboard.writeText(value); setLabel("已复制"); } catch { setLabel("复制失败，请选择下方链接"); } }}>{label}</button>;
}
