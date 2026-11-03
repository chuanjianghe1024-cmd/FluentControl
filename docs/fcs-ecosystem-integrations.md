# FCS ecosystem adapters and execution placement

Research/decision date: 2026-10-26. This records ecosystem direction and a Home Assistant configuration example; it does **not** claim Xiaomi certification, a cloud service, or physical ecosystem acceptance.

## 中文：职责与接入结论

C3 的默认核心职责保持为稳定采集、身份/能力发现和既定 `fcs-node/2` 查询。Gateway 承担跨设备规则、外部生态适配、云上传、历史与执行记录；FC 负责配置界面和本机显示控制。本机 Windows、NAS/Pi/Linux 的 Gateway 共用当前的受限脚本核心。第三方扩展不得变成基本传感器查询的必需依赖。

C3 允许通过可选插件增加轻量动作，例如 WoL 和本机阈值规则。它们必须由已有插件清单表达运行位置及权限，按已有 USB 物理管理边界安装。没有安装插件时，C3 仍可正常被查询；插件失败、云端断网不能拖垮查询。MicroPython 插件与 Core 共用解释器，不是安全沙箱，因此还需要耗时/内存预算、故障禁用与不加载插件的恢复启动路径。

**电脑上的 Gateway 不能在电脑已关机时唤醒自己。** 自动唤醒必须由另一台常驻主机，或仍有供电和网络的 C3 插件发出。发送魔术包成功只代表已发送，不代表目标电脑已经启动；是否能唤醒还取决于网卡、固件电源配置和网络路径。

| 目标 | 当前数据能否承载 | 需要补齐的接入层 |
| --- | --- | --- |
| Home Assistant | 可以映射；光照值、lx、稳定 ID、有效性和缓存年龄已有 | 当前提供 REST 示例；后续 Gateway MQTT Discovery 适配器 |
| Apple 家庭 / HomeKit | 可通过 HA 的 HomeKit Bridge 映射为 LightSensor | HA 配对、实体选择、mDNS/网络配置；也可后续开发独立 Homebridge 适配器 |
| 与已有米家设备联动 | 可以经 HA 规则同时使用 FCS 和支持的小米设备 | FCS 进入 HA，小米官方 Xiaomi Home 集成导入已有米家设备；实际支持依品类/地区/控制路径而定 |
| FCS 设备直接出现在米家 App | 现有数据语义可做映射，但当前不具备原生接入资格及实现 | 正式 MIoT 产品模型、平台注册/接入、身份认证、上线测试等；不能靠修改 JSON 变成已接入设备 |

HomeKit 是 Apple 的生态/协议；Home Assistant、Homebridge 是可以桥接到它的开源软件。“与米家设备联动”和“把 FCS 设备导入米家 App”必须在界面与文档中区分。小米官方 `xiaomi_home` 集成是把米家设备导入 HA，不能将它宣传成任意 HA 设备导回米家 App 的桥。

### 数据映射

保持内部数据格式。每个适配器读取受校验的标准信号，转换外部格式：

| FCS | HA / 后续 MQTT Discovery | 边界 |
| --- | --- | --- |
| `deviceId` | device identifiers | 不使用 IP 作为永久身份 |
| `deviceId + sensorId + signalId` | unique_id | 同一设备多个传感器不得合并成一个实体 |
| `illuminance`、`number`、`lx` | `device_class: illuminance`、`state_class: measurement`、单位 `lx` | 光照不是灯的亮度百分比 |
| `valid`、`fresh`、Gateway 缓存年龄 | available/unavailable | 无效、重复身份或超时不得变成 0 lux，不从历史恢复为新样本 |
| 硬件/驱动能力清单 | 已存在的设备与实体 | 未安装温湿度芯片不得发布虚构温湿度实体 |
| 采样时间与 sequence/uptime | 记录属性、去重 | 不把收到网络响应的时间冒充原始设备采样时间 |

MIoT 属性使用产品对应的服务/属性实例标识（siid/piid）及格式、单位、范围、访问权限。不能把 FCS `sensorId` 直接当 siid，不能照抄另一产品的实例编号。小米官方 HA 转换代码把 `illumination` 映射为 `ILLUMINANCE`/`LIGHT_LUX`，支持数据语义能够转换的判断；这不证明我们的设备已通过 MIoT 合规验证。

### 现有 API 的 HA 接入示例

[REST 配置示例](examples/fcs-home-assistant.yaml) 只读取 Gateway 的受认证 `/rpc`，不要求更改 C3。替换物理 deviceId 与网关地址，将完整 `Bearer ...` 保存到 HA 的 `secrets.yaml`，按 HA 流程检查配置并重启。若已有 `sensor:`，合并列表，不覆盖原配置。默认 HTTPS 并校验证书；网关部署见 [host guide](fcs-gateway-host.md)。

示例按完整信号身份选择唯一条目，并检查身份、协议、单位、valid/fresh、数值范围及缓存年龄。缓存最多 5 秒、HTTP 超时 5 秒；断线或无效数据应为 unavailable。它是开发示例，模板离线回归不等于已完成真实 HA/Apple 家庭配对验收。接入后在 HA 的 HomeKit Bridge 中选择生成的光照实体；实际 entity_id 以 HA 界面为准。跨容器/网段时还要配置 HomeKit 所需的 mDNS 与连接端口。

### 当前实现与后续边界

本次开发版已实现 C3 可信 MicroPython 插件/USB 安装/回滚/安全模式、阈值规则，以及 Windows/Linux 共用的受限 Python 脚本、WoL 和显式 HTTPS 动作。格式、限制、创建/验证/干运行步骤以 [插件与脚本首版](fcs-plugin-contract.md) 为准。HA 首版采用上述 REST 示例；MQTT Discovery、原生米家认证、Matter Bridge、云账户和离线遥测补传仍未实现。

## English

Keep the C3 core focused on reliable collection, identity/capability discovery and the existing node API. Put cross-device rules, ecosystem adapters, history, cloud export and execution records in the Gateway, with FC as the configuration/display client. Optional lightweight C3 actions remain possible via the existing USB-installed plugin design; they must not become a dependency of basic queries.

A Gateway running on a powered-off PC cannot wake that PC. An always-on NAS/Pi or a powered C3 must send the magic packet. Report packet transmission separately from confirmed computer availability. Every rule needs one explicit executor to prevent duplicate actions, plus freshness checks, hysteresis, debounce and cooldown. Do not replay queued actions after reconnecting.

FCS illuminance data can map to HA sensors and, through HomeKit Bridge, Apple Home light sensors. The included REST example uses the existing authenticated Gateway API; it is not proof of real HA/HomeKit pairing. MQTT discovery is a future Gateway adapter. Xiaomi Home can import supported Xiaomi devices into HA for joint automation; that does not export arbitrary FCS/HA devices into the Xiaomi Home app. Native Xiaomi access requires a separate MIoT product/onboarding/authentication path. HomeKit is Apple's ecosystem; HA and Homebridge are open-source bridging implementations.

Cloud export stays opt-in and separate from local queries. Future adapters need explicit destinations/signals, protected credentials, TLS, bounded queues and retention policy. No cloud credentials, account enrollment, live uploads, plugin installations or wake packets are performed by this documentation/example.

## Primary sources checked

- [Xiaomi: MIoT Spec](https://iot.mi.com/v2/new/doc/introduction/knowledge/spec)
- [Xiaomi: create a product](https://iot.mi.com/new/doc/accesses/direct-access/productcenter/create-product)
- [Xiaomi: direct access overview](https://iot.mi.com/new/doc/accesses/direct-access/overview)
- [Xiaomi official HA integration](https://github.com/XiaoMi/ha_xiaomi_home/blob/main/doc/README_zh.md)
- [Xiaomi official MIoT-to-entity mapping](https://github.com/XiaoMi/ha_xiaomi_home/blob/main/custom_components/xiaomi_home/miot/specs/specv2entity.py)
- [Xiaomi integration: HA-to-Mi Home boundary](https://github.com/XiaoMi/ha_xiaomi_home/discussions/970)
- [HA REST sensor](https://www.home-assistant.io/integrations/sensor.rest/)
- [HA MQTT discovery](https://www.home-assistant.io/integrations/mqtt/)
- [HA MQTT sensor expiry and retained-state warning](https://www.home-assistant.io/integrations/sensor.mqtt/)
- [HA HomeKit Bridge supported sensors and networking](https://www.home-assistant.io/integrations/homekit/)
- [HA Wake on LAN](https://www.home-assistant.io/integrations/wake_on_lan/)
