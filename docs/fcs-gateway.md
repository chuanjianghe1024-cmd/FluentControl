# FCS Gateway 最小运行版

## 当前实现

- 新独立项目 `src/FluentControl.FcsGateway/`，Windows x64/.NET 8，可单独运行的后台进程。
- 同一个 Windows 用户最多运行一个 Gateway（mutex）；通过本机 Named Pipe `FluentControl.FcsGateway.v1` 通信，并启用 CurrentUserOnly。
- Gateway 通过 UDP 42735 发现兼容设备，按固定 `http://发现来源IP:8766/v1/sample` 验证读取；只接受私有 IPv4 地址、`fc-device/1` 及合法 deviceId、lux/有效性数据。不会根据未认证广播内容执行管理命令。
- 周期刷新，保留上次成功时间和最近有效 lux；12 秒没有成功数据标记离线，离线不将旧值显示为实时。
- IPC 操作：`status`（进程状态）、`list`（设备快照）、`discover`（主动扫描 + 当前快照）。按请求一行 JSON/响应一行 JSON。
- 既有 FC 光照读取仍保留直连兼容；Gateway 本身不修改设备网络配置、插拔 USB 或改变自动亮度。
- `--self-test` 为纯本地格式与安全边界回归；它不是 ESP32-C3 实机测试。

## 下一阶段 / Not yet implemented

- 完整规则引擎、插件代码加载、设备端刷写、BLE 配网、云同步和独立图形管理器均未交付。
- 公开只读 LAN 设备要求升级到新 C3 固件；旧的签名固件不能被 Gateway 匿名读取。FC 已保存的签名订阅仍按旧流程工作。
- 独立 Gateway 当前以可执行文件形式提供，FC 默认安装包会捆绑它；更灵活的独立 MSI 安装器仍待专门验证。
- 只有在用户已登录、且运行 Gateway 的 Windows 电脑保持唤醒时，后台扫描才会继续。该进程不会替代可 24 小时独立运行的 C3 规则引擎。

## 配网入口状态

首次配网的 C3 SoftAP 网页（FCS-xxxxxx / http://192.168.4.1）与 FC 原有 USB Wi-Fi 配网向导已提供，Gateway 是配网成功后用于 LAN 发现及实时光照验证的组件。BLE 当前仅有原有认证读取功能，**BLE 直接提交 Wi-Fi 密码的安全配网通道尚未实现**；不允许为了实现此入口而让 Wi-Fi 密码在未经加密的 BLE 特征值中传输。独立图形控制台、无线规则下发及安装管理也仍待实现。

## 生命周期与安装升级

FC 安装包默认包含网关可执行文件，但只有用户打开 FCS 设备页面并需要发现时才启动后台进程，避免单纯启动 FC 就永久打开扫描服务；进程可以在 FC 窗口关闭后继续运行。独立使用可运行 `FluentControl.FcsGateway.exe --serve`，退出/升级前可显式运行 `FluentControl.FcsGateway.exe --stop`；停止请求只允许同一 Windows 用户经本机 IPC 发送。卸载与升级前应先关闭 FC，再停止该进程，避免 MSI 的文件占用提示。当前还没有独立 MSI 安装器或可选系统服务。

## v1 USB 管理集中到 Gateway（本次迭代）

FCS Gateway 现在拥有 USB CDC 的端口发现、物理设备身份握手、光照 READ 和经过白名单的管理 FC 命令。FC 配网向导、设备管理和生产模式的 USB 订阅通过本机 Named Pipe 调用 Gateway；原有 FC 串口实现暂作回归测试与迁移兼容参考，不再是这些入口的主路径。

IPC 操作：
- `usb.discover`：`port=AUTO|COMx`，返回已验证 `{port, identity}` 列表；
- `usb.manage`：提供 `deviceId`、`port`、已知管理 `command` 与对象 `arguments`；Gateway 必须重新 PING 核对身份后才通过物理 USB 发送；
- `sample.read`：按稳定设备 ID 优先 USB READ；USB 不可用时尝试该身份在 Gateway 发现并读取验证的公开 LAN 来源；不假设仅拔掉数据线就能保持光照不中断；
- `list` / `discover`：报告 USB/LAN 去重设备与有效性、最近成功读数、过期状态；`capabilities`：返回目前支持的传输与管理范围，以及未实现的插件状态。

若 Gateway 是独立启动，本机 IPC 仍仅允许同一 Windows 用户；这是单用户管理信任边界。返回 USB 绑定密钥仅允许同用户本机请求，不通过公开 LAN 接口返回。私有网络中的公开传感器接口仅用于读取，仍不提供管理写入。

**当前局限**：没有独立 MSI 安装器（Gateway 二进制可独立执行），当前源码暂在 FC 解决方案中并捆绑 FC 安装包；计划在稳定协议和 CI 验证后抽出可复用 FCS 独立工程与发行包。旧签名 LAN 客户端保留兼容读取，不能由 Gateway 的新匿名读取冒充验证通过。BLE Wi-Fi 安全配网、插件安装/规则引擎仍未实现，参见 [插件清单设计](fcs-plugin-contract.md)。

## 独立部署（便携预览）

Windows build 成功通过完整验证后额外上传 `FCS-Gateway-x64` Artifact，内含 Gateway 独立自包含 .NET 8 运行时、依赖和配置文件。解压后可执行 `FluentControl.FcsGateway.exe --serve`；关闭前使用 `--stop`，或在控制台按 Ctrl+C。无需安装 FC、网站登录或网关级管理员权限。**这是便携预览，不是独立 MSI/Setup 安装器**；目前仅提供 Named Pipe 控制面，没有独立桌面 UI。一个用户同时运行 FC 捆绑的 Gateway 与独立版时，同名互斥锁保证只能有一个实例；请避免版本不一致，切换版本先执行 `--stop`。
