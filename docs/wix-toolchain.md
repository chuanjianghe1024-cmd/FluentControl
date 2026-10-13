# WiX 工具链审查（2026-10-13）

结论：暂时保留清单中的 WiX 5.0.2，建议关闭仅升级工具的 [PR #3](https://github.com/chuanjianghe1024-cmd/FluentControl/pull/3)。WiX 7 迁移需单独确认许可并完成兼容性验收；本修复不接受任何新协议，不更新正式 Release。

## 故障证据

- PR head `6dd0b7055c8117b26fdbb6f3d8e8abcfdc5490dc` 仅把工具从 5.0.2 改为 7.0.0。
- [Windows run 38269140092](https://github.com/chuanjianghe1024-cmd/FluentControl/actions/runs/38269140092) 的 job `114878797689` 在 UI 扩展恢复时报告 WIX7015。应用编译、测试、发布和启动检查成功；MSI 构建失败，安装/升级/卸载步骤跳过。
- 原脚本恢复 UI 扩展 5.0.2，但构建时只指定扩展名，会从缓存选取版本。现在恢复与构建均显式使用清单中的同一精确版本。
- WiX [5.0.2 加载器](https://github.com/wixtoolset/wix/blob/v5.0.2/src/wix/WixToolset.Core/ExtensibilityServices/ExtensionManager.cs) 查找 `wixext5`；[7.0.0 加载器](https://github.com/wixtoolset/wix/blob/v7.0.0/src/wix/WixToolset.Core/ExtensibilityServices/ExtensionManager.cs) 查找 `wixext7`。现有扩展不能视为已兼容 WiX 7。当前日志先被许可检查阻断，不能声称已经运行并观察到扩展 ABI 错误。

## 许可与成本

WiX 源码采用 [MS-RL](https://github.com/wixtoolset/wix/blob/v7.0.0/LICENSE.TXT)。官方说明，使用 WiX 生成安装包本身通常不会把应用变成 MS-RL 衍生作品；FluentControl 自有代码的 MIT 许可无需因此更改，第三方文件仍遵守原许可。

[WiX 7.0.0 OSMF EULA 原文](https://github.com/wixtoolset/wix/blob/v7.0.0/OSMFEULA.txt) 是官方预编译二进制的额外使用条款。第 1 条要求同时满足用于创收活动、使用者年度总收入至少 US$10,000 的使用者支付维护费；低于该金额以及另付维护/支持费的使用者有相应豁免。费用按项目支付安排执行，EULA 没有固定美元报价。不能仅凭本项目免费/MIT 或开发者工资推定适用性，也不能把无需付费当作无需同意协议。

[OSMF 官方说明](https://docs.firegiant.com/wix/osmf/) 指出条款从 v6 引入，v7 开始强制明确接受。未获所有者明确许可前，不添加自动接受参数、命令或环境配置，不通过改用 v6 回避决定。源码自行编译的权利在 EULA 第 4 条保留，但这引入构建维护与来源验证成本，本次不采用。

| 方案 | 实际收益 | 成本及风险 |
| --- | --- | --- |
| 暂留 5.0.2 | 保持现有当前用户安装、中文向导、稳定组件和升级逻辑；无新 OSMF 接受动作 | WiX 5 社区安全更新已于 2026-02-05 结束；这是短期稳定方案，不能宣称长期安全维护仍有效 |
| 迁移 7.0.0 | 当前主版本、较新修复、长路径等增强 | 必须确认 EULA 与费用适用性，同步升级扩展并验收向导、组件、升级、修复和卸载 |

依据：[官方生命周期](https://docs.firegiant.com/wix/) 与 [发布说明](https://docs.firegiant.com/wix/whatsnew/releasenotes/)。WiX 7 的双用途 bundle、Burn 安全加固和 WixUI_Advanced 改进主要不适用于本项目的单 MSI、自定义 UI；当前脚本逐文件生成绝对 Source 路径，不使用 Heat 或 Files 通配采集。此为按现有代码作出的适用性判断，不代表 WiX 7 没有维护价值或无需评估安全公告。

## 本次约束与后续验收

- `.config/dotnet-tools.json` 仍精确固定 5.0.2。构建脚本在恢复工具、签名和产生输出前拒绝非稳定 5.x 版本。
- Dependabot 对 `/.config` 保留补丁更新 PR，忽略 WiX 主版本更新；未来迁移由维护者主动发起。不要因版本固定而忽略安全公告。
- 不修改 installer 定义、UpgradeCode、组件身份、安装目录策略、个人配置或 v0.3.92 标签/附件。
- `tests/installer/Test-WixPolicy.ps1` 验证不受支持版本在执行工具前退出，并接入 Windows CI。
- Windows CI 原有 `Test-Msi.ps1` 实际覆盖安装、旧版本 fixture 升级、静默操作、向导、启动项、文件占用、修复、卸载和用户配置保留。它使用 0.0.1 测试 fixture，不能等同于从正式 v0.3.92 安装包升级的专项测试。
- Linux 静态检查不等于 Windows 验收。以修复 PR 对应的 Windows Actions 结果为准；真实 Windows 桌面手工体验与正式 v0.3.92 到新版本升级仍需单独记录。正式包只可只读下载作基线，不能覆盖或修改发布资产。

迁移 WiX 7 前由所有者确认 EULA 及费用安排，再成套更新工具、UI 扩展和版本策略，跑完整 Windows 回归以及正式旧版升级测试。届时另行修改 Dependabot 策略；本次没有代替所有者作出许可决定。
