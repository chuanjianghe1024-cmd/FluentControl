import Link from "next/link";
import { environment } from "@/lib/env";
import { LoginButtons } from "@/components/account";
import { Icon } from "@/components/icon";
export const metadata = { title: "登录" };
export default async function Login({ searchParams }: { searchParams: Promise<{ next?: string; error?: string }> }) {
  const query = await searchParams; const env = await environment();
  const providers: string[] = [];
  if (env.AUTH_SECRET && (env.DATABASE_URL || env.HYPERDRIVE)) {
    if (env.AUTH_GITHUB_ID && env.AUTH_GITHUB_SECRET) providers.push("github");
    if (env.AUTH_GOOGLE_ID && env.AUTH_GOOGLE_SECRET) providers.push("google");
  }
  const next = query.next?.startsWith("/") && !query.next.startsWith("//") && !query.next.includes("\\") ? query.next : "/me";
  return <div className="login-page"><div className="login-card"><span className="brand-icon large"><Icon name="sliders" size={32} /></span><div className="eyebrow">WELCOME TO FLUENTCONTROL</div><h1>把好配置，留在这里。</h1><p>登录后发布、管理你的显示器配置包。<br />浏览和下载公开配置无需登录。</p>{query.error && <p className="error" role="alert">{query.error === "OAuthAccountNotLinked" ? "该邮箱已有账号，请使用原来的登录方式。" : "这次登录没有完成，请重试。"}</p>}<LoginButtons providers={providers} destination={next} /><p className="login-note">继续即表示你已阅读<Link href="/privacy">隐私说明</Link>和<Link href="/terms">使用说明</Link>。</p><Link className="text-button" href="/">先逛逛社区 →</Link></div></div>;
}
