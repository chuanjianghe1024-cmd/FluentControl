# 自动亮度、时间主题与光照数据订阅

设置 → 自动调节预设。默认关闭；选中一台目标屏幕后显式启用，会实际调节其亮度。启用状态在本机保存，FC 下次运行时继续规则。关闭后保留当前屏幕亮度，FC 主题恢复手动偏好。当前只有一条活动规则/一个目标屏幕，不是每屏同时独立运行。

## 三种调节依据

- **光照强度**：订阅一个不断返回最新照度的 HTTP/HTTPS 链接，使用本地 lux→亮度曲线计算目标。USB 硬件可以经本机桥接服务提供 URL，Wi-Fi 硬件可以直接提供 URL，第三方设备只需实现下面同一份 JSON 协议。
- **指定时间**：独立的 24 小时亮度折线，以所选时区的当前时间插值；可以指定 FC 浅色、深色主题开始时间。小数小时如 7.5 表示 07:30，支持跨午夜主题区间。0/24 时亮度相等，避免午夜跳变。
- **日出日落**：选择地区快捷填入，或输入经纬度与时区；本地估算每天日出日落，分别设置日间、夜间亮度，同步 FC 浅色/深色主题。考虑闰年、时区规则/夏令时、极昼与极夜。地形遮挡、天气和高纬折射不建模，时间是近似值。

两条曲线分别保存，切换不会重置。图表支持点击添加、拖动节点、数值编辑与 JSON 导入导出；导出文件只是曲线，不是光照数据来源。图表模拟输入只预览，不写屏幕；但选择“内置模拟光照”作为活动数据来源并打开自动调节，会实际写屏幕。

**主题仅控制 FC 自身，不改变 Windows 系统/其他应用主题，也不调整 Gamma 或色温。** 手动在“常规”选择主题会关闭自动主题切换。当前配置不包含用户偏好学习；USB/BLE 驱动和固件需待硬件验证后接入。

## 统一光照数据格式 v1

链接每次 GET 返回最新一帧（JSON 原始内容，不是 HTML 页面）：

```json
{
  "format": "fluentcontrol.light-sample",
  "version": 1,
  "sourceId": "my-light-sensor-01",
  "timestamp": "2026-10-14T06:00:00Z",
  "lux": 123.4,
  "valid": true,
  "simulated": false
}
```

- 真实设备和模拟源使用同一结构；示例时间不可直接长期使用，服务需要实时更新。
- `sourceId`：非空稳定 ID，最长 100 字符；不需要显示器型号或 FC 私有代码。
- `timestamp`：带 UTC/偏移的采样时间；客户端检查过期、乱序和未来时间。保持设备时钟同步。重复时间戳不增加滤波样本数。
- `lux`：有限数值 0–200000；曲线目前覆盖 0–10000 lux，超出曲线范围按端点限制。缺少 lux、null、负值不当作黑暗。
- `valid`：测量是否成功；false 时 FC 暂停本轮，不下发亮度。
- `simulated`：模拟标记，界面显示来源与标记；它不改变数据处理协议。
- 不包含命令、脚本、DLL、私有 VCP 指令或自动执行动作。字段白名单、版本校验、16 KiB 上限；不跟随重定向、不使用 Cookie 或嵌入用户名密码。HTTP 用于用户明确添加的本机/LAN 接口；公网建议 HTTPS。

新增来源不会联网探测或自动启用，先保存名称与 URL，再由用户开启。只轮询一个活动来源，不会扫描局域网。新增/删除来源会关闭自动调节，避免无意切换到模拟输出。目标屏幕和经纬度不发送给来源；GET 请求仅访问用户提供的地址。

## 采样与屏幕写入策略

- 默认订阅间隔 **1000ms（1Hz）**，允许 500–60000ms；本机/局域网优先 1s，公网低频来源可用 5–30s，取决于服务频率与配额。
- BH1750 高分辨率测量典型 120ms、最长 180ms（默认测量时间设置），1Hz 是本项目的初始工程选择，不是传感器强制频率。来源采样、HTTP 轮询、DDC 写入是三个不同频率。
- 三个新样本取中值，随后 EMA 系数 0.25 平滑；短时单点遮挡/尖峰不会直接决定输出。
- 过期阈值为 max(5s, 3×订阅间隔)，未来超过 5s 拒绝；网络单次超时 3s、不重叠请求；异常数据/连接失败暂停输出，故障重试退避约 10s（手动取消除外）。不把失联转换成 0 lux。
- 每屏自动操作最多每 2s 一次；与目标差异不足 2 个百分点不写；单次最多推进 5 个百分点，实际值以驱动支持档位和回读为准。
- 手动亮度操作触发 **10 分钟保持期**，自动输出不会触发自己的保持期；检测到外部读值偏离上次回读也进入保持。手动关闭再开启可以解除保持。
- 写入失败暂停自动执行，需重新启用，不持续重写失败命令。HDR 开启/未知或显示器离线时暂停亮度；按时主题不依赖显示器亮度是否可写。
- 使用既有 gate/generation 通道；刷新、关闭、换来源、改规则使旧请求失效；睡眠/恢复或时钟跳变丢弃旧光照样本，不追放睡眠期间计划。

## 无硬件测试

安装 Python 3 后在仓库根目录运行：

```powershell
python tools/light-source/server.py
```

在 FC 添加 `http://127.0.0.1:8765/light.json`。服务器只绑定本机回环地址，默认每分钟循环从暗到亮再变暗。编辑 `tools/light-source/pattern.json` 的 seconds/lux 节点后重启服务器；也可指定 `--pattern 路径 --port 8765`。这只是模拟来源，不冒充已经完成 USB 串口桥接。真实硬件以后只需替换生成 lux 的部分并继续返回相同结构。

## 参考与验证

- [ROHM BH1750 数据手册](https://www.mouser.com/datasheet/2/348/bh1750fvi-e-186247.pdf)，测量时间见 Electrical Characteristics。
- [NOAA 通用太阳位置公式](https://www.gml.noaa.gov/grad/solcalc/solareqns.PDF)，按公开公式独立实现日出日落估算，不调用网站运行时服务。
- 纯逻辑回归覆盖格式、时间新鲜度、尖峰/重复采样、对数插值、跨午夜、极昼极夜、输出步幅、手动/自动通知隔离。现有显示器/音频/配置回归通过。2026-10-14：Linux .NET 8 纯逻辑回归、C# 12 语法解析及本机模拟 HTTP 端点实测通过。Windows 构建 #129 已通过完整 WinUI、模拟界面/调度、MSI 与 Setup 验收；包含曲线编辑持久化、午夜衔接、百分比步幅、写入间隔、手动保持和关闭门控。实体传感器、显示器与休眠恢复实测仍待完成。

# Automatic brightness and light-data subscriptions

Three independent modes: illuminance from a live HTTP(S) JSON feed, a fixed 24-hour brightness curve, or locally estimated sunrise/sunset using coordinates and a time zone. The two curves retain separate edits. Optional light/dark switching affects FC only, not Windows. The default is off; explicitly enabling applies real brightness changes to one chosen display and persists across app restarts.

Real and simulated providers return the same `fluentcontrol.light-sample` document. Use `tools/light-source/server.py` with the configurable pattern for a local test endpoint. Default polling is 1Hz; a three-sample median and EMA smooth readings, writes are limited to one per 2 seconds with a 2-point deadband and 5-point step limit. Manual changes hold automation for 10 minutes. Invalid/stale data, unavailable displays and HDR pause brightness; write failures require re-enabling. Hardware USB/BLE bridges and preference learning remain future work.
