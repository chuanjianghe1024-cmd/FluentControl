# FCS 插件与脚本首版 / Plugins and scripts v1

当前开发版实现 USB 安装的 C3 MicroPython 动作插件、C3 光照阈值规则，以及桌面 Gateway/Linux host 的受限 Python 脚本。实际接口通过 `operations.list` 发现；旧固件的 planned 操作仍不可执行。完整 C3/WoL 实机验收待进行。

## 执行位置

| 执行者 | 已实现 | 前提 |
|---|---|---|
| C3 | 稳定节点查询、WoL 动作插件、本地阈值/持续时间/回差/冷却 | 独立供电、已配网、通过 USB 安装并启用插件与后台采样 |
| FC 内置 Gateway | USB 插件安装管理、复杂多信号脚本、WoL、显式 HTTPS POST | Windows 在线；脚本功能另需 Python 3.10+ |
| Linux/Docker host | 共享脚本解释器、LAN 数据采集、HA REST 桥接、WoL/HTTPS | 常驻服务器；Docker 内含 Python；不支持 USB 安装 |
| Home Assistant | 通过 REST sensor 消费 FCS 数据，再用 HA 自动化联动其他设备 | 配置 HA 集成；无 FCS MQTT Discovery 自动发现 |

每条自动化只选择一个执行者。电脑关机后自身 Gateway 不能唤醒自身，可用仍供电的 C3 或其他常驻 Gateway。HA/HomeKit/米家关联设备的可用程度取决于对应 HA 集成；本项目没有原生米家产品认证。

## C3 包、USB 事务和恢复

实际包格式（严格字段，非之前的规划 manifest）：

```json
{"id":"wol","version":"1.0.0","runtime":"micropython-v1","permissions":["network.wol"],"source":"def run(ctx, arguments):\n    return ctx.wol(ctx.settings['mac'], ctx.settings['broadcast'], 9)\n"}
```

源码与打包器在 FCS 仓库 `plugins/wol` 和 `scripts/build_plugin.py`。入口 `run(ctx, arguments)`；`ctx.settings` 为配置，`ctx.sample` 为规则样本（手动为 None）。`ctx.emit(data)` 需 `events.emit`；仅保留最近 16 条 RAM 事件。权限约束 SDK，**MicroPython 插件不是安全沙箱**，只能安装可信代码；不支持 MPY、native、额外模块或插件市场。

所有下列操作当前仅 USB：

| 操作 | 参数 / 行为 |
|---|---|
| `storage.get` | 实际文件系统容量/余量、堆、数据分区和限制 |
| `plugin.list` | 插件版本、启用状态、配置、安全模式、存储与 RAM 事件 |
| `plugin.begin` | `id,size,sha256`，返回临时 `token` |
| `plugin.chunk` | `token,offset,hex`，最多 512 字节、严格顺序 |
| `plugin.commit` / `plugin.abort` | `token`；提交校验长度、SHA-256、语法，不执行源码 |
| `plugin.configure` | `id,enabled,settings`；安装/更新后默认禁用 |
| `plugin.run` | `id,arguments`；一次执行，不重试 |
| `plugin.rollback` / `plugin.remove` | `id`；回滚后禁用，卸载清除代码槽 |
| `plugin.recover` | 无参数；退出安全模式，保留全部插件禁用 |
| `rules.list` / `rules.set` | 无参数 / `rules` 数组；替换整份规则集 |

4 MiB 为 Flash 总容量，系统分区和驱动占用后才是数据空间。最多 4 插件，包 12 KiB/源码 8 KiB、配置 1 KiB；安装按实际余量预留 64 KiB、暂存和状态空间。双代码槽、原子状态替换、超时暂存清理，保留上一版回滚。64 条传感器历史与 16 条执行记录仅 RAM。插件异常禁用；超过 250 ms 返回预算禁用；无限循环由 8 秒 WDT 复位进入安全模式。它不提供实时抢占或进程隔离。

规则示例：

```json
{"rules":[{"id":"light-wake","pluginId":"wol","sensorId":"ambient-01","signalId":"illuminance","above":300,"rearmBelow":200,"holdMs":10000,"cooldownMs":300000,"enabled":true,"arguments":{}}]}
```

最多 8 条且序列化总计 1536 字节。先观察低于回差值，再连续高于阈值满持续时间才触发；重新回落才能再触发。需启用后台采样，推荐 1000 ms。启动、无效数据、超过 5 秒中断解除待触发状态，不补发旧动作。无线管理仍未开放。

## Gateway 脚本创建、验证与运行

脚本语言标识 `fcs-python-subset/1`，语法类似 Python，由随 Gateway 分发的 AST 解释器执行，不会 `exec` 用户源码。Windows 可设置 `FCS_PYTHON` 为已安装 Python 3.10+ 的完整路径；默认调用 `python`。缺少 Python 只影响脚本，节点查询和 C3 管理不依赖 Python。Linux 容器已包含 Python。

在 FC 安装目录（Linux 将 EXE 换成 `./FluentControl.FcsHost`）：

```powershell
.\FluentControl.FcsGateway.exe --script-create light-wake.json
.\FluentControl.FcsGateway.exe --script-validate light-wake.json
.\FluentControl.FcsGateway.exe --script-test light-wake.json signals.json
```

创建不会覆盖现有文件，初始 `enabled=false`。编辑 `inputs` 中设备/传感器/信号 ID 和 `targets.desktop` 的实际 MAC/广播地址。测试输入例如 `{"desk":{"value":500},"clock":{"utcHour":10,"utcMinute":0}}`。测试只输出动作意图，**不会发送 WoL 或 HTTP**。

验证和测试通过后，将文件保存至 `%LOCALAPPDATA%\FluentControl\GatewayScripts`（可由 `FCS_SCRIPTS` 覆盖），显式改 `enabled=true`；FC 或 `FluentControl.FcsGateway.exe --serve` 需运行。Linux 为 `FCS_DATA/scripts`，容器默认 `/data/scripts`。修改文件后自动重载，不重放旧动作。`execution-status.json` 仅保存最近一次执行状态，不含凭据。

每份 JSON 有 `format:"fcs.gateway-script/1"`, `id`, `enabled`, `inputs`, `settings`, `targets`, `holdMs`, `cooldownMs`, `source`；可选 `rearmMode:"explicit"`，生成的示例使用明确回差：

```python
def run(signals, settings):
    light = signals['desk']['value']
    if light < settings['below']:
        return [{'action': 'rearm'}]
    if light > settings['above'] and 7 <= signals['clock']['utcHour'] < 23:
        return [{'action': 'wol', 'target': 'desktop'}]
    return []
```

`clock` 为 Gateway 当前 **UTC** 时间。多个 `inputs` 可用于跨节点比较；仅全部输入有效且新鲜时评估。`rearm` 不能与动作混用；显式模式的 `[]` 不重新武装，保留回差。默认 `empty` 模式下 `[]` 为条件不满足/重新武装。启动必须先观察重新武装条件，不能靠启动时恰好满足条件执行动作。

支持赋值、if/else、for、数值运算、比较、布尔、字典/列表取值，以及 min/max/sum/len/abs/round/range；不支持 import、属性访问、while、用户函数调用、文件/进程/网络访问。最多 8 个任务、每个 8 输入/8 动作；源码 16 KiB、AST 2048 节点、4096 执行步、循环 64 项、进程 3 秒截止，数据有深度/长度限制。扫描周期 2 秒，加上采集和解释耗时，不能当实时控制器。

## HTTPS 上传（默认关闭）

仅 Gateway 执行动作，C3 不保存云端凭据。显式配置一个固定 HTTPS 目标，例如：

```json
{"kind":"https","url":"https://your-service.example/telemetry","tokenFile":"/data/secrets/telemetry.token"}
```

脚本返回 `{"action":"https","target":"cloud","data":{"illuminance":123}}`，`cloud` 必须匹配 `targets` 中同名配置。`tokenFile` 可省略；若配置，必须为绝对路径，读取为 Bearer token，不随脚本包分享。数据上限 8 KiB、5 秒超时、不跟随重定向、不自动重试或离线补传。由用户选择哪些信号上报并配置接收服务，接收端的授权/保留/删除由该服务负责；未提供 FCS 云账户或云存储服务。

WoL 目标只允许私有 IPv4、UDP 7/9；发送 6 个 FF + 16 次目标 MAC。`sent` 仅证明提交发送，不证明目标已醒。Docker bridge 的广播通常不能穿透到实体 LAN；要在 Linux 上用 host network（同时调整 HTTP 监听/防火墙），或让 C3 执行 WoL。默认 Compose 保持较小暴露面，不自动开启 host network。

## 验证边界

单元/集成测试覆盖存储限额、分块摘要、USB 权限、回滚、安全模式、触发重装、脚本禁止 I/O、资源预算、动作目标与干运行。Windows CI 检查安装包含执行器；Linux CI 验证原生和 Docker。实机 C3 运行内存、WDT/Wi-Fi 并发、真实网络广播、网卡唤醒与 HA 实例仍需验收。

English: The development implementation supports trusted USB-installed MicroPython action plugins and bounded local threshold rules on C3, plus a restricted Python-syntax interpreter for desktop/Linux Gateway scripts. Create, validate and dry-run before enabling; dry-runs have no network effects. Windows scripting optionally requires Python 3.10+, while Docker includes it. Device code installation remains physical-USB-only. Actual free space is measured; 4 MiB flash is not plugin storage. HA uses the REST example; MQTT discovery, native Xiaomi certification, cloud accounts and plugin marketplaces are not implemented.
