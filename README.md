# FluentControl

Windows 聚合控制中心，使用 WinUI 3 / C# / .NET 8，遵循 Windows 11 Fluent Design。

## 控制面板

- 显示器默认一起控制，可切换单独调节；保留 M1/M2、重命名和屏幕识别。
- 提示在 4 秒后消失，警告/错误保留 8 秒；切换页面立即清空提示。
- 音频只优先显示 Windows 默认设备和默认通话设备，其余活动设备折叠。
- 鼠标速度与指针大小、显示器亮度/对比度/扬声器音量；色温仅在显示器报告能力且能读取当前值时显示，共控色温仅包含各屏幕共同支持的选项。
- 设置页支持跟随系统及简体中文、繁體中文、English、日本語、한국어、Deutsch、Français、Español。标题栏、导航标题、托盘菜单和提示同步切换；中文应用名为“聚合控制”。硬件设备名和用户配置名保留原文。
- 配置支持保存、更新、删除和前后循环切换。保存显示器、默认音频和鼠标的当前控制值及静音状态；根据设备 ID 匹配，缺失设备跳过并提示，不切换 Windows 默认音频路由。启动时不会自动应用最后的配置，名称后的星号表示当前值可能与该配置不同。

## 托盘、自启动与快捷键

关闭按钮默认隐藏到托盘，可在设置中关闭该行为。托盘菜单可打开/隐藏主面板、切换配置、显示/隐藏桌面面板及彻底退出。托盘不可用时不会把主窗口藏到无法恢复的位置。

自启动默认关闭；启用后使用当前用户的 Windows Run 项，登录后以 `--background` 运行。移动程序文件夹后需重新启用，Windows 任务管理器也可以单独禁止此启动项。关闭自启动会删除 FluentControl 自己的 Run 项。

| 快捷键 | 动作 |
| --- | --- |
| Ctrl + Alt + Shift + Space | 打开面板 |
| Ctrl + Alt + Shift + ← | 上一个配置 |
| Ctrl + Alt + Shift + → | 下一个配置 |
| Ctrl + Alt + Shift + ↓ | 隐藏主面板 |

可整体禁用快捷键；无法注册的组合在设置中明确列出，不抢占其他应用。应用使用单实例机制，再次运行会唤起已有窗口。

## 桌面面板

设置中开启：真正的半透明背景、紧凑控制行、顶部配置名与左右切换箭头。背景不透明度可设 10%–85%，默认 30%，文字保持不透明。双击解锁，失去焦点或按 Esc 自动锁定，锁定时不能改变设备值。右上角拖动移动，右下角拖动缩放，移动和缩放不要求解锁；位置与尺寸都会保存。可恢复自动尺寸。底部提示独立排版，空间不足时仅控制行滚动。

可设置浅色/深色文字、显示器群控或单独控制、具体控制项与最多 1–16 行。不同控制组以空隙分隔。该面板是无任务栏按钮、不置顶的桌面浮动窗口，普通应用可以覆盖它；没有挂接到 Explorer 的非公开 WorkerW 桌面层。

## 显示器特殊功能

较暖色温需要显示器支持；Windows 夜间模式提供原生设置入口。软件准星可指定一台屏幕，使用透明、鼠标穿透的置顶窗口，适用于窗口或无边框应用，不保证出现在独占全屏游戏上。

本版不调用未知厂商 VCP 指令，不控制厂商游戏模式或硬件准星，也不提供游戏 FPS 测量。显示器刷新率不等于游戏 FPS。支持色温也不等于支持厂家护眼模式。

## 保存位置

`%LOCALAPPDATA%\FluentControl\user-state.json` 保存设置与配置；`monitor-names.json` 保存屏幕名称。设备接口 ID 随接口/驱动变化可能改变，需要重新命名或更新配置。鼠标指针大小采用非公开系统兼容接口 `SystemParametersInfo(0x2029)`，失败会恢复原值，保留 Windows 设置入口。

## 构建

Windows 11 x64，Visual Studio 2022 的 WinUI / Windows 应用开发组件、Windows SDK 及 .NET 8 SDK。

```powershell
dotnet build src/FluentControl/FluentControl.csproj -c Debug -p:Platform=x64
dotnet run --project src/FluentControl/FluentControl.csproj -p:Platform=x64
```

完整离线部署文件夹：

```powershell
dotnet publish src/FluentControl/FluentControl.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o publish
```

运行 publish 中的 FluentControl.exe，必须保留其余依赖文件。GitHub Actions 在 Windows 上构建并上传完整文件夹。

## 当前验证边界

Windows CI 包含控制逻辑与名称保存测试、可恢复的鼠标设置测试、发布资源检查、真实程序启动和模拟设备界面检查，以及提示消失、语言切换、配置读写、八种语言标题/菜单一致性、桌面面板锁定和快捷键注册检查；还通过真实鼠标拖动验证移动/缩放，并切换背景色采样屏幕像素验证半透明，检查最小尺寸下的提示布局。只有全部通过后才上传程序包；结果以对应提交的 Actions 为准。DDC/CI、真实音频路由、多屏识别和自定义指针主题仍需在你的硬件验证。

如果启动失败，程序会尽可能弹出错误并写入 `%LOCALAPPDATA%\FluentControl\Logs\startup.log`。请提供此日志；如果没有生成日志，说明失败可能发生在托管应用初始化之前。下载后请先完整解压所有文件，再运行 `FluentControl.exe`。

1. 音量/麦克风滑块与 Windows 声音设置数值同步；静音切换正确。
2. 对照显示器 OSD 检查背光亮度；HDR、ECO 模式可能限制设置。
3. 快速拖动最终值到达设备，失败明确显示；点击刷新读取实际值。
4. 无麦克风、不支持 DDC/CI、拔插设备后刷新，不崩溃。
5. 多显示器与重名设备逐个核对；深浅主题与高 DPI 检查。

当前不支持笔记本内屏 WMI 亮度、实时外部状态同步、自动热插拔、输入源切换。输入源及其他离散属性后续使用选择控件，不强行显示为滑块。当前显示器百分比为 VCP 最大值归一化结果，部分 OSD 的标度可能不同。

## 验证

```powershell
dotnet run --project tests/FluentControl.Tests -c Release
./scripts/Test-Startup.ps1
./scripts/Test-Startup.ps1 -UiTest
```

`-UiTest` 会使用不操作硬件的模拟设备来检查所有面板；日常运行直接打开 exe。CI 在一次性 Windows runner 上额外使用 `--native` 验证鼠标读写并恢复原值。
