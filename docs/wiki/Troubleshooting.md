# 常见问题 · Troubleshooting

[返回目录 / Home](Home.md)

## 中文

| 现象 | 先检查什么 |
| --- | --- |
| 亮度、对比度都调不了 | 实体菜单的 DDC/CI、当前 HDR/节能/画面模式；关闭 HDR 后重新读取，再试一个项目 |
| 一台能调，另一台不能 | 分别记录型号、固件、连接和模式；有相同菜单或同品牌不等于远程能力相同 |
| 列表缺少控制项 | 等待高级读取完成，再尝试重新检测；没有通道可能是未声明、读取失败或当前模式受限 |
| 写入后没有变化 | 查看真实读回值与错误，不只看发送结果；用实体 OSD 核实 |
| 设备读取长时间等待 | 等待超时处理后刷新，检查日志；可比较直连与扩展坞/KVM 连接，记录其他变量不变的结果 |
| 有显示器音频，却没有屏幕音量控制 | 音频端点不代表 DDC 音量支持；用系统“音量”控制 |

### 开启 HDR 后调不了

HDR 可能由显示器接管亮度、对比度和色温。FC 会显示提示；可以关闭 HDR 后重新读取再试。若从 Windows 改了 HDR，也需要刷新 FC。不能保证每个型号都能在 HDR 下开放这些项目。[说明](HDR-and-Color-Temperature.md)。

### 色温 / 场景模式跳回，或者写读不一致

新版把待发送选项与实际读回值分开，下拉框完成后清空以便再次发送。这不代表自动恢复了标准模式。若提示“请求 6500、实际 9300”，请记录原始码、实体菜单名称和画面效果；不要把不同编码直接互换。读回失败显示未确认，不保存旧值冒充新状态。

### 原厂菜单能打开，但上下 / 确认无效

打开与导航是不同能力。只使用[本地配对](OSD-Pairing.md)中实际确认过的绑定，不能把读取到的按键事件当成可发送的方向命令。FC 软件菜单则直接使用已有可控参数。

### 软件色温不可用，或退出后仍然偏色

先检查 HDR、自动颜色管理和复制显示模式。普通关窗默认只是隐藏到托盘，效果会保留；需要恢复时点击“恢复原颜色”。强制结束、系统关机或驱动重置可能阻止退出恢复，下次打开按提示手动恢复。若其他软件已经改写曲线，FC 会保留新设置；不要在运行中手工删除恢复文件。

### 窗口、桌面面板与安装

- 桌面面板先双击解锁；失焦或 Esc 会再次锁定。遇到 Win+D、前置或缩放异常，请注明 Windows 版本、屏幕分辨率、各屏缩放和操作顺序。
- 4K 或长型号名称显示异常，先核对版本，再附截图和缩放比例。已有 DPI 改进不等于每种混合多屏组合都已实测。
- 更新前从托盘退出旧版。安装器若列出文件占用应用，请保存相关工作并检查实际占用；FC 不会替你强制关闭无关程序。
- 主程序关闭不等于卸载；升级和卸载保留设置。不要为排查单个问题直接删除整个用户数据目录。
- 仍无法解决时，按[诊断反馈模板](Diagnostics-and-Compatibility.md)提交。

## English

For ineffective brightness or contrast, check physical-menu DDC/CI, HDR, power saving and picture mode; turn HDR off, refresh and test one control. Different monitors can behave differently even within one brand. Wait for advanced discovery or explicitly redetect missing controls. Check real readback and physical OSD results, not only transport success.

A cleared color/picture dropdown is ready for another command, not proof that the display reverted to Standard. Preserve requested/returned codes when reporting mismatches. Native-menu opening and navigation are separate; use only observed local bindings. Audio endpoints do not prove DDC hardware-volume support.

For software temperature, check HDR, auto color management and duplicated outputs. Closing the window usually keeps FC in the tray and preserves the effect. Use Restore original colors. Forced termination or driver changes may require explicit recovery on the next launch; do not delete an active recovery file. FC preserves externally changed curves.

Double-click the desktop panel to unlock it; blur or Esc locks it again. Report resolution, per-display scaling, Windows version and exact steps for Win+D, focus or clipping problems. Existing DPI improvements do not certify every physical monitor arrangement.

Exit from the tray before updating. If the installer reports files in use, save work and investigate those applications; FC does not forcibly close unrelated programs. Upgrades and uninstallation retain settings. Avoid deleting the whole data folder for a single issue. Use the [diagnostic checklist](Diagnostics-and-Compatibility.md) when reporting unresolved problems.
