# Android 从零海报模板回归与修复报告

日期：2026-07-30
结论：通过

## 1. 验收口径与更正

此前曾用模板库中的现成模板完成“应用模板 → 导出”流程，该结果不满足“从 0 设计复杂海报模板”的验收条件，本报告不再把它作为通过证据。

本次使用 Android 模拟器从固定 3:4 空白画布开始，真实创建并保存了两个新的本地模板：

| 轮次 | 模板名称 | 模板 ID | 用途 |
| --- | --- | --- | --- |
| 首轮 | 从零回归海报 2026-07-30 | `1196858543E64B079E4F80801AF3C1B0` | 复现崩溃、完成第一轮修复和导出验证 |
| 修复后 | 从零回归海报 修复后 2026-07-30 | `DB4FF81D559C4A3DBDA9266570B46ED3` | 验证图层默认位置修复，并完成第二次完整回归 |

两个模板均由空白画布创建，不是复制或修改现成模板。

## 2. 测试环境

- 设备：Android Emulator `sdk_gphone64_arm64`
- 系统：Android 13，393dp 宽纵屏
- 包名：`com.top.thankful.watermark.andorid`
- 构建：Debug / `net8.0-android`
- 输入：Android 系统照片选择器
- 操作与取证：ADB 触摸、截图、日志、进程和内存采样；Android WebView CDP 用于确认路由、DOM 状态和控件位置

## 3. 首轮从零回归

### 3.1 实际设计内容

从固定 3:4、4500 × 6000 空白画布开始，依次添加：

1. 根级图片容器；
2. 本轮导入的实拍图片素材；
3. 主标题文字；
4. 副标题文字；
5. 分割线。

### 3.2 首轮发现的问题

| 编号 | 严重级别 | 问题 | 复现与证据 | 状态 |
| --- | --- | --- | --- | --- |
| BUG-01 | P0 | 为新增容器选择素材后页面进入错误恢复页 | `ArgumentOutOfRangeException: Unsupported Phosphor icon ... Actual value was sticker` | 已修复 |
| BUG-02 | P0 | 4500 × 6000 原尺寸 JPEG 导出时进程被 Android LMK 杀死 | 进程被杀前 RSS 约 1,145,572 KB，无托管异常堆栈 | 已修复 |
| UI-01 | P1 | 连续添加的根级文字和分割线默认都位于 `Top=10%`，内容互相重叠 | 首轮导出的标题、副标题、分割线叠在同一位置 | 已修复 |
| OBS-01 | 观察项 | 中间的双槽草稿选择两张照片后出现过一次 WebView 空白 | 进程仍存活且无托管异常；强制重启恢复；最终单槽规格和完整复测均未复现 | 未达到稳定复现条件 |
| DATA-01 | P3 | 一份 PNG 内容的测试素材沿用了 `.jpg` 文件名 | Skia 按文件头正常解码，未影响本次渲染；属于素材元数据清洁度问题 | 记录，非本轮阻断项 |

## 4. 原因分析与修改

### BUG-01：素材用途面板使用了不存在的图标名

素材本身已完成选择和预览渲染。随后 `MacSelectionInspector` 渲染“照片 / 图形”用途选择时，图形项使用了 `"sticker"`。当前打包的 Phosphor 图标路径表没有该键，组件渲染阶段抛出 `ArgumentOutOfRangeException`。

修改：

- 将图形用途图标从 `"sticker"` 改为已打包的 `"shapes"`。
- 新增契约测试，反射检查用途列表中的所有图标都能由 `WmPhosphorIconPaths` 解析。
- 新增真实图片容器回归测试：在固定 4500 × 6000 V2 画布中写入素材、选择素材并执行真实预览渲染。

涉及文件：

- `Watermark.Razor/Components/Mac/MacSelectionInspector.razor`
- `Watermark.Razor.Tests/WMPosterContainerAssetRegressionTests.cs`

### BUG-02：导出链同时持有多个 4500 × 6000 位图

同一次导出中存在两处不必要的全尺寸内存占用：

1. V2 布局测量调用 `DrawContainer(..., measureOnly: true)` 时，仍创建了完整 4500 × 6000 BGRA 位图；测量实际只需要逻辑宽高。
2. 最终渲染完成后，`RenderSrgbSurface` 又调用 `bitmap.Copy()`，使原图和副本同时驻留。

这会额外产生两个约 108 MB 的原始像素表面，并叠加源图解码、Skia 原生分配、编码器和 WebView 占用，最终触发 Android Low Memory Killer。

修改：

- 测量阶段只创建 1 × 1 占位位图，使用显式逻辑宽高完成文字换行和嵌套容器测量。
- 在现有 `GenerationCore` 管线中增加最终表面所有权转移；标准 sRGB 导出直接把最终位图交给调用方，不再复制。
- 高精度回调仍保持原有所有权行为，没有新增平行渲染或编码管线。
- 新增 4500 × 6000 固定海报真实 JPEG 渲染测试，覆盖图片容器、布局和完整尺寸输出。

涉及文件：

- `Watermark.Shared/Models/WatermarkHelper.cs`
- `Watermark.Razor.Tests/MacFullResolutionRenderServiceTests.cs`

### UI-01：新增根图层缺少默认错位策略

V2 根级非容器图层都采用同一套默认值，连续添加文字和分割线时全部落在画布左上方同一位置，用户必须逐个拖开后才能辨认和编辑。

修改：

- 根级非容器图层按现有图层数设置默认位置。
- 首列依次使用 `Top=10%、20%、30%...`；超过一列时再递增 `Left`。
- 图片容器默认布局不变，子节点定位不变，所有根节点仍保持 `Absolute`。
- 新增测试验证“容器 + 标题 + 副标题 + 分割线”的默认位置分别为 10%、20%、30%，且互不重叠。

涉及文件：

- `Watermark.Razor/Workspace/WMControlTree.cs`
- `Watermark.Razor.Tests/MacControlTreeTests.cs`

该处理遵循 LayoutSchemaVersion 2 的固定画布语义：根节点保持绝对定位，空图片槽不改变模板结构。

## 5. 修复后第二轮完整回归

### 5.1 从零设计

新建固定 3:4 空白模板 `DB4FF81D559C4A3DBDA9266570B46ED3`，从设计器依次完成：

1. 添加图片容器并选择素材；
2. 进入素材用途面板，`照片 / 图形` 正常显示，没有图标异常；
3. 添加标题 `AFTER / LIGHT`；
4. 添加副标题 `SPACED BY DEFAULT · 2026`；
5. 添加分割线；
6. 保存模板。

设计器中的实际默认位置：

- 图片容器：画布顶部；
- 标题：`Top=10%`；
- 副标题：`Top=20%`；
- 分割线：`Top=30%`。

画面中三个新增根图层清晰分离，UI-01 未复现。

### 5.2 选择图片并应用

- 模板库显示：`固定画布 · 1 槽`。
- 点击“使用模板”。
- Android 系统照片选择器选择 1 张真实照片。
- 正常进入实例编辑器，路由包含：
  `posterTemplateId=DB4FF81D559C4A3DBDA9266570B46ED3`。
- 点击“完成”后显示：`海报已应用到当前工作台`。

### 5.3 原尺寸导出

- 格式：JPEG
- 质量：92
- 尺寸：原尺寸
- 结果：显示 `已导出 1 张图片` 和 `导出完成`
- 设备文件：
  `/sdcard/Pictures/Litograph/从零回归海报 修复后 2026-07-30-01.jpg`
- 文件大小：604,980 bytes
- 像素：4500 × 6000
- SHA-256：
  `946192298b459d237791ac0e4f3313f638c80163ccd6de0473d301132bb0ab53`
- 导出后进程仍存活；采样值：
  - Total RSS：350,476 KB
  - Total PSS：272,930 KB
  - Native Heap RSS：158,393 KB

实际导出图已人工检查：图片容器、主标题、副标题和分割线均存在，默认错位保持，未出现叠层或裁切错误。

## 6. 自动化与构建验证

| 验证项 | 结果 |
| --- | --- |
| `dotnet test Watermark.Razor.Tests/Watermark.Razor.Tests.csproj --no-restore` | 415 / 415 通过 |
| 新增/相关定向测试 | 18 / 18 通过 |
| Android `net8.0-android` 构建与部署 | 通过，0 errors |
| `git diff --check`（主仓库及 `Watermark.Shared`） | 通过 |

构建仍输出仓库中已有的 Razor/nullable 警告，本轮没有把警告清理扩展为无关重构。

## 7. 最终结论

本次已用两个全新模板完成“空白画布设计 → 添加图片/文字/分割线 → 保存 → 系统选择图片 → 应用模板 → 4500 × 6000 JPEG 导出”的完整闭环。

三个已确认问题均已修复并有自动化覆盖：

- 素材用途面板不再因无效 Phosphor 图标崩溃；
- 原尺寸导出不再因重复全尺寸位图触发进程回收；
- 连续新增根图层不再默认重叠。

最终 Android 模拟器回归通过。
