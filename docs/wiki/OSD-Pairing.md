# OSD 菜单与本地配对 · OSD menus and local pairing

[返回目录 / Home](Home.md)

## 中文

### 先分清三种能力

| 名称 | 含义 |
| --- | --- |
| FC 屏幕菜单 | FC 自己绘制的窗口，用现有可控参数调节当前屏幕；Esc 关闭 |
| 原厂 OSD 启用 / 禁用 | 显示器菜单或按键状态；有的固件会同时弹出菜单，有的不会 |
| 原厂 OSD 导航 | 打开、上下、返回、确认等远程操作，需要该型号的协议或本机已验证绑定 |

摇杆或实体按钮的存在不能证明 DDC 能模拟这些动作。能打开原厂菜单，也不能推断剩余方向键已可用。

### 本地配对步骤

1. 选择对应屏幕，打开 **型号适配 → OSD 配对 / 遥控**。每台屏幕可以独立完成，不必先上传诊断或登录。
2. 从 OSD 启用、禁用但保留按键等基础操作开始。只有你观察到实际效果并确认，才成为有效的试验记录；禁用 OSD 与按键事件保留二次确认。
3. 在 **测试已声明的未知指令** 中选择候选和值，逐次点击试发。声明了值就提供声明值；只有代码时，0–5 等按钮明确标为试验值，也可手工填写。
4. 根据实体菜单的变化绑定打开、关闭、上、下、返回、确认等动作。摇杆左 / 右按真实效果映射，不按外观猜测。
5. 已确认的其他效果可以保存为命名按钮或选项组，在“遥控”页使用。允许只配对一部分，未验证的操作不开放。

未知指令可能改变其他设置，不一定是方向键。不要自动扫描或连续盲试；“写入成功”和“菜单动作成功”分别记录，显示器没有反应就确认失败或未观察到效果。

### 菜单打开后读取失败怎么办

“通信兼容设置”支持发送后等待 0–2000 ms，并可关闭写后回读。仍需观察实际效果，指令不会自动重发。

对于 OSD 按键字段，配对窗口可由你明确选择沿用当前会话已经成功读取的内容；不使用磁盘旧状态，也不从另一台屏幕继承。关闭窗口后该会话信息失效。

### 保存与分享

记录包含型号、固件、能力声明、代码和值的来源、发送与回读结果、观察、绑定和通信设置；可手工填写视频连接及 HDR 环境。v3 配对文件兼容读取 v1/v2，重新配对会归档旧记录。

导入提供可参考的试验记录，**不会直接激活另一台屏幕的遥控绑定**，需要在当前设备重新验证。同型号的两台屏幕也不自动共享本机绑定。原始能力串可选导出，分享前请检查备注和标识信息。

本版没有通用 HKC/HWV 原厂导航包，也没有 USB/HID 执行通道。[详细协议与边界](https://github.com/chuanjianghe1024-cmd/FluentControl/blob/main/docs/osd-pairing.md)。

## English

The **FC on-screen menu** is an application window using available monitor controls. **Native OSD enablement** changes firmware menu/button state and may or may not open the menu. **Native navigation** requires model-specific commands or locally verified bindings. A physical joystick or a menu that opens does not prove remote navigation support.

Open **Model adapters → OSD pairing / remote** for one display. Test the basic enable/disable actions and confirm what actually happened. Then use **Test advertised unknown commands** to send a candidate once. Declared options remain distinct from experimental values such as 0–5; unknown commands are not guaranteed direction keys and may change other settings.

Bind observed effects to open, close, up, down, back or confirm. Save other confirmed effects as named buttons or choice groups. Partial pairing is supported, while unverified actions remain unavailable. Transport success and visible menu success are recorded separately; there is no automatic command scan or write replay.

Communication settings provide 0–2000 ms settling and optional post-write readback. Visual confirmation is still required. For relevant button fields, the dialog can explicitly reuse data successfully read in the current session; old disk state and another monitor's fields are never substituted.

Pairing works offline. Records include device capabilities, model/firmware, candidate provenance, transmission/readback results, observations, bindings and communication settings. Video connection and HDR context can be entered manually. Version 3 reads v1/v2; re-pairing archives the previous record. Imports provide evidence rather than active bindings, so each current display must be verified again. No universal HKC/HWV navigation package or USB/HID control is included.
