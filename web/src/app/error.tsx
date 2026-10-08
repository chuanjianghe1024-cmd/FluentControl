"use client";
export default function ErrorPage({ reset }: { reset: () => void }) { return <div className="empty-state tall"><h1>暂时没有连接上</h1><p>你的本地配置不受影响，请稍后再试。</p><button className="button primary" onClick={reset}>重试</button></div>; }
