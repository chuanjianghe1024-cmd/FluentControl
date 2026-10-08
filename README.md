# FluentControl

Windows 聚合控制中心，使用 WinUI 3 / C# / .NET 8，遵循 Windows 11 Fluent Design。

## 控制面板

- 左侧导航：显示器、声音与麦克风、鼠标与指针；使用 WinUI 3 原生控件、Mica 和系统主题。
- 显示器按设备分组。首次发现时从左到右分配 M1、M2 等应用编号；编号不等同于 Windows 显示设置中的编号。
- 点击“识别显示器”会在各屏幕中央显示名称 4 秒。支持重命名为左屏、右屏等，名称保存到 `%LOCALAPPDATA%\FluentControl\monitor-names.json`，以显示设备接口 ID 匹配。更换接口或驱动后系统 ID 可能变化，需要重新命名。
- “单独控制”分别调节每台显示器；“一起控制”将支持的显示器设为相同百分比。亮度、对比度和显示器扬声器音量分别按支持情况提供。初始值不同时显示“不同”；部分写入失败会继续操作其他设备，并保留失败设备原值。
- 音频默认展示 Windows 默认输出、默认输入和默认通话设备；同一设备兼任多个角色只显示一行。其余活动设备（包括未选中的 Voicemeeter 端点）放入“其他音频设备”折叠面板。更改 Windows 默认设备后点击刷新。
- 鼠标速度 1–20 档；指针大小 1–15 档。指针大小依赖 Windows 的非公开 `SystemParametersInfo(0x2029)` 兼容接口，失败时恢复原配置并提示使用 Windows 设置入口。保留原指针主题和颜色，实际外观仍需按系统版本与指针主题验证。
- 滑块延迟 150ms 合并请求，后台串行执行；失败显示原因。支持音频静音、刷新和关闭时释放设备句柄。

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

Windows CI 包含控制逻辑与名称保存测试、可恢复的鼠标设置测试、发布资源检查、真实程序启动和模拟设备界面检查。只有全部通过后才上传程序包；结果以对应提交的 Actions 为准。DDC/CI、真实音频路由、多屏识别和自定义指针主题仍需在你的硬件验证。

如果启动失败，程序会尽可能弹出错误并写入 `%LOCALAPPDATA%\FluentControl\Logs\startup.log`。请提供此日志；如果没有生成日志，说明失败可能发生在托管应用初始化之前。下载后请先完整解压所有文件，再运行 `FluentControl.exe`。

1. 音量/麦克风滑块与 Windows 声音设置数值同步；静音切换正确。
2. 对照显示器 OSD 检查背光亮度；HDR、ECO 模式可能限制设置。
3. 快速拖动最终值到达设备，失败明确显示；点击刷新读取实际值。
4. 无麦克风、不支持 DDC/CI、拔插设备后刷新，不崩溃。
5. 多显示器与重名设备逐个核对；深浅主题与高 DPI 检查。

当前不支持笔记本内屏 WMI 亮度、实时外部状态同步、自动热插拔、输入源切换、托盘、快捷键、场景保存。输入源及其他离散属性后续使用选择控件，不强行显示为滑块。当前显示器百分比为 VCP 最大值归一化结果，部分 OSD 的标度可能不同。

## 验证

```powershell
dotnet run --project tests/FluentControl.Tests -c Release
./scripts/Test-Startup.ps1
./scripts/Test-Startup.ps1 -UiTest
```

`-UiTest` 会使用不操作硬件的模拟设备来检查所有面板；日常运行直接打开 exe。CI 在一次性 Windows runner 上额外使用 `--native` 验证鼠标读写并恢复原值。
