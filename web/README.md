# FluentControl 配置分享站

参考 RaySnapCF 的 Next.js / React / Auth.js 技术路线，采用**一个全栈 Cloudflare Worker**：SSR 页面、`/api/auth/*`、`/api/v2/profiles/*` 同域部署在 `https://fctrl.app`。数据库为 PostgreSQL，通过 Hyperdrive 连接；不需要另一个后端服务或 API 子域名。

状态：首版源码。正式域名、数据库和 OAuth 身份须配置后才能上线；不会复用 RaySnap 的数据或密钥。

## 已实现

- 配置社区首页：名称搜索、应用/游戏、型号（名称/PnP 码）、品牌的 AND 筛选，分页，中文界面与响应式布局。
- 一个配置包可包含多组、多场景和多台显示器；保留离线屏幕的公共 slot、别名、参数和映射，兼容桌面端 v1/v2 分享文件。
- 发布前预览、私人草稿、公开分享、作者编辑、版本冲突检查、历史版本固定下载与删除。
- 筛选按同一场景匹配应用，型号/品牌必须匹配同一台屏幕。
- GitHub / Google 登录使用 Auth.js + PostgreSQL Adapter，同域 HttpOnly 会话；账号不按邮箱自动合并。
- 公开包无需登录即可浏览下载。草稿、删除状态及从未公开的历史草稿不会被下载接口泄漏。
- 分享接口只接受白名单纯数据；限制 1 MiB、128 组、512 场景、每场景 16 屏；作者每天最多 100 次发布/更新。

尚未实现：评分/评论/收藏、桌面端 OAuth 登录及一键上传、管理员审核后台、账户注销。浏览器上传与客户端通过 HTTPS 链接下载可独立使用。

## 本地开发

Node.js 22+（CI 用 24）、PostgreSQL 16+。在 `web` 目录：

```bash
npm ci
cp .dev.vars.example .dev.vars
# 填写本地 DATABASE_URL、APP_URL 和新申请的 OAuth 凭据
```

迁移工具使用进程环境中的 `DATABASE_URL`，不自动执行生产迁移、不读取已提交凭据。先在终端安全设置该变量（与 `.dev.vars` 使用同一本地数据库），再执行：

```bash
npm run db:migrate
npm run dev
```

`.dev.vars` 仅本地使用且被 git 忽略；`next dev` 通过 OpenNext dev initialization 读取 Wrangler 本地绑定。生产预览使用 `npm run cf:build && npm run preview`，此时 `APP_URL` 应匹配预览地址；OAuth 提供方必须配置相应回调。

## PostgreSQL 与 Hyperdrive

1. 单独创建 PostgreSQL 数据库，可使用现有 PG、Neon、Supabase 或其他兼容服务。Hyperdrive 是连接加速层，不是 PostgreSQL 数据库本身。
2. 对直连 PG 的迁移连接执行 `npm run db:migrate`，保存迁移记录与校验和。首次迁移建表，后续重复执行不会重复建表。
3. 在 Cloudflare 中创建连接该数据库的 Hyperdrive 配置，**关闭查询缓存**，保证登录状态、私人草稿和撤回分享及时生效。
4. 在 `wrangler.jsonc` 添加：

```json
"hyperdrive": [{ "binding": "HYPERDRIVE", "id": "你的 Hyperdrive ID" }]
```

每个请求创建自己的 `pg.Pool`，请求完成后释放；不跨 Worker 请求共享套接字。应用参数查询不拼接用户值。生产优先使用 Hyperdrive；`DATABASE_URL` 直连入口保留给本地开发或没有 Hyperdrive 的环境。使用托管 PG 提供的 TLS 连接地址，不禁用证书验证。

## GitHub / Google OAuth 配置

两者都新建用于 FluentControl 的应用，不复用 RaySnap。生产网站 URL 为 `https://fctrl.app`。

| 项目 | 值 |
| --- | --- |
| GitHub OAuth callback | `https://fctrl.app/api/auth/callback/github` |
| Google Authorized JavaScript origin | `https://fctrl.app` |
| Google Authorized redirect URI | `https://fctrl.app/api/auth/callback/google` |
| 本地 GitHub callback | `http://localhost:3000/api/auth/callback/github` |
| 本地 Google callback | `http://localhost:3000/api/auth/callback/google` |

GitHub 建议生产/本地分别注册 OAuth App；Google 可按其控制台规则填写多个 redirect URI。Google OAuth 同意屏幕处于测试状态时，需要添加测试用户；对公众开放前完成相应发布设置。

把以下值放在 **Cloudflare Worker Secrets**：

```text
AUTH_SECRET                 # 独立的随机密钥，至少 32 字符
AUTH_GITHUB_ID
AUTH_GITHUB_SECRET
AUTH_GOOGLE_ID
AUTH_GOOGLE_SECRET
```

`APP_URL=https://fctrl.app` 是普通配置。代码不会将 Secret 放进 `NEXT_PUBLIC_*`、静态资源或版本库。可在控制台填写，或使用 `npx wrangler secret put <名称>` 的交互输入。`AUTH_SECRET` 可在本机用 `node -e "console.log(require('node:crypto').randomBytes(32).toString('base64url'))"` 生成。

## 构建与部署

```bash
npm run typecheck
npm test
npm run cf:build
npx wrangler deploy --dry-run
# 确认绑定、Secrets、数据库迁移都就绪后：
npm run deploy
```

Cloudflare Workers Builds 可选择本仓库，把 **Root directory 设为 `web`**：Build command 为 `npm ci && npm run cf:build`，Deploy command 为 `npm run deploy`，构建监控路径限定 `web/**`。Workers 控制台中为同一个 Worker 添加自定义域名 `fctrl.app`，或在 Wrangler 添加 `routes: [{ pattern: "fctrl.app", custom_domain: true }]`。预览域名测试时同步修改 `APP_URL` 和 OAuth redirect URI；正式域名绑定不在此源码提交中自动执行。

OpenNext 沿用 RaySnap 的部署适配器和版本，不要求把页面/API 拆成两个 Worker。页面全部动态读取，首版不需要 R2、D1、KV 或 AI Worker。配置 JSON 与版本保存在 PG JSONB 中；未来加入截图/大附件时再接 R2。部署是否适合 Workers Free，以实际 dry-run 压缩体积及账号限制为准。

## 接口

| 方法 | 地址 | 行为 |
| --- | --- | --- |
| GET | `/api/v2/profiles?q=&application=&modelId=&brand=&page=1` | 公开配置包摘要，12 条/页 |
| GET | `/api/v2/profiles/{id}?revision=1` | 原始 v2 JSON，直接兼容桌面端链接导入；不重定向 |
| POST | `/api/v2/profiles` | 登录后创建；同源请求，支持原始 v2/v1 包或发布 envelope |
| PATCH | `/api/v2/profiles/{id}` | 作者更新，必须包含 `expectedRevision` |
| DELETE | `/api/v2/profiles/{id}` | 作者删除，JSON body 带 `expectedRevision` |

发布 envelope：`{ title, description, visibility: "public" | "draft", bundle, expectedRevision? }`。桌面端未来上传需要独立的授权流程，不能直接把 GitHub/Google Client Secret 内嵌到 Windows 应用。

## 验证

`npm test` 包含协议、参数白名单与范围检查。设置 `DATABASE_URL` 后还执行真实 PostgreSQL 的登录表、会话、权限、多维关联筛选、并发版本更新、草稿与删除隔离测试；无数据库时该集成测试明确跳过。GitHub `Website build` 会启动 PostgreSQL 服务，执行两次迁移验证幂等，再跑所有测试、Next/OpenNext 构建和 Wrangler dry-run。

实际 OAuth 登录与正式 Hyperdrive 连接需要新凭据后验收；不能用模拟成功页面代替。

参考：[OpenNext Cloudflare](https://opennext.js.org/cloudflare)、[Auth.js PostgreSQL Adapter](https://authjs.dev/getting-started/adapters/pg)、[Hyperdrive + pg](https://developers.cloudflare.com/hyperdrive/examples/connect-to-postgres/postgres-drivers-and-libraries/node-postgres/)。
