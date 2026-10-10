# 本地 OSD 配对 / Local OSD pairing

此功能用于测试构建，不代表所有显示器已经支持原厂菜单导航。整个流程可离线完成，无需登录、上传或网站服务。

## 在第三台显示器上开始

1. 选择目标显示器，在「型号适配 → OSD 配对 / 遥控」或「显示器菜单」分类打开配对窗口。窗口会读取并保存诊断基线。
2. 在「基础测试」点击「启用 OSD」，观察原厂菜单是否打开。展开「绑定观察到的动作」，勾选实际发生的「打开」「启用 OSD」，然后点击「成功」。未发生预期效果则选择「失败」或「其他效果」，不要只根据发送成功判断。
3. 对 XWU-CBA / HWV62F5，已知用户报告过启用后 `0xCA` 读回失败。可以明确勾选「读回失败时，允许沿用本次会话已读取的按键字段」，再测试禁用/恢复。该选项只使用当前窗口已读到的高字节，不读取磁盘旧状态，也不会把失败结果中的零值当作真实状态。
4. 「禁用 OSD，保留按键事件」并不表示物理按键仍能操纵原厂菜单。「禁用 OSD 与按键事件」只在 MCCS 2.2 或能力串明确声明该值时开放，每次执行需要第二次点击确认。
5. 「候选指令」先确认原厂菜单可以打开。候选来自已安装型号适配、本地文件或用户手工填写的明确代码、值与来源；发送后记录所在菜单层级、结果、实际动作和备注。没有来源的私有码仅展示，不自动生成值或扫描。
6. 「遥控」提供上、下、返回、确认四个主要按钮和其他已绑定动作。摇杆左/右可以绑定成返回/确认。未验证的按钮不可用，部分配对也可保存。
7. 记录自动保存在本机。「记录与文件」可补充连接方式、HDR、环境说明，手动保存版本或导出 JSON。重新配对会在同一目录保留 archive 文件，再清除活动绑定与试验历史，保留候选。

没有已验证的 HWV/HKC 导航写入码随本次功能内置。`0x03` 的方向事件编号不能直接作为发送指令。开关配对成功不代表上下/确认也可用；可以先完成已知开关测试，再研究有依据的导航候选。

## 文件与证据

本地目录：`%LOCALAPPDATA%\FluentControl\OsdPairings\`。每个连接单独保存；文件名是型号/固件/能力指纹与本机连接身份的哈希，公开内容不含设备实例或自动采集的连接路径。

新格式：`format: fluentcontrol.osd-pairing`、`version: 1`，独立于场景配置和已审核适配包。主要字段：

| 字段 | 含义 |
| --- | --- |
| `id / revision / createdAt / updatedAt / simulated` | 配对标识、用户保存版本、时间与模拟标记 |
| `identity` | 型号 ID、型号名称、制造商、固件、MCCS、规范化能力指纹 |
| `sessions` | 应用/系统版本、分辨率、用户填写的连接/HDR/环境信息、完整诊断基线 |
| `commands` | 候选名称、VCP 代码、值、来源、来源型号和导入来源 |
| `trials` | 时间、会话、指令快照、菜单上下文、实际发送值、传输结果、原生错误、耗时、前后读回、是否使用当前会话字段、用户结果/动作/备注/确认时间 |
| `bindings` | 逻辑动作、候选和本机观察记录的引用、适用菜单上下文 |
| `eventCaptures` | 有标签、有时间戳的 `0x02 / 0x52 / 0x03` 读取记录与取消状态 |

文件限 4 MiB；最多 64 条候选、512 次试验、64 个会话、32 次事件采集，每次至多 96 个读数。会话已满时先本地归档，再开启新的配对历史。试验已满时仍可导出和重新配对。

默认导出不包含原始能力串；只有显式勾选才包含当前仍可用的原始串，其中可能有厂商设备标识。用户输入的备注按原文导出。

导入精确比较型号 ID、固件、MCCS 和能力指纹；不匹配或损坏文件不接受。导入保留候选、来源和历史证据，但不激活其中的遥控绑定。相同型号的另一台屏幕也应单独确认实际效果。导入或打开窗口不自动写入显示器。

## 事件与执行边界

按键事件采集仅在用户点击后运行，最长 5 秒，逐项顺序读取，对连续失败的代码停止读取；不会写入确认值。读取 `0x03` 可能消费事件，原厂菜单也可能自行处理按键，未读到事件不是不支持按键的证明。`0x02` 与 `0x52` 是通知/变化信息，不是方向键注入接口。

配对操作与刷新/释放句柄共用生命周期门控。窗口关闭或设备刷新会取消未开始的请求；已在进行的原生调用完成后才释放门控，并保留已完成发送的记录。配对期间暂停场景和桌面调节。

## English

Use **Model adapters → OSD pairing / remote** or the monitor-menu section. The assistant captures a baseline locally, sends only explicit user requests and keeps transport results separate from observed effects. Confirm the actual response before binding open, close, up, down, back, confirm or additional actions. Partial pairings work offline.

There are three initial OSD commands. Disabling both OSD and button events requires a separate confirmation and MCCS 2.2 or explicit advertised support. If OSD readback fails after opening the menu, an opt-in option can reuse button fields successfully read in the current window. Saved historical values are never used for this recovery path.

Navigation candidates come from an installed model adapter, a matching local file or an explicit user-entered code/value/source. No universal HWV/HKC navigation codes are bundled. Read-only button events are evidence, not host-to-monitor navigation commands.

Pairing files contain model/firmware/capability identity, session metadata, diagnostic baselines, candidates, trials, raw read/write results, observations, bindings and bounded event captures. Files are saved per connected display in `%LOCALAPPDATA%\FluentControl\OsdPairings\`. Re-pairing archives the existing record locally and starts a new active history with the same candidates.

Import requires exact model, firmware, MCCS and capability matching. Imported records never activate remote bindings without local testing. Export is optional and does not upload anything. Raw capability strings require explicit opt-in; device instance paths are excluded. No account or website is required.
