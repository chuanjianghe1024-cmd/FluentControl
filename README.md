# FluentControl

Windows 聚合控制中心，使用 WinUI 3 / C# / .NET 8，遵循 Windows 11 Fluent Design。

## 第一版

- Mica 背景、系统深浅主题、强调色、原生滑块与入场动画。
- 每个设备属性占一行：图标、名称、横向滑块、百分比、静音按钮。
- 枚举活动声音输出和麦克风，调整设备音量并切换静音（不是录音监听，也不是每个应用的音量）。
- 外接显示器 DDC/CI：亮度、对比度、显示器扬声器音量。只有读取成功的属性才显示。
- 滑块延迟 150ms 合并请求，后台串行执行，避免阻塞界面。
- 刷新重新枚举与读取设备；关闭释放显示器句柄和音频设备。

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

源代码由 Linux 环境生成，已做 XML、结构检查，尚未完成 Windows 编译和硬件实测。仓库创建后需查看 Windows CI，构建通过后再进行下列实测。不要把这版称为已测试发行版。

1. 音量/麦克风滑块与 Windows 声音设置数值同步；静音切换正确。
2. 对照显示器 OSD 检查背光亮度；HDR、ECO 模式可能限制设置。
3. 快速拖动最终值到达设备，失败明确显示；点击刷新读取实际值。
4. 无麦克风、不支持 DDC/CI、拔插设备后刷新，不崩溃。
5. 多显示器与重名设备逐个核对；深浅主题与高 DPI 检查。

当前不支持笔记本内屏 WMI 亮度、实时外部状态同步、自动热插拔、输入源切换、托盘、快捷键、场景保存。输入源及其他离散属性后续使用选择控件，不强行显示为滑块。当前显示器百分比为 VCP 最大值归一化结果，部分 OSD 的标度可能不同。

## 创建仓库并推送

若已安装 GitHub CLI 且登录：

```powershell
git init -b main
git add .
git commit -m "Initial WinUI aggregate control implementation"
gh repo create FluentControl --private --source=. --remote=origin --push
```

默认先私有，确认构建与接口行为后再决定公开和许可证。
