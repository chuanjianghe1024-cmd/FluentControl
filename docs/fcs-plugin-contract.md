# FCS 插件协议初稿 / FCS Plugin Contract (Draft)

> 设计状态：插件运行、安装、规则无线下发和社区下载都尚未实现。此文档定义未来兼容边界；当前 `FcsGateway.exe` 的 `capabilities` 返回 `pluginRuntime: not-implemented`。任何软件不得将“协议预留”显示成“已安装插件”。

## 插件包与规则包分离

- **代码插件**（Python/MPY、Gateway .NET 扩展、专用驱动）：增加传感器、信号、事件处理器或动作。目标运行环境必须在清单中声明。**向设备端安装/更新可执行代码仅允许 USB 物理管理**；无线入口不能上传代码、固件或执行脚本。
- **声明式规则包**（JSON）：只引用目标设备已经注册并允许使用的 Signal、Event 与 Action，包含可校验的时间窗、比较、去抖、冷却、执行策略等。未来可通过授权的 LAN/BLE 管理通道下发；当前未实现。
- **固件基线与更新**：独立于插件安装系统；出厂重置应清除第三方插件和规则，恢复受信任的固件基线。必须保留 USB 烧录恢复手段。

## Manifest v1（草案，不可安装）

```json
{
  "manifestVersion": 1,
  "id": "community.example.light-wake",
  "version": "1.0.0",
  "displayName": "Study light wake",
  "type": "action",
  "target": {
    "runtime": "micropython",
    "boards": ["esp32-c3"],
    "minCore": "1.0.0"
  },
  "entrypoint": "plugin.py",
  "requires": {
    "signals": ["illuminance"],
    "capabilities": ["network.udp"]
  },
  "provides": {
    "actions": ["wol.send"],
    "events": []
  },
  "permissions": ["sensors.read", "network.udp.send"],
  "settings": {
    "macAddress": {"type":"string","format":"mac48","required":true}
  }
}
```

`id` 在同一发布者名下稳定，`version` 使用语义版本号，`runtime` 声明主控/环境，`requires` 用来做依赖检查。单台设备可包含多个 Sensor，一个 Sensor 可包含多个 Signal；规则明确引用 `deviceId/sensorId/signalId`，不得假设“一个设备只有一个光照”。

## 安装状态机（未来实现）

`discovered -> checked -> staged -> verified -> enabled`，失败进入 `rejected / failed / rollback`。安装必须核对来源、包摘要、签名状态（若有）、大小、平台、Core/API 兼容性、目标传感器/动作依赖、可用内存/Flash 与敏感权限。禁止将数据型规则解析成任意 Python/eval。提交更改前完整暂存，安全启动后再更新活动索引；掉电后回退旧版本。

设备端第三方 MicroPython 与 Core 共享解释器，**不是安全沙箱**。声明权限仅用于审核、提示和 API 约束，无法保证恶意插件不会操作底层硬件/文件；Gateway 侧可在未来使用独立子进程隔离可执行扩展。

## 崩溃、安全模式与恢复

- 捕获异常并记录有限长度的错误日志，重复故障禁用插件。
- 如果设备持续重启，启动恢复模式不加载第三方插件，保留 USB 诊断与卸载入口。
- 完全恢复出厂清除第三方代码、规则、Wi-Fi、管理密钥和临时历史；保留稳定设备身份与永久标签凭据。实际固件回滚取决于主板 Flash/固件布局，不能靠“删除目录”冒充恢复固件。
- 高采样信号只在 RAM 保留有界缓存，Flash 追加写入受频率和空间限制。

## FC / Gateway / 网站分工

- **Gateway**：设备发现、身份与能力聚合、USB 管理与数据访问；后续可提供插件目录查询、已安装状态、安装包元数据、规则校验和操作审计。当前只提供 `capabilities` 的能力标志，不提供插件安装 API。
- **FC**：展示插件列表、权限、兼容性和安装流程；在 Gateway 实现前只显示“预留”。
- **fctrl.app**：网站源码继续私有。用户发布内容区分 `官方` / `社区` 来源、`官方代码审核` / `指定硬件安装验证` / `未验证` 审核状态，以及 `公开` / `私有` 可见性。审核代码不等于验证硬件；未来发布条款单独约定内容授权与撤回。**是否支持付费插件尚未决定**，不得推出占位收费或支付按钮。
- 个人传感器历史与账号不是使用 Gateway 的前提；云同步默认关闭，未来只在用户明确授权的设备/信号上启用。
