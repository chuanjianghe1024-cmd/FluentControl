# FCS Gateway: local Windows, Linux and Docker

## 中文

FC 默认继续使用随安装包提供的本机 Windows Gateway：按需启动，无需 Docker，无需 NAS。USB 发现、配网、管理和本机显示器控制继续可用。设置 → 自动调节 → 设备与接入可选择远程网关，或点击“使用 FC 自带的本机网关”切回；切换会取消正在进行的自动亮度采样及过渡。

独立 `FluentControl.FcsHost` 使用 .NET 10，面向 Linux amd64/arm64（NAS、树莓派等）。桌面及内置 Gateway 保持原运行时。两种 Gateway 编译同一份 `FcsNode.cs` 操作和参数校验，使用既定 `fcs-node/2`，不另建节点协议。远程宿主第一阶段只通过局域网读取节点，USB 管理仍只在 Windows 本机进行。路由器需要支持对应架构的完整 Linux/Docker；不承诺 OpenWrt/MIPS/32 位 ARM 均可用。

### 统一管理页（2026-10-29 开发版）

Windows 与 Linux 编译同一份 `tools/fcs-manager` 页面和 `manager.capabilities` 契约。打开就是设备列表，可查看连接、传感器、网关脚本清单；页面按宿主实际能力控制入口。FC 的“打开 FCS 设备管理”跟随当前选中的网关：本机启动 Windows 管理器，远程则打开已验证的网关根地址，不把 FC 令牌带入 URL。

- Windows：保留 USB 扫描、配网、设备插件/规则、身份验证恢复。原来的本机随机会话令牌不变。
- Linux/Docker：打开网关根地址（默认本机 `http://127.0.0.1:8767/`），输入 `manager.token`。可主动扫描局域网，或添加/更新设备 ID 与私人 IPv4；登记原子写入 `nodes.json` 并立即生效，无需重启。更换 IP 会丢弃该节点的旧 live 状态，重新验证身份。移除登记不清除设备配置或历史；再次扫描仍可能重新发现。
- `gateway.token` 继续只用于 FC/HA 读取；独立生成的 `manager.token` 用于管理页，不能混用。令牌仅保存在页面内存，不写浏览器存储；断开或刷新后重新登录。配置页不显示令牌，服务日志不记录令牌。
- Linux 目前仍是 LAN-only：USB、设备写入、插件安装和恢复入口不可用，后端也拒绝这些操作。两端脚本清单一致，但脚本创建/验证/安装仍通过对应宿主 CLI；本次不增加脚本编辑器。
- HTTPS 反向代理需在宿主设置 `FCS_MANAGER_ORIGIN=https://gateway.example` 为浏览器实际访问的完整 origin（含非默认端口，无子路径），重启服务。默认按直连请求的 scheme/host 校验 Origin，不信任任意转发头；不开放 CORS。远程管理应通过 HTTPS 或 SSH 隧道访问。

```sh
# 仅在自己的终端读取管理令牌，然后粘贴到管理页；不要分享：
docker compose -f deploy/fcs/compose.yaml exec gateway cat /data/manager.token
```

### Docker

在仓库根目录执行：

```sh
docker compose -f deploy/fcs/compose.yaml up -d --build
# 首次启动后，把示例中的 ID 和 IP 改为真实设备值，再复制配置：
docker compose -f deploy/fcs/compose.yaml cp deploy/fcs/nodes.example.json gateway:/data/nodes.json
docker compose -f deploy/fcs/compose.yaml restart gateway
# 仅在自己的终端中读取令牌，用于 FC 的密码输入框；不要放入日志或分享：
docker compose -f deploy/fcs/compose.yaml exec gateway cat /data/gateway.token
```

Compose 使用持久卷，默认端口只绑定宿主 `127.0.0.1:8767`。其他电脑访问时推荐通过反向代理提供 HTTPS，然后在 FC 输入根地址，例如 `https://gateway.example/`。HTTPS 使用系统证书校验。若只在可信局域网使用明文 HTTP，需要自行把端口绑定改为宿主的私人 IPv4，例如 `192.168.1.10:8767:8080`，并在 FC 显式勾选允许明文 HTTP；HTTP 地址必须是字面量私人 IP（环回地址可直接使用）。远程连接失败不会静默切回本机。

桥接网络使用 `nodes.json` 中的固定设备 ID/IP，不依赖广播；建议路由器 DHCP 保留地址。`discover: true` 启用 UDP 发现，但 Docker 广播通常需要 Linux host 网络：移除 `ports`，设置 `network_mode: host` 和 `ASPNETCORE_URLS=http://127.0.0.1:8767`，仍通过 HTTPS 代理访问。显式配置优先于发现；每次采集校验设备 ID 和实际 operation catalog。管理页登记即时生效；手动编辑文件、采样周期或自动发现开关后重启。

镜像以非 root 用户运行，Compose 限制日志大小、移除 capabilities，并在退出时刷新历史。普通停止不会删除数据卷；不要使用 `down -v`，除非确实要删除配置、令牌及历史。首次随机生成 256 位令牌，文件权限 0600；手动替换为新的 64 位十六进制随机值并重启可轮换令牌，随后更新 FC 保存的令牌。FC 用当前 Windows 用户的 DPAPI 保存令牌，不写入普通设置或订阅 URL。

### 原生 Linux

```sh
dotnet publish src/FluentControl.FcsHost -c Release -r linux-arm64 --self-contained true -o host-out
# x86-64 使用 linux-x64
FCS_DATA="$PWD/data" ASPNETCORE_URLS=http://127.0.0.1:8767 ./host-out/FluentControl.FcsHost
```

常驻服务可用 `deploy/fcs/fcs-gateway.service`：创建专用 `fcs` 用户，将发布文件放入 `/opt/fcs-gateway`，安装 unit 后启用。systemd 为配置和历史创建 `/var/lib/fcs-gateway`。无需安装桌面或 Windows UI。

### 数据、权限和验证边界

- `POST /rpc` 使用 `Authorization: Bearer <token>`，使用 `gateway.token` 时只允许 `status`、`list`/`discover`（读取后台发现结果）、`node.request` 的 LAN 只读操作及 `history.read`。`manager.token` 额外允许 `manager.capabilities`、`scripts.list`、`nodes.save/remove`，并让 `discover` 主动扫描；登记权限不授予设备写权限。`GET /healthz` 只报告进程存活，不代表传感器在线。USB、采样配置写入、停服务、插件安装及任意 URL 代理不开放。
- 后台统一采集，默认 2 秒一次，每轮最多并发 4 个节点，最多 32 个节点；客户端不会触发额外物理采集。网络读取限时、限长且禁止重定向。离线或身份改变立即停止提供 live 数据；重启只恢复历史，不把历史当作新样本。
- FC 读取远程光照时同时检查节点 `valid/fresh`、单位及 Gateway 缓存年龄，最长 10 秒，网络耗时计入年龄。远程关闭不积压亮度命令；电脑恢复后必须重新读有效数据。
- `history.jsonl` 最多 4096 条且不超过 8 MiB，整体滚动，容量不是固定天数；每分钟和正常退出时原子保存。掉电最多丢失上次保存后的一分钟。`history.read` 按 deviceId 返回最近 64 条，仅供读取，不回放控制。配置、令牌与历史均在数据卷中。
- 本阶段没有远程 USB、云同步、远程规则引擎或远程 MCP 入口。Windows 现有本机 MCP 保留。独立宿主的模拟节点、自检和 HTTP 集成测试不能代替真实 C3、NAS、树莓派及显示器验收。

## English

Windows and Linux now embed the same manager page and capability contract. FC opens the selected Gateway's manager without placing credentials in the URL. On Linux, open the root URL and enter the separately generated `manager.token`; the existing `gateway.token` remains read-only for FC/HA. Browser credentials stay in memory only. Administrators can scan LAN, save/update private-IP node registrations without restart, remove registrations without deleting device settings/history, and inspect script inventory. A changed route invalidates live state before identity is checked again. Discovery may find a removed node again.

USB management/recovery remain Windows-only and are disabled and rejected on Linux. Script editing/installing still uses the CLI. For an HTTPS reverse proxy, configure `FCS_MANAGER_ORIGIN` to the exact external origin and restart; arbitrary forwarded headers and cross-origin requests are not trusted. Use HTTPS or an SSH tunnel for remote management. Both tokens can be rotated separately by replacing their files and restarting.

FC still defaults to its bundled, on-demand Windows Gateway. No Docker or separate device is required. USB setup and display control stay on the PC. Under Automatic adjustment → Devices & setup, test and connect to an optional remote Gateway or switch back to FC's bundled Gateway. Switching cancels pending automatic samples and transitions.

The independent .NET 10 host targets Linux amd64 and arm64. Both hosts compile the same node operation/argument validator and retain `fcs-node/2`. This first standalone version collects over LAN only. Router support depends on a compatible full Linux/Docker environment; MIPS and 32-bit ARM are not promised.

Use the Docker commands above from the repository root. Replace the example node ID/IP with the physical device identity and a private IPv4 address, copy `nodes.json` into `/data`, then restart. The persistent named volume holds configuration, a generated 256-bit token and bounded history. Ports default to host loopback; expose a trusted HTTPS reverse proxy for other computers. For explicitly opted-in plaintext LAN use, bind the port to the host's private IP and select the corresponding FC checkbox. Credentials in URLs, redirects and public-IP HTTP are rejected. Tokens are protected with per-user DPAPI in FC.

Bridge networking works with configured IPs. Optional UDP discovery (`discover: true`) generally requires Linux host networking in Docker; remove `ports`, set `network_mode: host`, and explicitly bind `ASPNETCORE_URLS` to the desired interface. Configured nodes take precedence. UI node registrations apply immediately; restart after manual file/token changes. Do not remove the data volume unless deletion is intended. For native Linux, publish self-contained as shown above; the sample systemd unit expects an `fcs` user and binaries at `/opt/fcs-gateway`, with state under `/var/lib/fcs-gateway`.

With the reader token, `POST /rpc` exposes status, cached discovery/listing, LAN read-only node requests and the most recent 64 history records per device. The separate manager token additionally grants manager capabilities, script inventory, live discovery and persistent host node registration; it grants no device writes. The unauthenticated `/healthz` reports process liveness only. Remote USB, writes, shutdown, arbitrary URL forwarding, hosted cloud sync and remote MCP are not implemented. Existing local Windows MCP remains available.

A single background collector serves all clients (default 2-second poll, up to 32 nodes, four concurrent nodes). Identity and operation availability are checked during collection. Failed or stale nodes cannot supply live data; restart does not promote history to live state. FC also checks validity, freshness, units and transport age, including request time, with a 10-second maximum. There is no offline brightness-command queue.

History rolls at 4096 records or 8 MiB, whichever comes first. Atomic snapshots occur each minute and on graceful shutdown; sudden power loss can lose up to one minute. This is bounded sample history, not a guaranteed number of days. Automated tests cover simulated nodes, authentication, identity changes, stale data, history bounds/restart and HTTP restrictions. Physical C3/NAS/Pi/display validation remains separate.

## Local scripts / 本地脚本

Linux host 与 Windows Gateway 共用受限脚本核心；容器含 Python，原生部署需 Python 3.10+ 并设置 `FCS_PYTHON`。任务放在数据目录的 `scripts/`，通过 CLI 创建/验证/干运行后才显式启用。见 [脚本操作](fcs-plugin-contract.md)。HTTP `/rpc` 不接受脚本上传或设备写入；管理令牌仅额外允许宿主设备登记与管理清单。Docker bridge 不保证局域网 WoL 广播可达；默认推荐由独立供电的 C3 执行唤醒。
