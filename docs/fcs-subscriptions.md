# 统一订阅与有线 ROS 2 / Shared subscriptions and wired ROS 2

开发预览。**合并 Gateway 内部订阅核心，保留 ROS 2/DDS、OPC UA 各自的协议适配层。** FC、Gateway 脚本、ROS 遥测导出、OPC UA 本地资源导出读取同一套最新信号、接收时间与有效状态。C3 仍是 `fcs-node/2`，不安装 ROS、DDS 或 OPC UA。FC 仍只选择并消费 Gateway 信号。

This preview shares one internal Gateway subscription core while retaining separate ROS 2/DDS and OPC UA adapters. FC, Gateway scripts and protocol exporters consume the same signal observations and quality. C3 and its flash layout remain unchanged; FC needs no ROS installation. Build clients and Gateway from this preview together: old Gateways do not implement this API, and readers do not silently fall back to a separate acquisition path.

## 为什么这样合并 / Why this boundary

| 统一到 Gateway / Shared in Gateway | 保留在协议层 / Protocol specific |
|---|---|
| 设备/传感器/信号身份、类型、单位 / Resource identity and schema | ROS topic/message/field/QoS、DDS 发现与安全 / ROS mapping, discovery and security |
| 同设备采集复用、最新值、源序号与接收时间 / Shared acquisition and latest observations | OPC UA NodeId、证书、MonitoredItem、会话与一级挂载 / UA identity, trust, sessions and mounts |
| 变化游标、过期/失联、订阅租期与资源上限 / Change cursors, quality, leases and bounds | FC 光照类型与范围、脚本策略、消费者更严格的年龄限制 / Consumer validation and stricter age limits |

这样多客户端不会各自读取同一设备，也不会把“刚从缓存读出来”误认为“刚采到”。协议适配器仍独立部署；无需把所有依赖装进 Gateway。订阅不等于共享或云上传，后两者继续由用户显式配置。

Multiple consumers share device reads and cannot refresh a sample merely by fetching it again. Independent adapters keep their native dependencies and wire protocols. Sharing and cloud upload remain explicit, separate capabilities; this API neither enables them nor replays delayed cloud/history records as live samples.

## API

Windows 使用同用户 pipe `FluentControl.FcsGateway.v1`，每次请求一行 JSON；HTTP host 使用 `POST /rpc` 和现有只读 `gateway.token`。示例首次请求：

```json
{
  "operation": "subscriptions.read",
  "arguments": {
    "signals": [{"deviceId":"ext-ros2-room","sensorId":"light","signalId":"illuminance"}],
    "cursor": null,
    "waitMilliseconds": 1000
  }
}
```

响应示例（游标由服务器生成，客户端应原样保存）：

```json
{
  "protocol": "fcs-gateway/1",
  "subscriptionProtocol": "fcs-subscription/1",
  "cursor": "process-epoch.opaque-hash",
  "waitedMilliseconds": 12,
  "changed": true,
  "reset": false,
  "samples": [{
    "deviceId":"ext-ros2-room", "sensorId":"light", "signalId":"illuminance",
    "valueType":"number", "unit":"lx", "value":123.5,
    "valid":true, "fresh":true, "status":"ready",
    "sequence":42, "uptimeMs":10000,
    "receivedAtUtc":"2026-11-05T12:00:00Z",
    "sampleAgeMilliseconds":12, "maxAgeMilliseconds":3000,
    "transport":"adapter"
  }]
}
```

- 下一次用同一组信号和上次 `cursor` 请求；新观察或质量变化立即返回。超时返回当前快照、`changed=false`。这是有界长轮询，不是 WebSocket，也不是每次采样的事件日志。
- 首次 `cursor=null` 且 wait>0 会等待初始采集，最多到期限；wait=0 立即读快照，可能得到 `pending`。按三元身份匹配，不依赖 samples 排序。
- `status` 为 `pending/ready/stale/unavailable`。仅 ready 同时 `valid=true,fresh=true`；其他状态的 value 为 null。旧身份/计数/时间可保留用于诊断，不能继续当实时值使用。
- 合法格式但不存在的资源返回 unavailable，不会停止其他订阅或 Host。ROS 可将同一来源映射到多个输出话题：内部只订阅一次，每个话题保留自己的类型/单位检查，单一来源失效不牵连其他来源。/ Unknown resources become unavailable without stopping the Host. ROS deduplicates shared sources and projects them to every configured topic independently.
- 游标包含进程实例。Gateway 重启后返回 `reset=true`，缓存从空开始；租期过后重新订阅也会重新采集。游标不是授权令牌，也不提供断线历史补发。
- `waitedMilliseconds` 是服务器处理与等待耗时；客户端只将往返时间超过它的部分计入额外网络延迟，避免把长轮询等待重复计入数据年龄。消费者还应在本地计时并失效缓冲数据。
- 同一 sequence+uptime 的重复值不会刷新原始接收时间或延长寿命；数据变化而计数不变会失效。C3 uptime 不代表 UTC；此处时间是源接收时间，不伪装成设备采样时间。

Windows uses newline JSON over the same-user pipe; HTTP uses the existing read-only bearer token. Repeat the exact selection with the returned cursor. Changed observations or quality transitions wake readers; a timeout returns the current snapshot with `changed=false`. Initial reads may report pending. Match identities, not array order. Only ready/valid/fresh samples are actionable. A restart resets the cursor and cache; no history is replayed. Subtract server wait from round-trip duration before adding transit age, and expire buffered values locally. Repeated counters never renew sample lifetime.

## 上限和采集 / Bounds and acquisition

每请求 1..16 个不同信号；wait 为 0..2000ms；请求最大 16 KiB。每进程最多 128 个活跃信号、32 个设备、32 个并发长轮询，给管理调用留出空间；资源超限返回错误，HTTP 并发超限为 429。支持 number、integer、boolean、最多 1024 字符的 string，复杂值拒绝。

请求续租 10 秒，之后若无客户端续租则停止该订阅的额外采集。核心默认每 500ms 检查活跃设备，适配器接收新数据会主动唤醒；同设备同一时刻只有一次读取，物理设备最多 4 个并发，内存中的 ROS/CAN 来源不等待慢 USB。Windows 读取设备；Linux 沿用原后台缓存采集（默认 2 秒），不再为每个客户端增加节点请求。Linux 原有历史采集、设备发现和 ROS/CAN worker 生命周期独立，订阅租期结束不会停止它们。

源有效期上限为 60 秒，消费者可更严格：FC 最多 10 秒，脚本最多 5 秒，OPC UA 还受自身 `staleMilliseconds` 和会话看门狗限制。较慢采样源必须接受期间无实时值，不能靠扩大消费者阈值重新标为 fresh。快速变化合并成最新值，慢消费者不积压队列。端到端延迟仍受 C3 采样、Linux 采集、worker 100ms 批处理、核心调度、网络与客户端周期影响；无硬实时或无损录波保证。

Limits: 1–16 selections/request, 0–2000ms wait, 16 KiB request, 128 active signals/32 devices/32 concurrent long polls per process, scalar strings up to 1024 characters. Requests renew a 10-second lease; unused subscriptions stop additional acquisition. Default core polling is 500ms with adapter wakeups, one in-flight read/device and four physical reads; cached adapters bypass slow hardware. Linux retains its independent background collector/history and workers. Source lifetime is capped at 60s, with stricter FC/script/UA limits. Latest-value coalescing is intentional; neither deterministic latency nor lossless recording is promised.

读取凭据只能消费现有目录，不获得生产者、管理、恢复或写设备权限。当前读取令牌是整个 Gateway 的读取角色，没有新增逐用户/逐信号 ACL。ROS 导出清单与 OPC UA export/证书授权仍单独设置。原 `node.request` 保留用于元数据与显式查询；本版已迁移的订阅客户端统一走新入口。

Read credentials do not grant producer or management authority. Existing reader access is Gateway-wide, not a new per-user/per-signal ACL. ROS exports and UA export/certificate grants remain explicit. `node.request` stays available for metadata and direct queries; migrated subscription consumers use this API.

## 有线 ROS 2 部署 / Ethernet deployment

这里的有线是 **以太网/IP 上的 ROS 2/DDS**，不是在 CAN 帧内直接运行 ROS 2。CAN 仍用独立 SocketCAN worker，接入后的信号共享同一订阅核心。配置目录与令牌步骤见 [适配器指南](fcs-adapters.md)。

1. Gateway 主机与 ROS 设备接入允许互通的有线网段，确认 IP/路由、防火墙、组播/VLAN 策略。双方使用匹配的 `ROS_DOMAIN_ID`，话题、消息类型、字段与 QoS 按对方实际声明填写。
2. 加载已有 ROS 环境（CI 为 Jazzy），选择明确的 DDS 网络接口。提供 [Cyclone DDS 示例](../deploy/adapters/cyclonedds-ethernet.xml)，将 `eth0` 改为实际以太网卡名称；两端都应配置自己的正确接口。
3. 原有 SROS2 keystore/enclave/permissions 继续生效；不要为接通而关闭安全策略。`ROS_DOMAIN_ID` 仅隔离发现，不是身份验证。下列命令不改变已有安全环境变量。

```sh
. /opt/ros/jazzy/setup.sh
export ROS_DOMAIN_ID=84  # Replace with the authorized deployment's domain.
unset ROS_LOCALHOST_ONLY
export ROS_AUTOMATIC_DISCOVERY_RANGE=SUBNET
export RMW_IMPLEMENTATION=rmw_cyclonedds_cpp
export CYCLONEDDS_URI=file:///etc/fcs/cyclonedds-ethernet.xml
python3 tools/gateway-adapters/ros2_adapter.py /etc/fcs/ros2.json
```

Cyclone RMW 需预先安装。没有组播或跨路由的部署需要按既有 DDS 基础设施配置 peers/discovery，不由 Gateway 自动扫描或开放防火墙。Linux Docker 实物接入通常使用 host network；按实际 NAS/Pi 支持情况部署。生产者 TCP 仍只绑定 loopback，不向网络开放。ROS 导出读取放在独立线程，网络等待不会阻塞 ROS 输入回调；输出队列只保留最新批次。

Wired ROS means DDS over Ethernet/IP. CAN uses its own adapter and joins the same internal subscriptions. Match domain, topics/types and QoS; configure the actual Ethernet NIC on each participant. The example requires the installed Cyclone DDS RMW. Preserve SROS2 security. Multicast-restricted/routed networks need their existing DDS peer/discovery configuration. Linux containers commonly need host networking for physical peers; the producer port remains local-only. A background export reader prevents Gateway long polls from blocking ROS callbacks.

## 验证及稍后实测 / Validation and later hardware acceptance

软件检查：`tests/FluentControl.FcsSubscriptions.Tests` 验证共享读取、游标、重复样本过期、断线/恢复、租期、取消及慢源隔离；HTTP 和 Windows pipe 验证长轮询不阻塞管理调用；ROS 测试覆盖真实进程和两个独立 Docker 网络命名空间之间的 DDS 收发；原 OPC UA 加密订阅与级联回归继续执行。Docker bridge/veth 测试证明跨节点网络路径，不代替实体网线、交换机和机器人验证。CI 结果以对应提交的 Actions 为准。

实物验证顺序：

1. 有线 ROS 发布一个允许的数值话题，确认 Gateway 出现对应虚拟设备；lux 信号才能接 FC 自动亮度。
2. FC、OPC UA 同时订阅同一信号，确认数值、源时间和有效状态一致；另选 C3 信号验证 ROS 遥测输出。
3. 停 ROS 发布者或拔网线，等待配置的有效期，确认无效/null、FC 暂停使用旧值；恢复连接后新样本自动恢复。
4. 退出订阅客户端，检查租期结束与重新订阅；测试实际 QoS/SROS2、多网卡选择、NAS/Pi 重启及长时间运行。

Software tests cover shared reads, freshness, cursors, leases, cancellation, slow-source isolation, HTTP/pipe concurrency, real ROS processes and DDS across two container Ethernet peers, plus encrypted UA regression. Physical acceptance remains: NIC/cable/switch behavior, QoS/SROS2, disconnect/recovery, FC automation and long-running NAS/Pi deployment. See [ROS discovery](https://docs.ros.org/en/jazzy/Tutorials/Advanced/Improved-Dynamic-Discovery.html) and [Cyclone interfaces](https://cyclonedds.io/docs/cyclonedds/latest/config/network_interfaces.html).
