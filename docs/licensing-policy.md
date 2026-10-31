# 许可策略与迁移说明 / Licensing policy

当前 FC、FCM（fc-sensor-models）和 FCSD（FCS-Drivers）采用未经改写的 **PolyForm Noncommercial License 1.0.0**，SPDX 标识为 `PolyForm-Noncommercial-1.0.0`。完整条款见各仓库 LICENSE。允许许可定义的非商业使用、修改与分发，包括条款列明的教育、科研、慈善等机构用途；不再追加“仅限个人”“禁止修改”“禁止非商业分发”等自定义限制。一般商业用途不获授权，具体范围以许可原文为准。

FC, FCM (fc-sensor-models) and FCSD (FCS-Drivers) now use the unmodified **PolyForm Noncommercial License 1.0.0**, SPDX identifier `PolyForm-Noncommercial-1.0.0`. Each repository's LICENSE governs. Noncommercial use, modification and distribution are allowed as defined there, including the listed organization uses. No additional personal-only, no-modification or no-noncommercial-redistribution restrictions apply. General commercial use is not granted; consult the full license.

## 历史授权 / Earlier grants

2026-10-31 的本次提交替换此前的自定义个人使用许可，不重写历史。此前合法取得的 MIT、PolyForm 或自定义许可授权仍按各自条款处理。历史 [MIT 副本](../LICENSE-MIT-LEGACY.md)及 [PolyForm 副本](../LICENSE-POLYFORM-LEGACY.md)用于版本追溯，不为新增内容提供 MIT 双许可。v0.3.140 等历史 MIT 标签及已发布安装包保持不变。当前 LICENSE 使用 PolyForm 官方原文，自定义许可可从 Git 历史查询。

This change on 2026-10-31 replaces the custom personal-use license without rewriting history. Earlier lawful MIT, PolyForm or custom grants retain their own terms. The archived [MIT](../LICENSE-MIT-LEGACY.md) and [PolyForm](../LICENSE-POLYFORM-LEGACY.md) texts document history, not an MIT alternative for new content. Historical MIT tags such as v0.3.140 and published installers remain unchanged. LICENSE uses the official PolyForm text; the custom license remains in Git history.

## 范围与第三方 / Scope and third parties

CLD（coverage-loop-desktop）不在本轮范围内；私有 FCS、FCW 的许可和可见性不因此自动改变。第三方依赖、参考模型、素材和嵌入内容保留原许可；来源或授权不明的内容不由本许可补授权。贡献者应有权按当前许可提交内容，既有外部贡献不得在无授权时重新许可。插件和用户内容不自动继承项目许可。

CLD (coverage-loop-desktop) is excluded. Private FCS and FCW are not automatically relicensed or made public. Third-party dependencies, reference models, assets and embedded material keep their terms; unknown permissions are not supplied by this license. Contributors must have the right to offer their work under these terms; existing external contributions cannot be relicensed without permission. Plugins and user content do not automatically inherit the project license.

## 分发与模型 / Distribution and models

FC 后续构建沿用根 LICENSE 复制为 LICENSE.txt 的流程。FCSD 后续包须附同版许可与第三方声明，中英发行说明和 manifest 使用 `PolyForm-Noncommercial-1.0.0` 并标明适用范围；先核对真实构建来源及其分发权，不把旧安装包重新贴标签冒充新许可构建。

Future FC builds retain the rule copying root LICENSE as LICENSE.txt. Future FCSD packages must include the matching license and third-party notices; bilingual notes and manifests use `PolyForm-Noncommercial-1.0.0` and identify scope. Verify the actual build source and distribution rights; do not relabel older installers as new-license builds.

FCM 将本许可用于其有权授权的原创设计文件、脚本与文档；模型包附同版许可。PolyForm 是面向软件起草的许可，此采用不承诺覆盖所有实体制造、外观设计或专利权，也不授予未知上游参考模型的权利。生产摆盘只是技术资料，不是另行商业生产授权。它是非商业源码/设计可见许可，不是 OSI 开源许可。

FCM applies these terms to original design files, scripts and documentation it is entitled to license, and includes the license in model bundles. PolyForm was drafted for software; its adoption does not claim coverage of every physical manufacturing, design or patent right or grant rights to unknown upstream references. Production plates are technical material, not a separate commercial manufacturing authorization. This is a noncommercial source/design-available license, not an OSI open-source license.
