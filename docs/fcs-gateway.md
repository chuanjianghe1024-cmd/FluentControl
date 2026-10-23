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
