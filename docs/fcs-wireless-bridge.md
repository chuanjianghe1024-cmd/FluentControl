# C3 小节点与 BTHome 接入桥 / C3 wireless bridge preview

这是未发布的开发预览，配套固件在私有 FCS 仓库 `codex/wireless-bridge`。需同时使用该固件与本分支 Gateway。无线距离、丢包、共存、实际 Flash/空闲堆与长时间运行须在真实 C3 上验收；模拟/CI 不替代实机。

## 数据与职责

ESP-NOW C3 + BH1750 → 桥接 C3；BTHome v2 广播 → 同一桥接 C3；桥接 C3 经 Wi-Fi 由本机 worker 读取，送入现有 Gateway 适配器授权目录，FC 继续通过 `subscriptions.read` 订阅。ROS 2、OPC UA、脚本、HA 可以继续使用 Gateway 已有接入方式，不新增第二套订阅核心。

桥上自带的 BH1750 保持原 `deviceId/ambient-01/illuminance`。远端节点在 Gateway 使用 `ext-radio-*`、`physical=false`；不是桥板制造清单中新焊接的传感器。首版是一个桥接层，无多跳、自动路由、云上传或自动共享。启用是本机显式配置，不由附近广播自动登记。

| 能力 | 首版范围 |
| --- | --- |
| ESP-NOW | 最多 4 个预配加密单播节点；每节点返回 BH1750 illuminance；约 1.5 秒查询一次 |
| BLE | 最多 8 个固定 MAC/addressType 来源；每来源最多 4 个选定信号，总共最多 32 路远端信号 |
| BTHome v2 | 未加密广播，需 packet ID；battery、temperature、humidity、illuminance、opening、motion；无主动 GATT 连接、无蓝牙设备控制 |
| 缓存 | C3 与 Gateway 都是有界 RAM 最新值；没有高频 Flash 日志 |
| 权限 | C3 配对/配置仅 USB；桥读数使用独立挑战 HMAC 的现有 bindingKey；Gateway 使用另外的本机 producer token 与固定授权 |

BTHome 明文广播的 MAC 白名单**不是身份认证**，也没有可信采样时间或跨重启防重放保证。重复 packet ID 不续鲜；首次接收年龄表示本桥观察时间。它适合环境遥测，不应单独触发安防、门锁等敏感动作。加密 BTHome、米家专有格式及 Home Assistant 通用 Bluetooth Proxy 协议尚未实现；本方案是 FCS 数据桥，不是 ESPHome 代理替代品。

## 1. 准备与 USB 配置

准备一块常供电桥接 C3，以及一块或多块小节点 C3；当前小节点仍使用真实 BH1750 的原板型。刷入对应分支 Actions 的 `fcs-c3-thonny.bin` / `fcs-c3-recovery.zip`，首装标识与恢复步骤沿用 FCS 的安装恢复手册，不整片擦除身份区、不操作陌生 C3。

在 Windows Gateway 管理页打开 USB 设备 → 小节点 / 蓝牙接入桥 → 读取桥接状态，记录 `stationMac`。配置通过 `bridge.configure` 写入，状态不会回传 PMK/配对密钥。Linux Gateway 仍是 LAN 管理，不新增 Linux USB 恢复/写配置能力；可先在 Windows 配好板子，再交给 NAS/Pi/Linux Gateway。

为网络生成一个 PMK，为每对节点与桥生成不同的 16 字节密钥。两端填相同的该对 key，全部设备填相同 PMK；不要使用文档占位文本或公开测试密钥。可以运行两次：

```sh
python -c "import secrets; print(secrets.token_hex(16))"
```

将路由器 2.4 GHz 信道固定为 1–11 中的一个，例如 6；桥接 C3 的 Wi-Fi 与 ESP-NOW 必须同信道。关闭路由器自动换信道，或变更后经 USB 更新所有节点的 channel。节点不做信道扫描；不影响正在关联的 Wi-Fi 去强行切换信道。

小节点配置（peer MAC 是桥的 STA MAC）：

```json
{"role":"node","channel":6,"pmk":"REPLACE_WITH_NETWORK_PMK","peers":[{"mac":"REPLACE_WITH_BRIDGE_STA_MAC","key":"REPLACE_WITH_PAIR_KEY"}],"bthome":[]}
```

不需让小节点接路由器；有旧 Wi-Fi 配置时先通过 USB 忘记网络。node 角色会关闭设置热点，后续重启也不会自动开热点，保留 USB 管理。需要热点时仍可显式 `ap.start`，期间 ESP-NOW 暂停。

桥接配置（peer MAC 是小节点 STA MAC；BLE MAC 使用连续小写十六进制，addressType 0=public、1=random static；旋转私有地址不支持）：

```json
{"role":"bridge","channel":6,"pmk":"REPLACE_WITH_NETWORK_PMK","peers":[{"mac":"020000000001","key":"REPLACE_WITH_PAIR_KEY"}],"bthome":[{"mac":"aabbccddeeff","addressType":0,"signals":["temperature"]}]}
```

仅 BLE 时 peers=[]、pmk=""；仅 ESP-NOW 时 bthome=[]。先完成桥的 Wi-Fi 配网并关闭设置热点，再验收无线接入。停用配置为 `{"role":"off","channel":1,"pmk":"","peers":[],"bthome":[]}`。全局 wireless.disable 与恢复出厂也会停用转发。

BTHome 观察模式独占原 BLE singleton，启用时暂停旧 FCS BLE GATT 查询，关闭后恢复原设置。扫描 interval=100 ms/window=30 ms、被动扫描，不建立 BLE 连接。Wi-Fi/BLE/ESP-NOW 共用 2.4 GHz 射频，实际丢包仍需测试。

## 2. Gateway 授权与 worker

复制 [授权示例](../deploy/adapters/c3-bridge-grant.example.json) 到 Gateway 数据目录的 `adapters.json`，或合并其 radio grant 到已有文件，**不要覆盖其他适配器**。核对映射后显式将 enabled 改为 true，重启 Gateway。`kind=custom` 复用现有生产者接口；会生成私有 `adapter-tokens/radio.token`。

复制 [worker 示例](../deploy/adapters/c3-bridge.example.json) 到本机私有目录，替换桥的 IP、完整 deviceId、来源 MAC 和路径。两份配置的输出 deviceId/sensorId/signalId/valueType/unit/staleMilliseconds 必须一致。BLE 地址来自设备资料/可信配置，当前没有扫描认领 UI。

通过物理 USB 的 `status` 取得桥的 `bindingKey`，保存为 worker 配置引用的 `bridge.key`（只有 64 位十六进制文本）。可使用 FCS 本机控制台；使用控制台时先退出占用同一串口的 Gateway。不要把 key、PMK、配置中的密钥、token 或用户真实 MAC 提交到仓库。Linux 将私有目录权限设为 700、密钥文件 600；Windows 放在当前用户私有目录。

在 Gateway **同一主机/容器网络命名空间**运行可信 Python 3.10+ worker（只依赖标准库）：

```sh
python tools/gateway-adapters/c3_bridge_adapter.py /path/to/private/c3-bridge.json
```

Windows 同样运行 `python ...`，修改 JSON 内路径，例如 `C:/Users/NAME/AppData/Local/FluentControl/FcsGateway/adapter-tokens/radio.token`，以实际 Gateway 数据目录为准。NAS/Docker 可把 worker 作为同一容器内受监督进程或共享其网络命名空间的 sidecar；不要把仅 loopback 的 producer 入口暴露到局域网。目录、现有宿主启动见 [适配器指南](fcs-adapters.md)。worker 尚未自动捆绑或由管理页启停，需自行配 systemd/容器监督/Windows 任务。

Gateway 管理页会出现 `ext-radio-desk`、`ext-radio-room`；FC 打开现有 FCS 订阅列表选择其中信号即可，光照自动亮度仍只接受 illuminance/number/lx。温湿度等用于查询、Gateway 脚本和已配置联动。

## 时间、故障和恢复

ESP-NOW 使用加密单播和应用 HMAC，桥每轮随机挑战只接受一次响应，900 ms 过期；节点仅在有效查询后测量，限 1 Hz。缓存年龄从桥发出查询时开始，保守包含采样/传输时间。无线丢包不重放动作，也不把旧值当作新测量。

桥 LAN 返回 `fcs-node/2` + `extensions.fcs.bridge`（`fcs-bridge/1`），含 bootId、sourceId、sequence、ageMilliseconds、类型/单位/质量。HTTP `GET /v2/bridge` 双向 HMAC 与随机 nonce 使用 `BRIDGE-READ` 域，与旧 READ 分离；无签名公共请求不可读取此缓存。HTTP 本身未加密，HMAC 提供身份/完整性，需在可信局域网或安全隧道中使用。

worker 将原始年龄加上保守网络耗时，按 bootId/sequence 去重，再通过已有 producer 发送；读取重试、Publisher 重试与重复广播都不刷新原接收时间。TTL 由授权决定（500–60000 ms）；离线后订阅进入 stale/unavailable，旧值变 null。单个桥错误只影响其 worker/信号，不终止 Gateway 其他服务。原始 C3 BH1750 USB/LAN 查询保持可用。

验收时依次测试 USB 本地光照、一个 ESP-NOW 节点、一个 BTHome 来源、同时接入、拔掉节点/桥、错误密钥、改信道、重启、全局无线关闭；记录内存、连续运行时间、丢包与实际间隔。不要据此预览宣称实机已通过。

## English

This unreleased preview connects up to four paired ESP-NOW BH1750 nodes and eight explicitly selected BTHome v2 broadcasters through a C3 Wi-Fi bridge. The physical BH1750 inventory and `fcs-node/2` remain intact. Remote values become explicitly granted `ext-*` virtual Gateway resources consumed by the existing subscription core, FC and configured integrations.

Install the matching FCS firmware, provision keys/roles over USB, match the router's fixed 2.4 GHz channel, enable the supplied adapter grant and run the standard-library Python worker alongside Gateway. PMK is shared by the network; LMKs are pairwise. Binding keys and local producer tokens remain separate. Linux deployment supports LAN acquisition; USB provisioning/recovery still requires the supported Windows path. No automatic worker packaging/startup, cloud upload, sharing, mesh, encrypted BTHome, Xiaomi decoder or generic Bluetooth Proxy is included.

BLE support is a passive, unencrypted BTHome subset with packet IDs: battery, temperature, humidity, illuminance, opening and motion. MAC allowlists are not authentication; BLE age is observation age, not a verified sensor timestamp. Observer mode pauses legacy FCS BLE GATT queries. ESP-NOW replies are challenge-bound and authenticated; duplicate/expired data never renews freshness. All buffers are bounded RAM. Software checks and firmware builds must be reported separately from physical RF, coexistence, recovery and long-running acceptance.
