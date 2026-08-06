# AGENTS.md

本仓库是「轻影 / Watermark」项目的多端代码集合，核心功能是水印模板设计、图片 EXIF 信息读取和批量生成带模板效果的新图片。

## 当前工作范围

- `Watermark.Andorid` 修改要考虑  macOS / Mac Catalyst / 安卓 / Windows / IOS 版本代码， 统一分为移动端和桌面端。
- 修改共享层代码要考虑其他端的适配，要能在桌面和移动端都完美适配显示，如果不能在其专有的组件目录下创建。
- 移动端和桌面端入口不同导向不同的UI，注意不要搞混。
- 不要修改 `bin/`、`obj/`、`.vs/`、`.idea/` 等生成文件或 IDE 本地文件。
- 除了模板编辑其余的可以不用考虑旧版兼容
- 图标库统一使用Phosphor Icons + Regular/Light 线宽

## 项目说明

### `Watermark.Andorid`

基于 .NET 8 MAUI Blazor Hybrid 的多平台应用，目标框架包括 `net8.0-maccatalyst`、`net8.0-ios`、`net8.0-android`，在 Windows 环境下还会包含 Windows 目标框架。

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

## 通用表单组件规范

- Razor UI 中需要下拉选择时统一使用公共组件 `Watermark.Razor/Components/Compatibility/WmSelect.razor`，选项统一使用强类型 `WmSelectOption<TValue>`。
- 不得新增原生 `<select>`、Masa `MSelect`、基于 `MMenu` 拼装的下拉选择器或其他平行实现；桌面端和移动端共同复用 `WmSelect`。
- `WmSelect` 必须保持显式开关状态：选择后关闭，后续点击可再次打开，并支持 `Esc` 关闭；修改公共组件时必须覆盖 Mac Catalyst、Windows WebView 和移动端触摸交互。

## 信息提示交互规范

- 移动端和桌面端的一次性信息提示（成功、失败、普通说明）统一使用“检查更新”同款的底部居中 Toast，不在页面内容区新增横幅、卡片或常驻提示条。
- Razor 组件统一通过 `Common.ShowToast(IPopupService, ...)` 触发 Toast；移动端和桌面端共同加载 `Watermark.Razor/wwwroot/css/wm-toast.css`，严格复用原“检查更新”提示的紧凑尺寸、深色背景、圆角、阴影和字号，不得直接使用 Masa 默认宽 Snackbar 样式。失败结果使用错误背景，同一时间最多显示一条。
- 需要用户作出选择的确认操作继续使用 `ConfirmAsync`；表单字段校验、空状态、持续进度和需要长期保留的业务状态应留在对应内容区域，不应改成短暂 Toast。

## 移动端数值参数组件规范

- 移动端所有具有 `Min`、`Max` 和 `Step` 的连续数值参数，统一使用公共组件 `Watermark.Razor/Components/Compatibility/WmNumericSlider.razor`；不得新增原生 `input[type="range"]`、Masa `MSlider` 或页面内自定义滑条作为平行实现。
- `WmNumericSlider` 在桌面端显示范围轨道与可编辑数值；在触摸设备上显示数值按钮，点按展开以当前值居中的五档滚轮，按钮区或滚轮中上下滑动按 `Step` 微调。浮层必须位于所属移动面板、底部工具栏和抽屉之上，不能被滚动容器裁切。
- 组件在数值实际切换到新的 `Step` 时统一触发一次轻触震动；到达边界、重复事件和外部数据回填不得震动。调用方不得另行叠加震动反馈。
- 连续预览使用 `ValueChanged`；需要撤销事务的场景通过 `InteractionStarted` 和 `InteractionEnded` 开启、提交一次编辑。父级已经提供字段标题时使用 `ShowLabel="false"`，不得重复显示标题。
- 非连续枚举、日期时间、颜色二维选择和单次开关不使用本组件，应使用相应的选择器或开关组件。

## 渲染管道性能约束

- 修改预览、应用或导出前，必须先追踪并复用现有渲染入口，禁止在页面组件中新增平行渲染管道。
- 同一图片、同一操作版本、同一目标尺寸只允许一次解码、一次必要缩放、一次操作重放和一次最终编码；UI 刷新、提交和导出必须通过共享产物、指纹或缓存衔接。
- `Flush` 只能等待或补齐最新版本，不允许无条件强制渲染。连续参数变化必须采用 latest-wins 和取消机制，禁止旧任务在队列中逐个完成。
- Blob URL 必须集中管理所有权：替换时释放、不重复创建，也不能释放仍被原图预览、撤销或待提交操作引用的 URL。
- JPEG 快速导出与高精度导出必须复用模板布局、调色和 EXIF/ICC 写入能力，禁止复制公共算法。
- 新增渲染功能必须提供调用次数测试或阶段计时，证明没有引入重复解码、渲染或编码。
