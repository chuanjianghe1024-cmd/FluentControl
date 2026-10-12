# 系统色温（软件） / Software color temperature

开发版在每屏卡片与整体视图中增加独立的「系统色温（软件）」折叠项。选择 2500–10000 K 后点击应用，软件通过当前屏幕的显卡颜色曲线调整冷暖，不发送 DDC 色温命令。内屏和外屏都按 Windows / 显卡接口实际可用性判断。

这是 **SDR 兼容功能**，不是校色工具。数值是软件效果的近似目标，不是仪器测得的面板色温；6500 K 完整保留开始控制时的曲线，不能据此声称把原本偏色的面板校准到了 D65。缩减 RGB 通道可能影响亮度与色阶。照片、印刷和需要准确颜色的工作应恢复原色并使用合适的校色流程。

## 使用

1. 在单屏卡片或整体控制的对应屏幕下展开系统色温，选择目标后点击「应用」。仅移动滑条不会写入。
2. 「恢复原颜色」恢复本次接管前捕获的曲线，保留其中已有的校色；不写入一个假定的线性出厂曲线。
3. 隐藏主窗口到托盘会继续保留效果。正常退出时尝试恢复仍归 FC 控制的曲线；强制结束、崩溃、系统关机或驱动重置不能保证执行恢复。下次启动会提供未完成恢复的提示，不自动应用旧色温或恢复记录。
4. 本机总配置可保存已确认的目标，`0` 表示 FC 未接管。应用总配置才执行；启动和导入不执行。软件效果不属于显示器型号参数，因此不进入型号预设、公共分享或 DDC 诊断。
5. HDR、自动颜色管理（ACM/WCG）、复制屏幕共用输出或状态不明时，禁用该控制。可以打开相邻的 Windows 夜间模式入口，实际可用性由 Windows 决定。

请避免同时使用 Windows 夜间模式、f.lux 或校色加载器。FC 在操作前和持有会话期间每 5 秒只读核对曲线；如果其他程序或显示事件改变曲线，停止接管并保留新的颜色，不持续抢写。切换 FC 的 HDR 开关前先恢复自己的软件曲线，关掉 HDR 后不自动重放色温。

## 实现与状态

- `Services/SystemColor.cs`：精确设备身份、SDR 模式检查、曲线变换、读回、恢复和日志；`MainWindow.SystemColor.cs`：按屏界面、生命周期和本机总配置。
- 复用 CCD 目标身份及 `GET_SOURCE_NAME`。写前重新解析接口路径和 GDI 显示源；复制模式不允许按单屏写曲线。令牌含目标和显示源，不能把旧曲线恢复到复用的输出名称。
- 在捕获的 3 × 256 WORD 原曲线上叠加近似白平衡。使用代表性 RGB 波长的普朗克辐射比、6500 K 相对基准及近似编码增益；每次从原曲线计算，避免连续调节累积偏色。保持单调和有限范围，拒绝无效/全黑原曲线。
- `SetDeviceGammaRamp` 属于兼容 API。微软明确指出它可能返回成功但忽略设置，在 HDR 下行为未定义，并与校色存在交互限制。因此读取成功只允许用户尝试，不能宣称写入一定支持；调用返回后再次读回并核对，最多容许一个 8-bit 量化级的差异。回读只能证明驱动曲线值，不能证明肉眼画面或所有游戏渲染路径的效果。
- 写前保存本机 `%LOCALAPPDATA%\FluentControl\color-recovery.json`，包含目标、原曲线、最近确认曲线和待写曲线；原子替换，读取限 1 MiB / 32 项，损坏文件保留并禁用功能。恢复前核对当前曲线归属，遇到其他程序的新曲线不覆盖。不可达或 HDR 状态保留待恢复信息。
- `display-color/<设备路径编码>/temperature` 为本机总配置独立键；不混入已有 `temperature` / `color-preset` 硬件键。暂不可确认的值不捕获，更新时保留已有值。偏好文件只保存滑条目标，不等于开机自动应用。
- 所有读写、刷新、HDR 切换和正常退出共用 `gate` / `generation`。原生 DC 每次创建并释放；无后台写入重试。退出先失效旧回调，在关闭窗口前等待恢复。

ICC/MHC2 是 Windows 的现代颜色管理路径，可覆盖 SDR/HDR。本版没有生成、安装或切换 ICC 文件，也没有接入私有夜间模式注册表接口。后续接入 MHC2 时应保留现有校色、实际色域/亮度元数据及用户默认配置，不用猜测的 HDR 元数据替换它们。

## 验证

逻辑测试覆盖双屏隔离、6500 K 原曲线、暖冷方向、无效曲线、外部覆盖、HDR/ACM 禁写、输出身份变化、返回成功却忽略写入、异常恢复与只读发现。WinUI 回归使用模拟曲线，验证两种视图、选择不自动写入、保存边界、刷新、HDR 前恢复及恢复按钮。

实体 HKC、笔记本 GPU、游戏全屏、夜间模式/ICC 加载器冲突、睡眠和热插拔仍需本机验证；CI 不修改测试主机的原生 gamma。

## English

Development builds add **System temperature (software)** for each display in both views. Choose 2500–10000 K and click Apply; moving the slider alone makes no change. This is an approximate SDR white-balance effect through the graphics driver, independent of DDC hardware presets. 6500 K preserves the captured curve rather than calibrating a panel to a measured white point.

Restore original colors returns to the captured baseline only while the current curve still belongs to FC. The tray keeps the effect active; normal exit attempts restoration before closing. Forced termination and display-driver changes can prevent restoration, so a durable local journal offers explicit recovery on the next launch. FC does not automatically apply a saved temperature at startup.

HDR, auto color management, duplicated outputs and unknown states block legacy gamma writes. Windows night light is offered as an alternative entry point. Do not combine competing color tools. External curve changes release control instead of triggering repeated writes. Enabling HDR through FC restores its software curve first; disabling HDR does not replay it.

Confirmed effects can be saved in local global profiles, independently of hardware color temperature. They are excluded from model presets and public exports. Windows can ignore a gamma write even when the API returns success, so FC checks readback and keeps uncertain state explicit. Physical display behavior still requires local verification. ICC/MHC2 management is not implemented.

## References

- [Microsoft: SetDeviceGammaRamp limitations](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp)
- [Microsoft: Windows hardware display color calibration pipeline](https://learn.microsoft.com/en-us/windows/win32/wcs/display-calibration-mhc)
