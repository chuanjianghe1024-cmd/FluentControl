# 官网与 Windows 发布者

官方域名为 https://fctrl.app。应用设置、MSI 的“有关信息”和签名的描述链接使用该地址；网站、DNS、HTTPS 及配置分享后台尚需单独部署。本仓库没有配置域名解析或自动上传。

## 当前状态

普通 GitHub Actions 构建未签名。品牌名称 `FluentControl`、MSI Manufacturer 和文件 Company 只是产品元数据，不代表 Windows 已验证的发布者。

“发布者未知”需要给应用及 MSI 添加有效的 Authenticode 代码签名。官网域名和 HTTPS 证书不能替代代码签名证书，自签名证书也不会自动获得其他用户的 Windows 信任。发布者名称来自证书中经过身份验证的个人或组织，不能任意改成域名。

SmartScreen 还会检查应用信誉；有效签名不保证新版本立即消除所有提示。不要通过关闭 Windows 防护、导入自签名根证书或修改安全策略来分发应用。

## 已准备的签名入口

在 Windows 签名机器上准备受信任 CA 颁发的有效代码签名证书、供应商要求的硬件令牌/HSM 或密钥提供程序、Windows SDK SignTool，以及供应商提供的 RFC 3161 时间戳地址。证书在 Personal / My 存储区中，私钥可以由硬件提供，不需要导出。

先干净发布应用，再执行：

```powershell
dotnet publish src/FluentControl/FluentControl.csproj -c Release -p:PublishProfile=Installer -p:Platform=x64 -p:Version=0.3.100 -o publish
./scripts/Build-Msi.ps1 -Version 0.3.100 -RequireSigned -CertificateThumbprint '<证书的 40 位指纹>' -TimestampUrl '<供应商的 RFC 3161 地址>'
```

默认使用 `Cert:\CurrentUser\My`，机器证书使用 `-MachineStore`，也可指定 `-SignToolPath`。证书指纹是公开标识；不要把私钥、PFX、密码或令牌 PIN 提交仓库或发到聊天中。云签名服务不暴露 Windows 证书存储时，需要接入对应提供方，当前脚本不假装已支持其认证。

流程顺序：验证证书和签名工具 → 签 `FluentControl.exe`、`FluentControl.dll` → 为签后的文件生成清单并打包 MSI → 签 MSI → 验证证书指纹、签名及时间戳 → 生成最终大小和 SHA-256。使用 SHA-256 摘要及时间戳摘要，网站描述地址为 https://fctrl.app；不会重签第三方依赖。显式要求签名时缺少证书、链验证失败或时间戳失败均终止，不回退到未签名发布。

`artifacts/obj/msi-<版本>/signatures.json` 如实记录 EXE、DLL 和 MSI 的状态；构建摘要也显示 MSI 状态。正常 CI 尚未连接签名身份；目前只验证未签名打包与脚本语法，真实证书签名流程须在配置证书后完成验收。验收时使用干净 Windows 验证签名链及实际安装，不以签名机器上自定义的根信任作为公众受信任的证据。

## 微软说明

- [SignTool 参数、时间戳和签名验证](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)
- [Smart App Control 代码签名要求](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control)
- [SmartScreen 检查应用与下载信誉](https://learn.microsoft.com/en-us/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/)
