import type { Metadata } from "next";
import Link from "next/link";
import { currentUser } from "@/lib/auth";
import { Logout } from "@/components/account";
import { Icon } from "@/components/icon";
import "./globals.css";
export const dynamic = "force-dynamic";
export const metadata: Metadata = { metadataBase: new URL("https://fctrl.app"), title: { default: "FluentControl · 找到你的屏幕配方", template: "%s · FluentControl" }, description: "发现、分享和管理显示器配置。按应用、游戏、型号和品牌找到适合自己的画面。" };
export default async function Layout({ children }: { children: React.ReactNode }) {
  const user = await currentUser().catch(() => null);
  return <html lang="zh-CN"><body><a className="skip" href="#main">跳到主要内容</a><header className="site-header"><nav className="nav wrap"><Link className="brand" href="/"><span className="brand-icon"><Icon name="sliders" size={24} /></span><span>FluentControl<small>屏幕配置社区</small></span></Link><div className="nav-links"><Link href="/">发现配置</Link><Link href="/me">我的配置</Link><Link className="button subtle nav-publish" href="/publish"><Icon name="upload" size={16} />分享配置</Link>{user ? <><span className="nav-user" title={user.name || ""}>{user.name || "用户"}</span><Logout /></> : <Link className="login-link" href="/login">登录 <span>↗</span></Link>}</div></nav></header><main id="main" className="wrap">{children}</main><footer className="site-footer wrap"><span className="brand-footer"><Icon name="sliders" size={17} />FluentControl<span>让每一块屏幕，恰到好处。</span></span><div><Link href="/privacy">隐私</Link><Link href="/terms">使用说明</Link><a href="https://github.com/chuanjianghe1024-cmd/FluentControl">GitHub ↗</a></div></footer></body></html>;
}
