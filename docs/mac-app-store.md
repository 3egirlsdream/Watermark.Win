# Mac App Store 发布

Mac Catalyst Release 默认 `MacDistributionChannel=AppStore`，Debug 默认 `Direct`。
官网分发脚本 `scripts/build-macos-pkg.sh` 显式使用 `Direct`，不可用于商店提审。
渠道策略由宿主提供给共享 UI 和业务服务；商店版禁止网页支付及官网更新。

## 部署顺序

1. 部署 Server.NetCore 的 `POST /api/Watermark/Login`、`POST /api/Watermark/DeleteAccount`。
   原 GET 接口保留给旧客户端，新客户端不会回退到 GET。
2. 先部署 Server.NetCore 的 `WebsiteLogin`、`WebsiteLogout`、`WebsiteCreateOrder`、`WebsiteQueryOrder`，再部署 Watermark.Web 的官网账号和支付页面。
   公共入口必须为 HTTPS，Caddy 的 `/account/*` 保持由 Web 容器处理。
   登录票据为随机、单次使用，60 秒失效；只保存在 Web 进程内存中，重启后失效。
   多 Web 实例需粘性路由或先改用支持原子兑换的共享存储，不能直接随机分流。
   浏览器兑换票据后建立 HttpOnly、Secure、SameSite=Strict 的 30 分钟会话。
   现有账号的密码表示实际为 Base64，并非 MD5 或加密；仅通过 HTTPS POST 请求体传输，不进入跳转 URL、Cookie 或票据存储。后端密码存储升级须另行迁移，不把 Base64 当作安全保护。
   官网支持独立登录和支付宝会员购买（8/18/28 元，不自动续费）。短时后端凭证通过服务端加密签名的 HttpOnly Cookie 保存，不暴露给 JavaScript；请求通过同源 Origin 检查。
   后端按账号限制每 5 分钟 10 次登录尝试；Web 按连接 IP 限制每分钟 60 次。反向代理多用户可能共享 IP，不信任未经配置的 X-Forwarded-For。
   后端会话同样仅保存在进程内存，30 分钟到期；重启需重新登录。多后端实例也需粘性路由或共享会话存储。
   套餐价格和会员归属由后端确定，查询检查订单归属；支付宝验签/主动查询、金额与 AppId 校验及数据库事务负责开通，浏览器不能传入“已支付”状态。
   部署后必须用真实账号验证登录及一笔支付的回调、金额、重复回调和跨账号拒绝；本地自动测试不替代线上支付宝验证。
3. 确认以上服务上线，再发布客户端。旧服务未部署时登录/官网衔接会失败，不做不安全回退。

## 构建商店包

使用 MacAppStore 发布配置，并从钥匙串提供真实的商店证书和 provisioning profile：

```sh
dotnet publish Watermark.Andorid/Watermark.Andorid.csproj \
  -f net8.0-maccatalyst -c Release -p:PublishProfile=MacAppStore \
  -p:MacDistributionChannel=AppStore \
  '-p:CodesignKey=Apple Distribution: YOUR NAME (TEAMID)' \
  '-p:CodesignProvision=YOUR MAC APP STORE PROFILE' \
  '-p:PackageSigningKey=3rd Party Mac Developer Installer: YOUR NAME (TEAMID)'
```

提审前核对 ApplicationDisplayVersion / ApplicationVersion 与 App Store Connect，使用全新输出构建。
通过 TestFlight 验证 Apple Silicon 冷启动、离线启动、导入、导出、官网会话和商店更新跳转。
隐藏会员购买入口和提供官网账号入口并不构成外部支付资格；官网落地页及账号权益仍需按发行地区审核要求评估。
