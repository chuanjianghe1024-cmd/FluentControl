# WiX 工具链策略

2026-10-13，仓库所有者明确说明项目不用于创收，并授权接受 WiX 7 的 OSMF EULA、切换 WiX 7。此前 PR #4 的暂留 WiX 5 方案被本决定替代。

## 版本与许可

- `.config/dotnet-tools.json` 精确固定 WiX 7.0.0；`Build-Msi.ps1` 从同一清单读取版本，恢复和加载完全一致的 `WixToolset.UI.wixext`。避免主工具与扩展分开升级、或缓存选中其他版本。
- 工具恢复后执行 `dotnet tool run wix eula accept wix7`，成功后再恢复扩展、编译 MSI。这会在执行构建的用户/计算机上记录 WiX 7 EULA 的接受状态。
- 脚本仅支持稳定的 7.x 版本，在运行工具、签名或产生输出前拒绝其他主版本和预发布版本。此授权不延伸到未来新协议；Dependabot 保留补丁更新 PR，未来主版本由维护者单独审查。
- [WiX 7.0.0 OSMF EULA](https://github.com/wixtoolset/wix/blob/v7.0.0/OSMFEULA.txt) 与 [MS-RL 源码许可证](https://github.com/wixtoolset/wix/blob/v7.0.0/LICENSE.TXT) 分开。此次接受协议未开通赞助、订阅或付款。将来用于创收时，维护者须重新评估费用适用性。
- 外部贡献者运行构建脚本前应阅读上述协议和接受行为；仓库所有者的决定不代表替其他组织作出法律承诺。

## 升级原因与兼容性

[PR #3](https://github.com/chuanjianghe1024-cmd/FluentControl/pull/3) 原始版本仅升级工具，曾在 [run 38269140092](https://github.com/chuanjianghe1024-cmd/FluentControl/actions/runs/38269140092) 因 WIX7015 失败，安装回归未执行。本修复同步升级扩展并明确接受已授权的协议。

WiX 5 的社区安全更新已于 2026-02-05 结束；WiX 7 提供较新维护和长路径等改进。见 [生命周期](https://docs.firegiant.com/wix/) 和 [发布说明](https://docs.firegiant.com/wix/whatsnew/releasenotes/)。当前项目不使用 Burn、Heat 或 Files 通配采集；继续使用单 MSI 和自定义中文向导。

WiX 5/7 加载器分别查找 `wixext5` / `wixext7`，不能保留旧扩展。UI 对话框、路径验证自定义动作和 MSI 升级行为必须由 Windows 回归验证，不能仅凭应用编译成功判断兼容。

## 验收边界

`tests/installer/Test-WixPolicy.ps1` 验证版本边界；Windows CI 继续检查应用编译、发布、启动、MSI 生成以及 `Test-Msi.ps1` 的安装、旧版本 fixture 升级、向导、修复、文件占用取消、卸载和个人配置保留。

除 0.0.1 fixture 回归外，测试只读下载发布清单指定的正式 MSI，验证大小及 SHA-256 后执行正式旧版安装→新版本升级→卸载，检查目录、启动项、配置保留、全部新 payload 哈希和旧包未被修改。以本 PR 最新 Actions 结果为准，不能套用旧 WiX 5 的通过记录。保留当前用户安装、稳定 UpgradeCode、组件身份和数据目录；不修改已发布 v0.3.92 标签或附件。

## 已定位的 WiX 7 UI 差异

首轮 Windows 回归确认：静默安装/升级/卸载成功，但真实向导停在目录浏览窗口。WiX 7 的 BrowseDlg 不再内置 OK 的 SetTargetPath/EndDialog 事件，而由宿主 UI 集合定义；本项目自定义 UI 已显式补上路径检查、目录提交和关闭对话框事件。保留既有向导回归，不用跳过 UI 测试来掩盖问题。参考 [WiX 7 标准 UI 接线](https://github.com/wixtoolset/wix/blob/v7.0.0/src/ext/UI/wixlib/WixUI_InstallDir.wxs)。
