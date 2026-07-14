# AGENTS.md

本仓库是「轻影 / Watermark」项目的多端代码集合，核心功能是水印模板设计、图片 EXIF 信息读取和批量生成带模板效果的新图片。

## 当前工作范围

- `Watermark.Andorid` 修改要考虑  macOS / Mac Catalyst / 安卓 / Windows / IOS 版本代码。
- MAC部分修改参考路径：
  - `Watermark.Andorid/Platforms/MacCatalyst/`
  - `Watermark.Andorid/Watermark.Andorid.csproj` 中与 `net8.0-maccatalyst`、macOS 签名、打包、公证相关的配置
  - `Watermark.Andorid/README.md` 中 macOS 构建、发布、签名、公证相关说明
- 修改共享层代码要考虑其他端的适配。
- 不要修改 `bin/`、`obj/`、`.vs/`、`.idea/` 等生成文件或 IDE 本地文件。
- 除了模板编辑其余的可以不用考虑旧版兼容

## 项目说明

### `Watermark.Andorid`

当前重点项目。它是基于 .NET 8 MAUI Blazor Hybrid 的多平台应用，目标框架包括 `net8.0-maccatalyst`、`net8.0-ios`、`net8.0-android`，在 Windows 环境下还会包含 Windows 目标框架。

虽然目录名拼写为 `Andorid`，但这是现有项目名称，不要擅自重命名。当前只处理它的 macOS / Mac Catalyst 版本。

### `Watermark.Razor`

共享的 Razor UI 组件库，包含模板设计器、预览页、设置页、登录/市场/弹窗等通用 Blazor 页面和组件。多个宿主项目都会引用它。

### `Watermark.Shared`

共享业务模型和底层能力库，包含水印画布、图片、文字、边框、EXIF、颜色分析、模板、用户、配置、缓存、接口等核心模型和工具逻辑。它会被 Razor UI 和多个端使用。

### `Watermark.Win`

Windows 桌面宿主项目，基于 .NET 8、WPF 和 Blazor WebView。包含 Windows 专用窗口、热键、WPF 视图、Windows 端模型和入口逻辑。
- 共用MAC桌面端的UI，部分Windows API额外适配。

### `Watermark.Web`
Web端不用考虑，不报错即可。

### `Watermark`

较早或备用的 MAUI Blazor Hybrid 项目，目标包括 Android、iOS、Mac Catalyst，并在 Windows 环境下包含 Windows 目标框架。当前工作不要修改该项目，除非用户明确要求。

### 根目录文件

- `Watermark.sln`: 顶层解决方案，用于组织多个项目。
- `README.md`: 仓库总览，介绍项目目标、支持平台、开发环境和主要功能。

## 开发注意事项

- macOS 本地测试常用目标框架为 `net8.0-maccatalyst`。
- macOS 相关签名、公证、沙箱配置集中关注 `Watermark.Andorid/Platforms/MacCatalyst/Entitlements.plist`、`Info.plist` 和 `Watermark.Andorid.csproj` 的 Mac Catalyst 配置。
- `Watermark.Razor` 与 `Watermark.Shared` 是跨端共享代码，修改前要考虑 Windows、Android、iOS、Web 的连带影响。
- 保持现有 .NET 8、MAUI、Blazor Hybrid、WPF 项目结构，不做无关重构。
- 组件库尽量使用Masa Blazor

## 渲染管道性能约束

- 修改预览、应用或导出前，必须先追踪并复用现有渲染入口，禁止在页面组件中新增平行渲染管道。
- 同一图片、同一操作版本、同一目标尺寸只允许一次解码、一次必要缩放、一次操作重放和一次最终编码；UI 刷新、提交和导出必须通过共享产物、指纹或缓存衔接。
- `Flush` 只能等待或补齐最新版本，不允许无条件强制渲染。连续参数变化必须采用 latest-wins 和取消机制，禁止旧任务在队列中逐个完成。
- Blob URL 必须集中管理所有权：替换时释放、不重复创建，也不能释放仍被原图预览、撤销或待提交操作引用的 URL。
- JPEG 快速导出与高精度导出必须复用模板布局、调色和 EXIF/ICC 写入能力，禁止复制公共算法。
- 新增渲染功能必须提供调用次数测试或阶段计时，证明没有引入重复解码、渲染或编码。
