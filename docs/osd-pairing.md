# 本地 OSD 配对 / Local OSD pairing

此实验性功能从 v0.3.75 起提供，不代表所有显示器已经支持原厂菜单导航。整个流程可离线完成，无需登录、上传或网站服务。

## 在第三台显示器上开始

1. 选择目标显示器，在「型号适配 → OSD 配对 / 遥控」或「显示器菜单」分类打开配对窗口。窗口会读取并保存诊断基线。
2. 在「基础测试」点击「启用 OSD」，观察原厂菜单是否打开。展开「绑定观察到的动作」，勾选实际发生的「打开」「启用 OSD」，然后点击「成功」。未发生预期效果则选择「失败」或「其他效果」，不要只根据发送成功判断。
3. 对 XWU-CBA / HWV62F5，已知用户报告过启用后 `0xCA` 读回失败。可以明确勾选「读回失败时，允许沿用本次会话已读取的按键字段」，再测试禁用/恢复。该选项只使用当前窗口已读到的高字节，不读取磁盘旧状态，也不会把失败结果中的零值当作真实状态。
4. 「禁用 OSD，保留按键事件」并不表示物理按键仍能操纵原厂菜单。「禁用 OSD 与按键事件」只在 MCCS 2.2 或能力串明确声明该值时开放，每次执行需要第二次点击确认。
5. 点击「测试已声明的未知指令（数量）」进入「候选指令」，确认原厂菜单可以打开。程序会自动列出当前显示器声明支持、但用途未知的代码；无需填写代码和来源。选中代码后，点击具体值的「试发」按钮，每次只发送一次。声明提供了参数列表时只列出这些值；只有代码时提供明确标为试验值的 `0x0000–0x0005`，也可以输入其他十六进制值。试验值不是显示器声明的取值、也不是预设方向键，可能改变未知设置。发送后按实际效果绑定，并记录菜单层级、结果和备注。原有的已安装适配、本地导入和手工候选仍可使用。
6. 「遥控」提供上、下、返回、确认四个主要按钮和其他已绑定动作。摇杆左/右可以绑定成返回/确认。未验证的按钮不可用，部分配对也可保存。
7. 记录自动保存在本机。「记录与文件」可补充连接方式、HDR、环境说明，手动保存版本或导出 JSON。重新配对会在同一目录保留 archive 文件，再清除活动绑定与试验历史，保留候选。

「其余已声明功能及分类」会解释已知标准功能为何没有进入未知指令列表。重置、自动调整、频率、版本、按键通知等不会生成未知按键试发按钮；没有未知代码或能力读取失败时明确说明原因，不伪造候选。自动候选来自本次诊断中的声明，不按厂商名称猜代码，也不根据读数或最大值生成参数。

没有已验证的 HWV/HKC 导航写入码随本次功能内置。`0x03` 的方向事件编号不能直接作为发送指令。开关配对成功不代表上下/确认也可用；可以先完成已知开关测试，再研究有依据的导航候选。

## 当前开发版新增

确认某次试验成功或产生其他可用效果后，可在「保存为自定义控件」填写名称：选“按钮”保存单个操作，选“选项组”则填写选项名称；同一组名可继续加入其他已验证的指令。这些控件出现在「遥控」页。选项选择后立即清空，表示待发送命令，不冒充显示器当前状态。没有生成未知连续值滑块：单个读数或最大值不足以确认整个范围可以写入。

「候选指令 → 通信兼容设置」可设发送后等待时间，或关闭写后回读。关闭回读仍需用户观察确认，并保留 `0xCA` 的写前按键字段保护。每次试验保存当时的等待/回读策略。确认某命令失败会撤销该命令的方向键绑定和自定义控件；重新配对也会清除这些绑定。导入不激活自定义控件，也不覆盖本机兼容设置。

「记录与文件」增加用户手动填写的视频接口 `videoConnection`，与连接说明/HDR/环境信息一起保存，便于同屏 HDMI / DP 比较；不能据此自动判断固件支持。USB/HID 仍为[研究方案](usb-hid-adaptation.md)。

## 文件与证据

本地目录：`%LOCALAPPDATA%\FluentControl\OsdPairings\`。每个连接单独保存；文件名是型号/固件/能力指纹与本机连接身份的哈希，公开内容不含设备实例或自动采集的连接路径。

新格式：`format: fluentcontrol.osd-pairing`、`version: 3`（当前开发版，可读取旧版 v1/v2；下次保存写为 v3），独立于场景配置和已审核适配包。主要字段：

| 字段 | 含义 |
| --- | --- |
| `id / revision / createdAt / updatedAt / simulated` | 配对标识、用户保存版本、时间与模拟标记 |
| `identity` | 型号 ID、型号名称、制造商、固件、MCCS、规范化能力指纹 |
| `sessions` | 应用/系统版本、分辨率、用户填写的连接/HDR/环境信息、完整诊断基线 |
| `commands` | 候选名称、VCP 代码、值、来源、来源型号和导入来源；`discovery` 区分 `manual` / `capability-option` / `capability-experiment`，`declaredValues` 保留声明值，`advertisementCapturedAt` 记录能力采集时间 |
| `trials` | 时间、会话、指令快照、菜单上下文、实际发送值、传输结果、原生错误、耗时、前后读回、是否使用当前会话字段、用户结果/动作/备注/确认时间 |
| `compatibility` | 传输固定 `ddcci`；发送后等待 0–2000ms（默认 100）；`readback` 为 `once` 或 `none`。没有自动重发开关 |
| `customControls` | 用户命名的按钮或选项组；每个选项引用本机已确认成功/其他效果的试验和指令 |
| `bindings` | 逻辑动作、候选和本机观察记录的引用、适用菜单上下文 |
| `eventCaptures` | 有标签、有时间戳的 `0x02 / 0x52 / 0x03` 读取记录与取消状态 |

文件限 4 MiB；最多 32 个自定义控件（每组最多 32 个选项）、512 条候选、512 次试验、64 个会话、32 次事件采集，每次至多 96 个读数。会话已满时先本地归档，再开启新的配对历史。试验已满时仍可导出和重新配对。

默认导出不包含原始能力串；只有显式勾选才包含当前仍可用的原始串，其中可能有厂商设备标识。用户输入的备注按原文导出。

导入精确比较型号 ID、固件、MCCS 和能力指纹；不匹配或损坏文件不接受。导入保留候选、来源和历史证据，但不激活其中的遥控绑定。相同型号的另一台屏幕也应单独确认实际效果。导入或打开窗口不自动写入显示器。

## 事件与执行边界

按键事件采集仅在用户点击后运行，最长 5 秒，逐项顺序读取，对连续失败的代码停止读取；不会写入确认值。读取 `0x03` 可能消费事件，原厂菜单也可能自行处理按键，未读到事件不是不支持按键的证明。`0x02` 与 `0x52` 是通知/变化信息，不是方向键注入接口。

配对操作与刷新/释放句柄共用生命周期门控。窗口关闭或设备刷新会取消未开始的请求；在途通信正常完成或所属通信进程被中止后才释放门控；主程序不在原生调用执行期间销毁物理句柄，并保留已完成发送的记录。配对期间暂停场景和桌面调节。

## English

Use **Model adapters → OSD pairing / remote** or the monitor-menu section. The assistant captures a baseline locally, sends only explicit user requests and keeps transport results separate from observed effects. Confirm the actual response before binding open, close, up, down, back, confirm or additional actions. Partial pairings work offline.

There are three initial OSD commands. Disabling both OSD and button events requires a separate confirmation and MCCS 2.2 or explicit advertised support. If OSD readback fails after opening the menu, an opt-in option can reuse button fields successfully read in the current window. Saved historical values are never used for this recovery path.

The **Test advertised unknown commands (count)** entry automatically lists unknown codes from the current monitor’s valid capability declaration, including undefined codes below E0. Select a code and click a value to send once, then bind the observed effect. Advertised value lists are used as given. Codes with no value list offer explicitly labelled experimental values 0–5 and an optional custom hex value, without requiring the code or source to be typed. Experimental values may change unknown settings and are not claimed to be direction keys. Known standard, reset and information features are classified separately; an empty list is explained. Installed model adapters, matching local files and explicit manual candidates remain available. No universal HWV/HKC navigation codes are bundled. Read-only button events are evidence, not host-to-monitor navigation commands.

The current development version writes v3 files with named buttons/choice groups, bounded settling delays, optional readback and a manually recorded video connection. Each named option requires local observed evidence; imports do not activate controls or overwrite local communication settings. Choice selectors clear after sending and do not claim to show hardware state. Versions 1 and 2 remain readable.

Version 2 pairing files distinguish advertised options from experimental values and retain the declaration timestamp and value list. Version 1 files remain readable. Automatic candidates are checked against the current session’s capabilities before testing or replay; imports alone never authorize them. Files contain model/firmware/capability identity, session metadata, diagnostic baselines, candidates, trials, raw read/write results, observations, bindings and bounded event captures. Files are saved per connected display in `%LOCALAPPDATA%\FluentControl\OsdPairings\`. Re-pairing archives the existing record locally and starts a new active history with the same candidates.

Import requires exact model, firmware, MCCS and capability matching. Imported records never activate remote bindings without local testing. Export is optional and does not upload anything. Raw capability strings require explicit opt-in; device instance paths are excluded. No account or website is required.

标准功能分类参考：[ddcutil VCP feature catalog](https://www.ddcutil.com/vcpinfo_output/)。分类不代表具体型号已实现该功能；未知代码也不代表存在导航用途。
