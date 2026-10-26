# FCS / FC P0 验收清单（开发阶段）

> 本清单记录代码基线与待验证项，不代表已发布或实体设备验收通过。

| P0 项目 | 现有代码基础 | 本轮变更或待验收 |
| FCS 独立发行与 FC 客户端边界 | Gateway IPC 与 FC 客户端已有初步实现，但 Gateway 工程仍在 FC 仓库 | 将 Gateway/维护测试迁入 FCS；FCS 独立 SemVer/CI/便携包；FC 默认捆绑固定兼容版本；FC 生产路径只经 FCS API。当前尚未完成，首个 FCS Release 不得提前发布 |
| --- | --- | --- |
| Gateway 设备自动发现与恢复 | LAN 广播发现；USB PING；sourceId 合并与采样兜底 | Gateway 自主 USB 热插拔扫描/30 秒重试；USB 管理回报真实 COM 口。待 C3 拔插、Wi-Fi IP 变化与睡眠唤醒实机测试 |
| lux → 自动亮度 | 订阅、曲线、平滑过渡、最短间隔、故障暂停 | 待 20 分钟稳定取样、断线 10 秒恢复与实体显示器回读 |
| 多显示器与聚合控制 | 按目标屏幕逐个安全写入，支持已发现属性 | 待双 HKC、HDR 混合状态、一个显示器掉线的真实验收 |
| HDR 安全提示 | HDR 状态独立检测、写入前限制、面板提示 | 待开关 HDR、显示器 DDC 被锁时的恢复流程 |
| 托盘与浮窗同步 | 共享自动调节状态与 lux 显示 | 待主窗关闭/隐藏、跨显示器分组、托盘明暗模式现场测试 |
| 配置与恢复 | 自动模式配置持久化、已有光源 ID、超时保护 | 待升级、重启、休眠后不误启自动控制，不覆写手动设置 |

## P0 交付门槛

1. 插入受支持 USB 设备后，无需打开 FCS 页面也能在 Gateway 的 `list` 看到同一稳定设备 ID；旧 COM 消失和新 COM 出现不会产生两条身份。
2. 同一设备通过 USB 与局域网可读时不重复添加，断开 USB 后在已发现且可达的 LAN 上恢复光照；断开全部数据路径，亮度不继续按过期 lux 写入。
3. 自动亮度仅在用户明确开启且显示器允许写入时生效；HDR/未知状态、写入失败以及手动调整都不强行覆盖。
4. 托盘、浮窗和设置页对自动模式、来源有效性与 HDR 限制显示一致；配置重启后恢复但不绕过安全保护。
5. FCS 独立发行门槛也必须通过：Gateway 源码与维护测试归 FCS 仓库、FCS 独立版本/构建产物可用、FC 安装包捆绑固定兼容版本并只经 API/IPC 操作设备。不要把当前 FC 仓库生成的 Gateway Artifact 当作独立 FCS Release。
6. CI（逻辑/Windows/安装）以及两台 HKC 与第三块 C3 的**实际实机验收**都完成后，才讨论首个正式发布。不要把自动化模拟测试等同于硬件验收。

## 验证命令

```powershell
dotnet run --project src/FluentControl.FcsGateway/FluentControl.FcsGateway.csproj -c Release -- --self-test
dotnet run --project tests/FluentControl.Tests -c Release
dotnet build src/FluentControl/FluentControl.csproj -c Release -p:Platform=x64
```

不因本清单创建新 Release。
