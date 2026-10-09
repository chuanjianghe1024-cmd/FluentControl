# 型号配置与网站接口

## 批量配置 v2

分享文件采用 `fluentcontrol.monitor-bundle` v2，包含多个分组与多套场景；读取兼容旧 `fluentcontrol.monitor-profile` v1。导出来源是**型号配置库**，可导出全部型号或指定型号的全部预设；同型号自动共用分组。JSON 使用 UTF-8 可读 Unicode。

```json
{
  "schema": "fluentcontrol.monitor-bundle",
  "version": 2,
  "name": "我的办公与游戏配置",
  "groups": [{
    "name": "本地 / 默认分组",
    "profiles": [{
      "name": "修图",
      "applications": ["Photoshop"],
      "monitors": [{
        "slot": "display-1",
        "modelId": "DEL1234",
        "modelName": "示例型号",
        "displayName": "左屏",
        "brand": "Dell",
        "values": {"brightness": 45, "contrast": 70, "gain-red": 50, "input": 15},
        "brightness": {"enabled": true, "minimum": 10, "maximum": 90, "offset": 0, "curve": 1.2}
      }]
    }, {
      "name": "夜间游戏",
      "applications": ["示例游戏"],
      "monitors": [{
        "slot": "display-1",
        "modelId": "DEL1234",
        "modelName": "示例型号",
        "displayName": "左屏",
        "brand": "Dell",
        "values": {"brightness": 20, "contrast": 60}
      }]
    }]
  }]
}
```

示例型号只用于说明。`modelId` 是 PnP 厂商/产品码，不是设备实例或序列号；`modelName` 优先取 Windows DisplayConfig/EDID 友好名称，无法读取时保留驱动名称，可在信息页手动补充。品牌来自已知 PnP 厂商映射，未知值保留厂商码，可编辑。别名、场景名和应用标签属于用户填写的共享元数据。

同一物理屏幕在整个文件使用稳定 slot，多个同型号屏幕使用不同 slot。新导入流程不绑定物理设备，按型号入库，文件名作为本地来源记录。相同名称与内容去重，同名不同内容加序号；离线型号仍可入库。应用时可选任意目标屏幕，按实际参数与选项取交集；跨型号使用后提示另存到目标型号组。旧批量导入解析保留兼容，导入操作不向硬件写入。

连续属性单位为 0–100%；输入等枚举保留原始值；Gamma 使用已解析的 16 位指令值。`temperature` 是 Windows 高层 API 枚举，与 VCP `color-preset` 不同。缺失键表示不修改。只读信息、重置、电源、OSD 按键控制和未知码不分享。保存前及应用前校验当前允许的选项：显示器报告的当前色温值不等于可设置选项，不可重放的值不保存，旧配置中的无效选项跳过并报告数量。

主分支色温修复保留旧文件的 `temperature` 编码；当设备已有可写 `color-preset` 时，旧值通过显式映射调用同一个 VCP 通道，界面与新快照只使用规范的 `color-preset`。同一场景两者都有且 VCP 值可用时以 VCP 为准，避免旧重复项覆盖。原始 VCP 读回高字节的容差信息不作为预设编号；持续读回不同值会报告实际结果，不把选择值伪装成硬件状态。

限制：1 MiB、最多 128 组 / 512 场景，每场景最多 16 屏，每屏 64 项；名称 80 字符、型号名称 128 字符、应用标签最多 32 个，每个 80 字符。不支持的 schema/version 被拒绝。型号相同也重新按本机能力校验，不假设固件、HDR 或显示模式相同。

## 显示器能力元数据（v2 可选扩展）

每个 monitor 新增可选 `hardware`，仍兼容未携带该字段的 v1/v2 文件。上传、下载、离线导入和再次导出均保留它；已保存场景在发现设备时补充能力快照，不额外增加 DDC 查询。元数据不参与硬件写入授权，应用配置始终重新检查目标的实时能力。

```json
"hardware": {
  "version": 1,
  "manufacturerCode": "DEL",
  "firmware": "1.3",
  "mccsVersion": "2.2",
  "controls": [{
    "key": "brightness", "vcpCode": 16, "kind": "continuous",
    "advertised": true, "readable": true, "writable": true,
    "minimum": 0, "maximum": 100, "options": []
  }]
}
```

`advertised` 表示能力字符串声明；`readable` 表示本次成功读取；`writable` 表示 FC 本次建立了经过范围/选项校验的控制通道，并不保证某个硬件命令必然成功。`kind` 为 continuous/choice/gamma/read-only/action；连续范围使用 FC 的 0–100 标准化单位，离散选项是实际命令值，不是翻译后的文字。最多 256 个控制项、每项最多 256 个选项；未知厂商码只记录声明，不据此生成写入功能。固件/MCCS 只接收数值版本。完整 EDID、原始能力字符串、序列号、连接路径和设备实例 ID 不导出。整包仍受 1 MiB 限制。

网站可按同一屏幕的型号、品牌和可写参数集合组合筛选。参数相似度定义为可写参数集合的 Jaccard 交并比；它是功能相似度，不代表画质、数值范围、离散选项或固件完全兼容。没有能力快照的旧文件标为未知，不能假装成 100% 匹配。读取能力失败时保留上次记录；离线屏幕仍可分享上次保存的信息。

## FC 屏幕菜单与原厂 OSD

显示器卡片及整体控制页提供每块屏幕的“屏幕菜单”，在对应屏幕打开独立 FC 软件菜单，支持滚动、调整大小、Esc 关闭，使用现有的真实 DDC 通道和选项。刷新设备/退出应用时关闭窗口，关闭时取消尚未执行的滑动操作；输入、电源和 OSD 按键设置继续确认，恢复出厂设置保留在主面板。

这是 FC 软件菜单，不是原厂 OSD 画面的捕获或复制。标准 `0xCA` 控制原厂菜单/按键的启用状态，`0xCC` 控制支持的菜单语言；启用菜单不等于弹出菜单。`0x03` 为物理按钮事件队列，不作为模拟方向键写入。原厂菜单唤出和导航仍需经过验证的型号适配或厂商 SDK，当前显示为未接入，不能从共享文件加载可执行控制逻辑。

## 查询维度与网站接入状态

客户端已支持应用/游戏、型号（名称或 PnP 码）、品牌的单条件或多条件筛选。非空维度按 AND 组合、文本不区分大小写；多个屏幕的场景中，型号和品牌必须匹配同一个屏幕。应用标签只用于信息与筛选，**不会自动启动程序或切换配置**。

`IMonitorProfileExchange` / `HttpMonitorProfileExchange` 支持 HTTPS 下载和 JSON POST 批量上传。下载入口已接入 UI；上传按钮与网站登录尚未接入，计划官网及 API 域名为 `https://fctrl.app`。独立网站代码位于 [FluentControl-Web](https://github.com/chuanjianghe1024-cmd/FluentControl-Web)，已实现配置查询、认证和作者归属等服务端逻辑，正式部署及登录密钥配置仍单独进行。桌面客户端不会自动请求该域名，没有自动上传或后台网络请求。基础接口如下（部署后相对此域名，完整服务端行为以网站仓库为准）：

| 接口 | 用途 |
| --- | --- |
| `GET /api/v2/profiles?application=Photoshop&modelId=DEL1234&brand=Dell` | 一个或多个可选维度 AND 筛选，返回分页摘要 |
| `GET /api/v2/profiles/{id}` | 返回完整 v2 JSON；可直接粘贴此 HTTPS 链接下载 |
| `POST /api/v2/profiles` | 接收 v2 JSON，需要网站认证；桌面端发布按钮尚未接入 |

服务端索引 `applications[]` 和每个 monitor 的 `modelId/modelName/brand`；以 bundle 或场景为搜索结果均须保留所属 bundle 与 scene 标识，避免跨屏错误匹配。下载不跟随重定向，20 秒超时，流式大小限制。认证、作者归属、删除、分页及版本管理由独立网站负责，服务端独立校验；审核与运营策略不能用客户端校验代替。共享文件是纯数据，不携带命令、DLL、私有码脚本或可执行路径。

通用功能只读探测，联动取所有已连接屏幕能力和选项的并集（含不同型号），写入仅针对支持的屏幕。`IMonitorModelAdapter` 为随应用审核发布的适配器预留，尚无动态加载器或厂商 SDK。

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

## 总配置与型号预设的本地关系

`UserState.Profiles` 是总配置，保存全部可读取的监视器/音频/鼠标参数；`MonitorPresets` 是按型号分组的可复用显示器预设。总配置的 `MonitorPresetIds` 只记录来源，写入使用总配置自己的 `Values` 快照；修改/删除库项目不改变已有快照。保存总配置自动复用相同型号、参数及映射的预设，否则新增；每个屏幕独立绑定，可引用同一预设。失联设备保留在快照中。

本地组织迁移版本 `ProfileOrganizationVersion=1`，首次升级先保存原始备份再拆分旧场景的各屏参数。旧组作为来源数据保留，界面总配置列表与切换统一跨组；不再把文件分组当作型号分组。

共享 `SharedScene` 新增可选 `scenario`（场景/应用，80 字符）、`summary`（短简介，160 字符），与 `applications` 一起保留。型号预设名建议“型号 - 场景/应用 - 短简介”，不强制覆盖自定义名称。公共 API 只接受监视器库数据，总配置中音频、鼠标和设备路径不公开。
