import Link from "next/link";
export default function NotFound() { return <div className="empty-state tall"><h1>这份配置暂时不在这里</h1><p>它可能已被删除、停止分享，或链接有误。</p><Link className="button primary" href="/">返回配置社区</Link></div>; }
