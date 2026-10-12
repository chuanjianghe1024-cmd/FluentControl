# 安全问题 / Security policy

当前项目处于测试阶段，优先在最新正式版与当前 `main` 上确认问题，不承诺旧版本长期安全维护。普通设备兼容性、显示器拒绝调节或 UI 问题使用 Bug / 型号兼容性模板。

对于恶意配置导入、任意代码执行、更新包校验绕过、隐私泄露等潜在漏洞，请勿在公开 Issue、PR 或 Discussions 中提交利用代码、凭据或未修复漏洞的完整细节。

先查看 [Security 页面](https://github.com/chuanjianghe1024-cmd/FluentControl/security)。如果提供 **Report a vulnerability**，请通过该私密入口提交。若入口不可用，先开一个仅请求维护者提供私密报告渠道的 Issue，不包含漏洞技术细节；维护者确认渠道后再发送报告。此文档本身不代表 GitHub 私密报告功能已启用。

报告请包括受影响的版本或提交、Windows 环境、问题类型、最小复现、预期影响和经过脱敏的证据；已知的缓解措施也可一并提供。复现文件和日志仅在确认的私密渠道传递，删除令牌、序列号和本机路径。维护者会与报告者核对、修复和安排公开说明；目前没有固定响应 SLA 或漏洞赏金承诺。不要在其他用户设备上验证，或使用破坏性显示器指令证明问题。

## English

This project is in testing. Security issues are prioritized against the latest stable release and current `main`; long-term support for older releases is not promised. Use normal issue forms for compatibility and UI problems.

For suspected vulnerabilities such as unsafe imports, code execution, update-verification bypass or information disclosure, avoid public exploit details or credentials. Check the [Security page](https://github.com/chuanjianghe1024-cmd/FluentControl/security) and use **Report a vulnerability** if the private reporting option is available. If it is absent, open an issue requesting a private contact route without technical vulnerability details, and wait for the maintainer to establish that channel. This document does not claim private reporting has been enabled.

Include the affected version or commit, Windows environment, issue type, minimal reproduction, impact, redacted evidence and any known mitigation. Send reproduction files and logs only through the confirmed private channel; remove tokens, serial numbers and local paths. The maintainer will coordinate verification, remediation and disclosure; no fixed response SLA or bounty is promised. Do not test on other people's devices or use destructive monitor commands.
