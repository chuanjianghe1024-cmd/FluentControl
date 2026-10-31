# 许可策略与迁移说明 / Licensing policy

2026-10-31 起，本次许可切换提交及后续采用新许可的版本，使用 [个人非商业使用许可 1.0](../LICENSE)。仅允许自然人个人非商业自用；商业项目集成、企业内部业务、客户用途、收费服务、软件/固件/实体销售和独立再分发均未获授权。向官方贡献的有限 fork/PR 例外见 LICENSE。不是 OSI 开源许可，不再使用 PolyForm 名称描述当前许可。

From the licensing-change commit on 2026-10-31, versions adopting it use the [Personal Noncommercial Use License 1.0](../LICENSE). Only an individual's own noncommercial use is permitted. Commercial integration, internal business operations, customer use, paid services, sales and independent redistribution are not granted. LICENSE provides a limited upstream fork/PR exception. This is not an OSI open-source license and must not be described as PolyForm.

## 历史授权 / Earlier grants

既有 [MIT](../LICENSE-MIT-LEGACY.md) 和 [PolyForm Noncommercial](../LICENSE-POLYFORM-LEGACY.md) 授权不追溯撤销。历史副本仅用于记录先前版本条款，不是当前新增内容的可选双许可。v0.3.140 标签仍携带 MIT；此前下载者按对应版本条款使用。不得重写历史标签或替换已发布附件来宣称追溯限制。

Existing [MIT](../LICENSE-MIT-LEGACY.md) and [PolyForm Noncommercial](../LICENSE-POLYFORM-LEGACY.md) grants are not retroactively revoked. These archived texts document earlier versions, not alternative licenses for new material. The v0.3.140 tag still carries MIT. Do not rewrite tags or replace released assets to claim retroactive restrictions.

## 项目与第三方边界 / Project and third-party boundaries

本轮统一 FC、FCM（fc-sensor-models）和 FCSD（FCS-Drivers）的许可。CLD（coverage-loop-desktop）明确排除；私有 FCS 和 FCW 不因本次公开仓库修改而自动更换许可或公开。FCSD 仅发布有权按所声明条款分发的产物，不能用公开 README 重授权私有源码或第三方代码。

This change covers FC, FCM (fc-sensor-models) and FCSD (FCS-Drivers). CLD (coverage-loop-desktop) is excluded. Private FCS and FCW are not automatically relicensed or made public. FCSD must distribute only artifacts it is entitled to distribute under the declared terms; its README cannot relicense private sources or third-party code.

第三方依赖、资源、参考模型及嵌入内容遵循原许可；来源或授权不明的资源不由本许可补授权。既有外部贡献也不得在无相应授权时被重新许可。贡献者保留著作权，不因贡献授予维护者额外商业转授权。插件和用户内容不自动继承本许可。

Third-party dependencies, assets, reference models and embedded material retain their original terms. Unknown permissions are not supplied by this license. Existing external contributions must not be relicensed without the required rights. Contributors retain copyright and do not automatically grant maintainers commercial sublicensing rights. Plugins and user content do not automatically inherit this license.

## 分发 / Distribution

后续 FC 构建沿用项目文件将根 LICENSE 复制为 LICENSE.txt 的流程；发布前核对包内许可与来源提交一致，并保留第三方声明。FCSD 新包必须附 LICENSE.txt 和第三方声明，在中英发行说明及 manifest 中写明 LicenseRef-Personal-Noncommercial-1.0 及适用范围；不能将旧 MIT / PolyForm 构建仅改标签当作新许可构建。模型下载包也附同版许可；生产摆盘是技术资料，不授予商业生产许可。

Future FC builds retain the project rule copying the root LICENSE as LICENSE.txt; verify it matches the source commit and preserve third-party notices. New FCSD packages must include LICENSE.txt and third-party notices, with LicenseRef-Personal-Noncommercial-1.0 and its scope in bilingual notes and the manifest. Relabeling an older MIT / PolyForm build does not relicense it. Model bundles must include the matching license; production plate documentation grants no commercial manufacturing permission.
