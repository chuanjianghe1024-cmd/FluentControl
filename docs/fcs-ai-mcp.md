# FCS Gateway MCP 接入（开发预览）

> 当前 Gateway 工程仍位于 FC 仓库；独立 FCS 打包和正式版网站分发尚未完成。本说明对应 `--mcp` 开发入口，不代表正式 FCS Release 或已完成硬件验收。

## 原理

`FluentControl.FcsGateway.exe --mcp` 启动本机 **MCP stdio** 适配器。MCP 不直接打开串口、扫描 LAN 或控制设备；它通过 **当前 Windows 用户专用** Named Pipe `FluentControl.FcsGateway.v1` 将请求交给唯一的后台 Gateway。若 Gateway 未启动，适配器尝试启动同目录 `FluentControl.FcsGateway.exe --serve`。无网页控制台、无外部 HTTP 监听、无云连接。

兼容 MCP JSON-RPC 请求：`initialize`、`notifications/initialized`、`ping`、`tools/list`、`tools/call`。JSON 请求与响应使用逐行 UTF-8 格式。支持本机支持 stdio MCP 的客户端；不暗示 ChatGPT 已自动连接本机服务，须用户在对应客户端显式配置。

示例客户端配置（将路径替换为实际安装位置）：

```json
{
  "mcpServers": {
    "fluentcontrol-sensors": {
      "command": "C:\\Program Files\\FluentControl\\FluentControl.FcsGateway.exe",
      "args": ["--mcp"]
    }
  }
}
```

不同 MCP 客户端的配置文件位置及字段可能不同，应遵循该客户端文档。运行时建议将相同版本的 `Gateway.exe` 用于 MCP 与后台 Gateway；旧版本混跑须先停止网关。

## MCP 工具

默认只读：

| Tool | 功能 |
| --- | --- |
| `fcs_list_devices` | 按稳定设备 ID 列出 USB/LAN 已知设备、状态与最近有效 lux |
| `fcs_device_capabilities` | 读取 Device/Sensor/Signal 清单。优先使用 LAN `/v1/capabilities`，USB 旧固件仅适配扁平信号 |
| `fcs_read_signal` | 指定 `deviceId`、`sensorId`、`signalId` 读取。**当前固件只支持 illuminance/lx**；其他信号不会伪装为已支持 |
| `fcs_device_status` | 通过 USB 读取经过脱敏的连接/无线/BLE 状态，不返回 bindingKey/setupPassword/密码 |
| `fcs_cloud_status` | 查看未启用的云扩展能力；当前不执行任何上传 |

可选设备写入仅在 MCP 进程环境变量 `FCS_MCP_ENABLE_WRITES=1` 时出现在工具列表。工具为 `fcs_set_device_setting`，只接受 `wireless.enable`、`wireless.disable`、`ble.enable`、`ble.disable`、`ap.start`、`ap.stop`，同时必须附 `confirmed: true`。真实设备必须通过 USB 身份验证；LAN 不允许写入。**不提供**恢复出厂、Wi-Fi 密码设置、绑定密钥轮换、设备端代码安装、任意串口命令或云上传给 MCP。

请只在有操作确认提示的可信 MCP 客户端中启用写入；`confirmed: true` 只是防误触标志，不是强认证机制或人类确认的替代。AI 工具不应绕过现有设备权限与本地用户权限隔离。

## 当前限制

- 本地 MCP 与 FCS IPC 的初版实现需 Windows CI、真实 MCP 客户端、C3 和 USB 串口逐项验证。CI 逻辑通过不等于实机验收。
- 第一版 `signal.read` 只消费已有 `illuminance`，多传感器协议尚需新增固件信号采集路由和 USB 能力模型。
- 无 MCP 订阅/推送流；目前是按需查询。大模型频繁查询可能竞争 USB 串口，不应当作无限速率流。
- 不从局域网直接开启管理接口；用户授权的局域网公开只读数据仍按现有 `fc-device/1` 协议处理。
- 云接口仅有能力与数据契约预留，**没有**账户、上传、存储、云规则执行或云同步。

相关说明：FCS 仓库 `docs/cloud-contract.md` 与 `docs/release-policy.md`。
