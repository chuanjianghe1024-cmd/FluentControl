# 型号配置与网站接口

## 已实现的边界

分享对象是 `fluentcontrol.monitor-profile` v1 的纯数据。通用功能由 VCP 能力声明和读回探测决定；不会在探测时试写、重置或切换输入。目录之外的码保留为型号适配占位，尚未实现任意厂商私有码的写入。`IMonitorModelAdapter` 为后续受审核的代码适配器预留契约，目前没有加载器或厂商 SDK。

本机配置仍按实例标识保存，保留重命名和每屏映射；共享文件只记录 PnP 厂商/产品型号，例如 `DEL1234`，同型号的两块屏幕由独立 slot 区分。导入时用户明确绑定 slot 到当前屏幕。具体参数仍需在目标屏幕上重新校验，不因型号相同就假定固件/HDR模式完全一致。

```json
{
  "schema": "fluentcontrol.monitor-profile",
  "version": 1,
  "name": "办公",
  "monitors": [
    {
      "slot": "display-1",
      "modelId": "DEL1234",
      "values": { "brightness": 45, "contrast": 70, "gain-red": 50, "input": 15 },
      "brightness": { "enabled": true, "minimum": 10, "maximum": 90, "offset": 0, "curve": 1.2 }
    }
  ]
}
```

示例型号只用于说明。连续属性单位为 0–100%；输入等枚举保留标准原始数值；Gamma 使用已解析的 16 位指令值。旧版 temperature 表示 Windows 高层 API 的温度枚举，与 color-preset 的 VCP 枚举不同。缺失键表示不修改。重置、电源、OSD 按键控制、只读信息、未知码均不能分享；所有参数在导入预览和实际写入时再次按目标能力校验。文件上限 1 MiB、16 屏、每屏 64 项；不支持的 schema/version 被拒绝。

## 网站接入约定

`IMonitorProfileExchange` / `HttpMonitorProfileExchange` 已提供 HTTPS 下载与 JSON POST 上传函数。下载入口已接入 UI；上传尚未接入 UI，没有默认域名、自动上传或后台网络请求。

建议网站实现：

| 接口 | 用途 |
| --- | --- |
| `GET /api/v1/models/{modelId}/profiles` | 后续列表/搜索接口，客户端本版未接入 |
| `GET /api/v1/profiles/{id}` | 返回上面的纯 JSON，链接可直接粘贴到本版下载入口 |
| `POST /api/v1/profiles` | 接收上述 JSON；上传客户端将由后续显式“发布”操作调用 |

下载不跟随重定向，20 秒总超时和流式大小限制；分享链接应直接返回 JSON。上传登录、作者归属、审核、删除与版本管理交给网站后续定义；当前客户端未实现认证。服务端发布时仍应独立校验数据。共享配置不能携带命令、DLL、私有码脚本或本机可执行路径。

## 其余建议的实施顺序

1. 当前已实现多屏映射与完整硬件场景、已有快捷键/托盘菜单/桌面面板。
2. 应用联动建议按前台可执行文件的明确规则切换，保留进入前的状态快照；退出后恢复快照，若用户手动调节则停止自动恢复。自动规则不执行输入切换、电源与重置。当前仅为后续设计，未启用进程监控。
3. 定时调节建议使用同一场景执行队列，带时区、工作日和睡眠恢复规则；托盘滚轮建议作为可选开关并加节流。两者本版未实现，以免与应用联动或手动调节相互覆盖。
4. 型号库后续按厂商/产品 ID、固件与 MCCS 版本匹配，并通过随版本发布的受审核适配器扩展。厂商 SDK 需要另行验证许可与硬件。

## 协议参考

- [Windows CapabilitiesRequestAndCapabilitiesReply](https://learn.microsoft.com/en-us/windows/win32/api/lowlevelmonitorconfigurationapi/nf-lowlevelmonitorconfigurationapi-capabilitiesrequestandcapabilitiesreply)
- [VESA MCCS 2.2a 标准副本](https://milek7.pl/ddcbacklight/mccs.pdf)
- [Windows WM_MOUSEACTIVATE](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mouseactivate)
- [Windows SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)
