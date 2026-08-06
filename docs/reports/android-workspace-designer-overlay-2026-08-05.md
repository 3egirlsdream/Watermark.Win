# Android 工作台模板叠套与参数误入设计器修复报告

- **首次记录日期**：2026-08-05
- **本次修订日期**：2026-08-06
- **文档类型**：根因复核、修复设计、实施记录与验收报告
- **影响范围**：`Watermark.Razor` 移动端模板入口与工作台；共享工作区会话启动接口；Android / iOS / Mac Catalyst / Windows / Web 共享组件兼容性
- **当前状态**：代码已实施；自动化测试、跨端构建及 Rider Android 模拟器回归通过；Android 物理设备和 Windows WebView 运行回归待补

---

## 1. 执行摘要

本轮处理的是两个彼此相关、但根因不同的核心问题：

1. **模板先行流程会产生“模板套模板”。** 用户先从模板库点击「使用模板」并选择照片时，旧流程先把模板和照片渲染成一张新的海报 PNG，再把这张派生图片作为工作台素材。用户进入工作台后再次选择模板，第二个模板只能套在这张已经包含第一个模板的 PNG 上，因此出现叠套。先导入照片再选模板时，工作台始终保留原始照片作为基础素材，所以行为正常。
2. **工作台底栏参数按钮误入完整模板设计器。** 「画布、尺寸、边距、外框、图片效果」等按钮原本通过 `TemplateDesignerRequested` 打开全屏 `WMTemplateDesigner` 的 `InstanceMode`。这与产品目标不符：这些按钮应在当前工作台内调整当前照片所应用模板的实例参数，而不是进入模板定义的设计界面。

修复后的行为是：

- 单照片槽模板从模板库选图后，工作台保存的是**原始照片 + 初始模板 ID**，不再先生成扁平海报；首次模板、切换模板和「无边」都从同一个原始素材重新计算。
- 工作台底栏的五类模板参数在当前底部面板内展开，继续复用现有模板属性面板、工作台草稿、预览和提交链。
- 只有模板库中的「编辑」或新建模板继续进入完整模板设计器。
- 对零照片槽和多照片槽模板，现有模型仍需生成多输入海报成品。为避免旧会话或这类成品继续叠套，工作台模板选择器会明确阻止再次套模板，并提示用户从模板库重新选择原始照片。

本轮没有新增解码、缩放、操作重放或编码管线；单照片槽模板先行流程反而删除了一次不必要的提前渲染和 PNG 编码。

---

## 2. 修正后的事实分级

| 结论 | 分级 | 依据 |
|---|---|---|
| 单槽模板先行流程先调用 `ApplyPosterAsync` 生成派生海报，再把派生海报作为工作台素材 | 已确认 | 旧模板入口调用链和工作区媒体 `Artifact.SourceOperation` 可直接追踪 |
| 再选模板时叠套的直接原因是基础素材已经是包含模板的扁平海报 | 已确认 | 工作台模板操作只能以当前媒体为基础；控制器测试可固定基础 artifact |
| 先选照片再套模板正常，是因为原始照片仍是工作区基础 artifact | 已确认 | 该流程通过工作台模板 operation 预览，不会替换基础素材 |
| 工作台五类参数按钮打开全屏实例设计器 | 已确认 | `WMMobileWorkspaceDock.TemplateDesignerRequested` 到 `MobileWorkspace.OpenWorkspaceTemplateDesignerAsync` 的静态调用链 |
| 底层工作台与全屏 Dialog 同时存在本身是渲染异常 | 不成立 | 模态框保留底层页面是正常结构；问题是错误的产品入口和编辑层级 |
| Android 底栏不可点击可仅凭桌面仿真排除 | 不成立 | Android WebView 的触摸合成、事件分发和组件生命周期必须在 Android 运行时验证 |
| MASA 首次激活时序是本次两个核心问题的唯一根因 | 不成立 | 本次叠套和误入设计器均有独立、确定的应用层调用链 |

既有 MASA.Blazor 1.11.9 升级、Dialog 激活修复和 Android WebView 兼容修复继续保留，但它们不再被描述为本次“模板套模板”或“误入设计器”的根因。

---

## 3. 根因分析

### 3.1 模板先行与照片先行走了两种不同的数据路径

旧的单槽模板先行路径：

```text
模板库「使用模板」
  -> 选择原始照片
  -> Controller.ApplyPosterAsync(imagesFirst: false)
  -> 模板 + 原始照片渲染并编码为新的海报 PNG
  -> 新海报 PNG 成为工作台当前媒体
  -> 工作台再次选择模板
  -> 新模板作用在“已含旧模板的 PNG”上
```

正常的照片先行路径：

```text
导入原始照片
  -> 原始 artifact 成为工作区基础媒体
  -> 工作台选择模板
  -> 模板作为有效操作参与预览
  -> 更换模板时替换模板操作
  -> 基础 artifact 始终是原始照片
```

因此，问题不是模板替换算法重复追加 operation，而是模板先行入口在进入工作台前已经丢失了可编辑的原始基础关系。

### 3.2 参数按钮把“模板实例调整”误映射为“模板定义设计”

旧调用链：

```text
WMMobileWorkspaceDock 五类参数按钮
  -> TemplateDesignerRequested
  -> MobileWorkspace.OpenWorkspaceTemplateDesignerAsync
  -> 全屏 MDialog
  -> WMTemplateDesigner InstanceMode=true
```

`WMTemplateDesigner` 适合新建或编辑模板定义，包含图层、素材槽、组件选择等完整能力。用户在工作台点击画布、尺寸、边距、外框或图片效果时，目标只是调整当前照片的模板实例参数。打开完整设计器既扩大了编辑范围，也使用户误以为离开了当前照片工作流。

正确复用点是现有 `WMTemplatePropertyPanel`、`WMTemplateEditorState`、`TemplatePreviewChanged` 和 `CommitTemplateAsync`，而不是再创建一个全屏编辑会话。

### 3.3 零槽和多槽模板的模型边界

- **单照片槽模板**可以表示为“一个原始工作区媒体 + 一个模板 operation”，因此可以无损保留原始素材并支持模板替换。
- **零照片槽模板**本质上是模板自身生成的海报。
- **多照片槽模板**依赖多个输入与槽位绑定；当前工作区媒体模型没有持久化多素材槽位关系，现有 `ApplyPosterAsync` 会输出一个扁平海报成品。

在没有新增多槽持久化模型的前提下，把零槽或多槽成品再次送入单媒体模板 operation 只会继续叠套。为避免隐性错误，本轮对这类派生海报采用显式保护，不伪造“可编辑实例”能力，也不新增平行渲染管线。

---

## 4. 目标交互

### 4.1 单槽模板先行

1. 用户在模板库点击模板并选择「使用模板」。
2. 系统打开 Android 图片选择器；取消选择时不创建空会话。
3. 选图后创建工作区会话，保存原始图片来源、初始模板 ID 和返回模板页的路径。
4. 只导航到 `/workspace/{sessionId}`。
5. 工作台首次预览显示一次模板效果，但当前媒体名称和基础 artifact 仍对应原始照片。
6. 再选其他模板时替换当前模板；选择「无边」时恢复原图；再次选模板仍只出现一层模板。

### 4.2 当前页面调整模板参数

1. 用户在工作台点击「画布、尺寸、边距、外框、图片效果」。
2. 对应属性面板在当前工作台底部区域展开，不创建全屏 Dialog，不离开当前照片。
3. 参数变化进入现有工作区模板草稿，并以 latest-wins 方式刷新预览。
4. 点击「应用」只提交当前媒体的模板实例；点击「取消」恢复提交前状态。
5. 关闭和重新打开相同面板时，已提交参数保持一致。

### 4.3 完整模板设计器的边界

- 模板库「编辑」和新建模板继续使用完整 `WMTemplateDesigner`。
- 工作台底栏参数不再打开 `InstanceMode` 设计器。
- 不新增 Android 专用设计器、不复制画布序列化或导出代码。

---

## 5. 代码实施

### 5.1 单槽模板先行改为工作区原始素材启动

修改 `Watermark.Razor/BlazorPages/Mobile/MobileTemplates.razor`：

- 注入并复用 `IWMWorkspaceLauncher`。
- 照片槽数量为 1 时，调用 `CreateFromSourcesAsync` 创建会话，同时传入：
  - `WMWorkspaceMode.Template`；
  - 用户选择的原始图片 source；
  - 初始模板 ID；
  - 当前模板页的返回路径。
- 导航统一为 `/workspace/{sessionId}`。
- 用户取消图片选择时立即返回，不创建会话。
- 零槽和多槽仍使用现有 `WMWorkspaceController.ApplyPosterAsync`，维持现有多输入渲染语义。

工作区控制器打开单槽会话时，会把初始模板 ID 恢复为模板 transaction/snapshot，再通过现有预览管线渲染。原始媒体不会被模板成品替换。

### 5.2 会话启动接口补充返回路径

以下接口增加末尾可选参数 `string? returnPath = null`：

- `IWMWorkspaceSessionStore.CreateAsync(...)`
- `IWMWorkspaceLauncher.CreateFromSourcesAsync(...)`

会话存储对返回路径执行已有的规范化后保存。该参数保持可选，因此不破坏现有调用方；工作区会话、画布 JSON、模板实例和导出协议均未变更。

### 5.3 阻止扁平海报再次套模板

`WMMobileWorkspaceDock` 根据当前媒体的 `Artifact.SourceOperation == WMImageOperationKind.Template` 识别派生海报：

- 模板选择器不展示可再次应用的模板卡片。
- 显示提示：“当前素材是已生成的海报成品，不能再次套用模板。请从模板库重新选择原始照片。”
- `PreviewTemplateAsync` 也执行相同保护，避免通过迟到事件或旧状态绕过 UI。

该保护覆盖零槽、多槽和旧历史会话中的扁平模板成品。

### 5.4 将五类工具映射为工作台内联属性面板

新增 `Watermark.Razor/Workspace/Components/WMMobileAppliedTemplateControls.razor`：

- 从 `State.TemplateEdit.CanvasJson` 创建 `WMTemplateEditorState`。
- 使用现有 `WMTemplatePropertyPanel` 显示指定的 `WMTemplateMobileInspectorSection`。
- 监听编辑器状态变化，序列化当前 snapshot，并通过 `TemplatePreviewChanged` 接入工作区模板草稿。
- 使用 100ms debounce 和取消令牌保证 latest-wins；组件销毁时取消待处理预览并解除监听。
- 通过组件隔离样式适配工作台底部面板高度、滚动和标题间距，不修改全局设计器样式。

`WMMobileWorkspaceDock` 使用 `WMPosterEditorPresentationState.CanvasConfigurationTools()` 的规范工具定义映射：

| 工作台工具 | 内联属性分区 |
|---|---|
| 画布 | Canvas |
| 尺寸 | Size |
| 边距 | Insets |
| 外框 | Frame |
| 图片效果 | ImageEffects |

当当前媒体还没有模板 ID 或画布 JSON 时，这些入口回到模板选择器；有活动模板时直接切换当前底部属性面板，包括存在瞬时草稿的情况。

### 5.5 移除工作台全屏实例设计器入口

修改 `Watermark.Razor/BlazorPages/Mobile/MobileWorkspace.razor`：

- 移除 `TemplateDesignerRequested="OpenWorkspaceTemplateDesignerAsync"`。
- 删除工作台内应用模板实例专用的全屏 `MDialog`。
- 删除 `showWorkspaceTemplateDesigner`、实例设计器字段及打开、关闭、完成提交方法。
- 删除与该全屏实例设计器有关的返回键和导航分支。

模板设计模式本身的 `WMTemplateDesigner` 未删除；模板库编辑、新建和 `TemplateDesign` 工作区能力保持不变。

---

## 6. 渲染与性能约束复核

### 6.1 没有新增平行管线

- 图片仍由 `IWMWorkspaceLauncher` 和现有 importer 导入。
- 模板预览仍通过 `WMWorkspaceController` 的模板 transaction 和预览调度执行。
- 内联参数仍通过现有 `TemplatePreviewChanged` 和 `CommitTemplateAsync` 提交。
- 导出、EXIF/ICC、JPEG 编码和模板布局算法均未复制或修改。

### 6.2 单槽流程减少一次工作

旧流程在进入工作台前执行一次完整的模板应用和 PNG 编码；进入工作台后又以该 PNG 为基础进行后续预览。新流程直接导入原始 source 并建立模板 operation，因此：

- 不再提前生成模板成品 PNG；
- 不再把成品重新解码为下一次模板的基础；
- 模板切换始终复用原始 artifact；
- 没有增加解码、必要缩放、操作重放或最终编码次数。

### 6.3 内联参数的并发行为

- 连续参数变化采用 latest-wins debounce。
- 新变化取消尚未提交的旧预览请求。
- 组件销毁会取消等待任务，避免旧面板向新状态回写。
- 最终工作区预览仍由控制器现有版本和取消机制裁决。

---

## 7. 自动化测试

### 7.1 新增或更新的契约测试

- 单槽模板先行必须调用 `IWMWorkspaceLauncher`，不得先生成扁平海报。
- 取消选图不得创建工作区会话。
- 零槽和多槽路径仍保留现有 `ApplyPosterAsync`。
- 工作台五类工具必须渲染 `WMMobileAppliedTemplateControls` 并使用规范映射。
- 工作台不得保留 applied-instance `TemplateDesignerRequested` 或全屏 `InstanceMode` Dialog。
- 扁平模板成品必须阻止再次预览模板。
- launcher 必须把 `returnPath` 传递到会话存储。
- 会话存储必须持久化初始模板 ID 和规范化返回路径。

### 7.2 控制器行为测试

`TemplateFirstThenSwitchingTemplate_AlwaysRendersOnceFromTheOriginalArtifact` 固定验证：

- 模板先行首次预览的基础 artifact 是 `original-media`；
- 切换模板后的基础 artifact 仍是 `original-media`；
- 每个渲染计划中只有一个模板步骤；
- 选择其他模板不会把前一个模板的输出作为输入。

### 7.3 测试结果

| 测试集 | 结果 |
|---|---:|
| `Watermark.Razor.Tests` | 425 / 425 通过 |
| `Watermark.Shared.Tests` | 111 / 111 通过 |

---

## 8. Rider Android 模拟器验证

### 8.1 环境

- IDE：JetBrains Rider `Running Devices`
- 设备：Pixel 5
- Android API：33
- 设备 ID：`emulator-5554`
- 架构：arm64
- 包名：`com.top.thankful.watermark.andorid`
- 构建：Release / AOT / `android-arm64`
- 安装方式：使用与现有应用一致的正式签名执行覆盖安装，保留原应用数据

最终 APK：

- SHA-256：`bbac38241129039b7a6e5bed0efc3bf84c4b8f48bc01b84a61754fb75697358a`
- 签名证书 SHA-256：`2b1f9dec180e972d6c3736c5efd52ee7952ede4155eeecdbbc23ea331ffa076e`
- `apksigner verify --print-certs`：通过

### 8.2 核心流程验证结果

使用单照片槽模板「经典水印相框」和模拟器相册中的测试图片执行：

| 用例 | 结果 |
|---|---|
| 模板库点击「使用模板」并选图 | 通过 |
| 进入工作台后标题仍显示原始图片文件名，而不是派生海报文件名 | 通过 |
| 首次预览只显示一层模板 | 通过 |
| 依次点击尺寸、画布、边距、外框、图片效果 | 五类属性均在当前工作台底部展开 |
| 是否打开全屏模板设计器 | 未打开 |
| 边距数值按钮与五档移动滚轮触摸 | 可点击、可滚动 |
| 上边距从 5% 调整为 6% | 预览即时更新 |
| 点击应用后关闭并重新打开边距面板 | 6% 已提交并保留 |
| 模板草稿取消 | 恢复提交前效果 |
| 切换到「无边」 | 恢复原始测试照片 |
| 再次选择「经典水印相框」 | 仍只有一层模板，没有叠套 |
| 完成模板草稿提交 | 返回正常工作台，不进入设计器 |

### 8.3 交互与稳定性

- 快速连续切换底栏工具正常。
- 属性区域横向和纵向触摸正常。
- 关闭、重新打开及工作区草稿应用/取消正常。
- WebView 控制台和 logcat 未发现 FATAL、未处理 .NET 异常、Blazor 渲染异常或 JS interop 异常。
- 仅出现 Chromium 缓存索引重建警告，与本功能无关。
- 最终签名 APK 覆盖安装后，Rider 模拟器可正常启动首页、打开模板页并进入模板选图链路。

Rider 模拟器验证可以证明本轮代码在 Android WebView 的真实触摸链上可交互，但不能代替不同厂商 WebView、系统导航方式和安全区组合的物理设备门禁。

---

## 9. 构建与跨端兼容结果

| 项目 / 目标 | 结果 |
|---|---|
| `Watermark.Razor` | 通过 |
| `Watermark.Web` Debug | 通过，0 警告 / 0 错误 |
| `Watermark.Andorid` Android Debug | 通过 |
| `Watermark.Andorid` Android Release arm64 / AOT | 通过，0 错误 |
| `Watermark.Andorid` Mac Catalyst Debug | 通过，0 警告 / 0 错误 |
| Windows 宿主运行验证 | 待 Windows 环境或 CI |

Android 构建仍报告本机 `monodroid-config.xml` 中无效 Java SDK 路径 `/Library/Java/JavaVirtualMachines/microsoft-11.jdk/Contents/Home` 的环境警告；构建使用有效的 Android SDK 完成，未影响 APK 生成和模拟器运行。该环境项应单独清理，不属于本 BUG 修复。

---

## 10. 验收标准

### 10.1 必须通过

- [x] 单槽模板先行选图后只进入工作台。
- [x] 工作台基础媒体仍是原始照片。
- [x] 首次模板只应用一次。
- [x] 切换模板不出现模板叠套。
- [x] 「无边」可以恢复原图，再选模板仍只出现一层。
- [x] 画布、尺寸、边距、外框、图片效果在当前工作台内配置。
- [x] 上述按钮不再打开完整模板设计器。
- [x] 参数预览、取消、应用和重新打开状态一致。
- [x] 扁平模板成品不能继续套用模板。
- [x] 未新增平行渲染或导出管线。
- [x] Razor 与 Shared 全量自动化测试通过。
- [x] Android Release arm64 构建和 Rider API 33 模拟器验证通过。

### 10.2 发布前补充门禁

- [ ] Android 物理设备覆盖手势导航和三键导航。
- [ ] 至少覆盖一个不同于模拟器的系统 WebView 版本。
- [ ] 零槽、多槽模板在真机上验证提示、返回模板库和素材状态。
- [ ] iOS 与 Mac Catalyst 对共享内联属性面板执行运行回归。
- [ ] Windows WebView 在 Windows 环境执行运行回归。

---

## 11. 风险与后续工作

### 11.1 多槽模板的完整实例编辑能力尚未建立

本轮没有把多张原始图片、槽位绑定和模板实例关系持久化到工作区。当前采用“保留既有多槽渲染 + 禁止对成品再次套模板”的安全策略。若产品后续要求多槽模板也能在工作台无损换模板，需要先设计：

- 多素材 source 的会话持久化；
- source 与照片槽 ID 的稳定绑定；
- 模板切换时的槽位迁移规则；
- 缺失、重复或新增槽位的交互；
- 多输入预览缓存、取消和导出一致性测试。

在该模型完成前，不应通过把成品 PNG 当作普通照片或复制 `ApplyPosterAsync` 来伪造支持。

### 11.2 物理设备仍是发布门禁

模拟器已经验证 Android WebView 触摸和内联面板行为，但厂商 WebView、GPU、系统栏、安全区和内存压力可能不同。若真机出现点击或滚动异常，应以点击探针、WebView 控制台和 logcat 定位实际事件链，不再把 MASA 首渲染时序预设为唯一原因。

### 11.3 旧历史会话

历史会话中的派生海报无法反推原始照片和槽位关系。它们会被识别为扁平模板成品并阻止继续套模板。用户必须从模板库重新选择原始照片，避免继续积累不可逆的模板层。

---

## 12. 关键改动文件

- `Watermark.Razor/BlazorPages/Mobile/MobileTemplates.razor`
- `Watermark.Razor/BlazorPages/Mobile/MobileWorkspace.razor`
- `Watermark.Razor/Workspace/Components/WMMobileWorkspaceDock.razor`
- `Watermark.Razor/Workspace/Components/WMMobileAppliedTemplateControls.razor`
- `Watermark.Razor/Workspace/Components/WMMobileAppliedTemplateControls.razor.css`
- `Watermark.Razor/Workspace/WMWorkspaceContracts.cs`
- `Watermark.Razor/Workspace/WMWorkspaceLauncher.cs`
- `Watermark.Razor/Workspace/WMWorkspaceSessionStore.cs`
- `Watermark.Razor.Tests/WMMobileWorkspaceDockContractTests.cs`
- `Watermark.Razor.Tests/WMWorkspaceLauncherTests.cs`
- `Watermark.Razor.Tests/WMWorkspaceSessionStoreTests.cs`
- `Watermark.Razor.Tests/WMWorkspaceControllerTests.cs`

---

## 13. 历史说明

该文件最初记录的是“模板应用后自动打开实例设计器，以及 Android Dialog 底栏交互”的修复方案。2026-08-06 的实际产品反馈进一步明确：工作台参数本来就不应进入完整实例设计器，同时暴露了模板先行入口先生成扁平成品所导致的模板叠套。

因此本版以新的目标交互和已实施代码为准：

- 不再把“工作台手动打开全屏实例设计器”作为验收目标；
- 不再把模板套模板排除在报告范围外；
- 既有 MASA 1.11.9、Dialog 和 Android WebView 兼容修复作为历史基础继续保留；
- 后续验收以“原始素材不丢失、模板可替换、参数当前页内联编辑”为准。
