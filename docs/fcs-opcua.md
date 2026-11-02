# FCS OPC UA 开发预览 / Development preview

OPC UA 适配器是按需运行的独立 .NET 10 进程，源码暂放在 FC 仓库 `src/FluentControl.FcsOpcUa`。它连接现有 Windows Gateway 的同用户 Named Pipe，或 Linux Gateway 的本机 HTTP API。C3 继续使用 `fcs-node/2`，无需安装 OPC UA、修改固件或占用额外 Flash。此版本尚未加入 FC 安装包或管理页面，不是稳定 Release，也未通过 OPC Foundation 一致性认证。

## 已实现范围

- 配置明确选择需要导出的信号；不自动共享全部设备。启动/退出适配器不更改设备、FC 订阅或云配置。
- `Objects/FCS/本系统/Gateway/Device/Sensor/Signal` 目录，OPC UA Browse、Read、MonitoredItem/Subscription。支持 number→Double、integer→Int64、boolean→Boolean、string→String；复杂 object/array 明确拒绝，暂不隐式转成字符串。
- 稳定命名空间 URI `urn:fluentcontrol:fcs:opcua:1`；Signal 的字符串 NodeId 为 `fcs/{systemId}/{gatewayId}/{deviceId}/{sensorId}/{signalId}`。客户端应解析命名空间 URI，不能固定依赖 `ns=2`。系统/Gateway ID 由部署者持久配置，移动位置不要更改 ID。
- `OriginSystem`、`OriginGateway`、`Unit`、`ValueType`、`ExportKind`、`TimestampBasis` 只读元数据。只暴露配置选择的信号；没有恢复、插件安装、脚本执行或设备写入 Method。
- 两个 Gateway 可建立加密的 OPC UA 订阅，将对方明确选择的本地资源挂到 `Mounted systems`。原始资源 NodeId、类型、单位、时间与质量保留，不能把挂载信号伪装成本机设备。
- 首版仅一级挂载，不接受对方的 `ExportKind=mounted`，不自动递归发现或二次共享；自挂载、重复资源和重复系统均拒绝。任意深度树、网状路由、地图展示、动态授权委托是后续工作。

## 启动

需 .NET 10 SDK 构建；自包含发布后的目标机器不需要另装 .NET：

```sh
dotnet publish src/FluentControl.FcsOpcUa -c Release -r linux-x64 --self-contained true -o opcua-out
# Windows 使用 win-x64；ARM64 Linux 使用 linux-arm64。
```

将 `deploy/opcua/linux.example.json` 或 `windows.example.json` 复制到用户可写目录，替换实际 deviceId/sensorId/signalId。相对文件路径以**进程工作目录**为准。Linux 使用原有只读 `gateway.token`，不需要 `manager.token`。HTTP 地址仅允许字面量回环 IP，禁止携带令牌跨网络发送或跟随重定向。Windows 以与现有 Gateway 相同的用户运行，先启动 `FluentControl.FcsGateway.exe --serve`；适配器不会自动启动/停止 Gateway。

```sh
./opcua-out/FluentControl.FcsOpcUa --init opcua.json
./opcua-out/FluentControl.FcsOpcUa --config opcua.json
```

`--init` 仅生成应用证书，不监听、不采集。`--config` 启动，Ctrl+C 停止。修改导出、移除信号或撤销证书信任后重启适配器，使旧会话关闭；不自动在线热更新配置。

## 证书与权限

仅提供 `Basic256Sha256 / SignAndEncrypt`；无无加密端点、自动信任或用户名密码后门。认证依据是双方信任的应用证书；可信应用以 Anonymous 用户身份获得**当前配置的全部只读导出**，首版无逐用户 ACL。连接的客户端需支持安全端点与证书交换。

1. 在每个 Gateway/客户端生成自己的证书。服务器证书需包含实际连接地址的 DNS/IP SAN；改为 LAN 主机名时先设置正确 endpoint，再初始化证书。
2. 只交换 `pkiDirectory/own/certs` 中的公钥 `.der`，核对 SHA-256 指纹后放到对方 `pkiDirectory/trusted/certs`。**不要交换 own/private 中的私钥。** 客户端也必须信任服务器证书。
3. 未信任证书会被拒绝；可在 rejected 中检查其指纹，但不要不经核对直接批量信任。保持 PKI 和配置目录仅运行账户可写，Windows 使用对应用户 ACL。
4. 使用防火墙限制 OPC UA 端口到所需客户端。端点 URL 的主机名用于地址与证书校验，不应当作监听接口防火墙。不要将开发版直接暴露公网。

级联时两个 Gateway 互相信任应用证书，在上层配置 `aggregate.example.json` 中的 mount，并核对对方 systemId/gatewayId、信号类型与单位。不是点击分享就自动获得对方权限。两个示例可在同机不同端口运行；跨主机需替换为实际名称且证书包含该名称。

## 实时、延迟与失联

客户端订阅使用真实 OPC UA Subscription；适配器到既有 Gateway 的采集仍是有界轮询，默认 2 秒，所以这是实时变化通知，不是硬实时系统或每一次原始采样的无损流。队列保留最新值；客户端需要历史时仍用既有历史接口，首版没有 UA Historical Access。

C3 的 uptime 不是 UTC。UA `SourceTimestamp` 表示**源 Gateway 接收时间**（Windows 没有该字段时为适配器首次接收时间），由 `TimestampBasis=origin-gateway-receipt` 明确说明。级联保持这个时间，不能以中转时间刷新旧数据的年龄。客户端可查看质量和 SourceTimestamp；跨主机应同步系统时钟。

- 初始未收到数据：`BadWaitingForInitialData`。
- 无驱动/无效读数：`BadSensorFailure`。
- Gateway 不可达：`BadNotConnected`；过期、fresh=false、长时间相同 sequence/uptime：`BadNoCommunication`。
- 身份/类型/单位或响应结构不匹配：`BadDecodingError`。旧数值可能保留供诊断，**Bad 质量时不得作为当前值驱动自动化**。
- 每 500ms 检查过期，级联会监测会话并重连；断线不自动执行任何动作。默认过期阈值15秒，可配置；设备采样间隔较长时需相应调整。

## Linux / Docker

现有 Gateway 继续独立部署。可构建适配容器：

```sh
docker build -f deploy/opcua/Dockerfile -t fcs-opcua:preview .
```

Linux 可让适配容器使用 `--network host`，访问宿主的回环 Gateway 端口；配置和 PKI 挂载到可写 `/data`，只读挂载 Gateway 的令牌文件，并将 JSON 中路径改为容器路径。进程以镜像内非 root APP_UID 运行，目录应授予该 UID 所需权限。没有 Docker socket、USB 或 privileged 权限需求。不要将带令牌的 Gateway HTTP 改成公网来迁就容器网络。NAS 的主机网络支持、ARM64、持久权限与长时间运行需实机验收。

## 验证与依赖

```sh
dotnet run --project tests/FluentControl.FcsOpcUa.Tests -c Release
```

测试运行临时 HTTP Gateway、两个加密 UA Server 和 UA Client，验证数据、浏览、订阅、只读、证书拒绝、一级级联、过期与恢复。它不替代 Windows USB、真实 C3、NAS/Pi、第三方 UA 客户端和多日运行验收。

使用 OPC Foundation UA-.NETStandard 1.5.378.182（MIT），许可证在 `deploy/opcua/licenses`。FCS 自有代码继续适用仓库当前许可，第三方 SDK 不改许可。没有引入 Python OPC UA 运行时。

## English

This optional .NET 10 sidecar exports an explicit signal allowlist from the existing Windows same-user Gateway pipe or Linux loopback HTTP API. C3 retains `fcs-node/2`. Build with `dotnet publish`, edit the platform JSON example, run `--init`, exchange and verify public application certificates, then run `--config`. Relative paths resolve against the working directory. A self-contained publish needs no installed runtime.

Only Basic256Sha256 SignAndEncrypt is enabled. Trusted applications receive read-only access to all configured exports; no per-user ACL, device writes, executable methods, recovery, implicit cloud upload or automatic trust. Protect PKI private keys and limit network access. Endpoint hostnames are not a firewall. Restart after configuration/trust changes to terminate old sessions.

The namespace URI is `urn:fluentcontrol:fcs:opcua:1`; stable resource IDs include origin system/Gateway/device/sensor/signal. Browse, typed reads and real UA subscriptions are implemented; source acquisition polls the Gateway (2 seconds by default). `SourceTimestamp` is origin Gateway receipt time, never fabricated C3 UTC; mounts preserve it. Bad quality must not drive automation. Watchdogs invalidate stale samples, and connections retry without issuing device actions.

One-hop, explicitly selected mounts appear below Mounted systems. Imported resources cannot be recursively re-exported. Arbitrary-depth federation, geographic maps, service catalogs, historical UA access, complex value types and manager UI integration remain outside this preview. FC packaging and stable releases are unchanged. Linux x64 automated tests are separate from Windows USB, C3 hardware, NAS/ARM64 and third-party client acceptance. The OPC Foundation SDK retains its MIT license.
