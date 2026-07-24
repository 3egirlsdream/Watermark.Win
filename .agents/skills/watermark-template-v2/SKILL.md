---
name: watermark-template-v2
description: 为「轻影 / Watermark」生成、修改、解释和校验 LayoutSchemaVersion 2 水印模板。用于用户要求根据版式描述、参考图、EXIF 文本、Logo、分割线或背景装饰创建可导入的 config.json，调整 V2 Flex/Absolute 布局，解决动态文字遮挡、左右/上下锚定、双行对齐，或审查现有 V2 模板配置时；涉及模板编辑器的画布直属节点、Resize/Scale、文字、Logo、分割线原生尺寸与拖拽交互时也应使用。
---

# Watermark 模板 V2

按照项目当前实现生成模板，不把理想化 CSS 行为当成已实现能力。

## 读取参考

- 创建或修改 `config.json` 前，完整读取 [references/model-reference.md](references/model-reference.md)。
- 设计连续布局、动态文本或自由装饰时，读取 [references/layout-recipes.md](references/layout-recipes.md)。
- 需要一个可导入结构作为起点时，读取 [references/right-anchored-footer.json](references/right-anchored-footer.json)，然后替换 ID、内容和资源，禁止原样复制 ID。

参考 JSON 中自动品牌 Logo 的 `Path` 故意留空，用于演示普通模板的动态品牌行为，不是可直接打包交付的完整资源；压缩模板必须按下文规则补入回退图片。

若仓库代码可访问，先确认以下文件没有改变；代码与本 Skill 冲突时，以代码为准并同步更新 Skill：

- `Watermark.Shared/Models/WMStyle.cs`
- `Watermark.Shared/Models/WMText.cs`
- `Watermark.Shared/Models/WMImage.cs`
- `Watermark.Shared/Models/WMLayoutEngine.cs`
- `Watermark.Shared/Models/WatermarkHelper.cs`
- `Watermark.Shared/Models/Global.cs`
- `Watermark.Razor/Workspace/WMTemplateValidator.cs`

## 工作流

### 1. 建立模板规格

从用户输入或参考图提取：

- 画布类型、参考宽高比、图片区和留白区；
- 每个视觉分组的锚定边、占用范围和层叠关系；
- 哪些节点参加连续布局，哪些是背景、印章或自由装饰；
- 文本的 EXIF Key、前后缀、字体文件、大小、字距、颜色、粗斜体和换行策略；
- Logo 来源、分割线方向、间距、边距、内边距和裁切要求；
- 半透明材质是否需要真实背景模糊、圆角、描边替代层和阴影；
- 交付形态是本地模板目录、可导入压缩模板还是模板市场资源；
- 动态文本的最短、典型、最长样本。

只在缺失信息会明显改变成品时提问。否则采用保守默认值，并在交付时列出假设。

### 2. 先分组，再定属性

将版式分解为少量顶级节点：

1. 顶级容器、文字、Logo 和线条都使用 `Position=Absolute`，通过 `Top/Right/Bottom/Left + Width/Height` 相对画布定位。
2. 容器内部默认使用 `Position=Static` 参加 Flex；顺序只由同一父节点下的 `PNode.SEQ` 决定。
3. 使用 `FlexDirection`、`FlexReverse`、`JustifyContent`、`AlignItems` 和 `Gap` 表达连续布局。
4. 仅将背景、印章和确实需要自由覆盖的节点设为 `Absolute`；它们不占 Flex 空间。
5. 使用 `Margin` 做局部修正，使用 `Padding` 控制容器内容区，不为 Flow 节点制造 X/Y 或平移 Transform。

简单、独立的文字、Logo 和线条应直接放在画布根级，不要为它们创建透明包装容器。需要动态连续排版、共同裁切、背景、阴影或整体效果时，仍应使用容器和 Flex。

优先采用 [references/layout-recipes.md](references/layout-recipes.md) 的配方，不为一个场景创建第二套布局数据。

### 3. 构造可持久化配置

- 固定写入 `LayoutSchemaVersion: 2`。
- 为所有节点写入统一 `Style`，即使叶子节点不会使用容器专属字段。
- 使用唯一、非空 ID；新 ID 默认采用 32 位大写十六进制字符串。
- 输出扁平的 `Containers/Texts/Logos/Lines` 数组，并为每个节点写入 `PNode.PID/SEQ`。
- 根级允许容器、文字、Logo 和线条混排；根节点统一写 `PID="0"`，并在全部节点类型之间使用连续、唯一的 `SEQ`。
- 容器仍只可靠支持“根容器 → 可选二级容器 → 叶子节点”。禁止生成第三级容器；根级叶子不计入容器深度。
- 二级自动宽度内容组设置 `Style.Width=Auto`；高度使用明确的 `Style.Height=Percent`，避免依赖尚未实现的完整 auto-height 测量。
- 新文本显式写入 `LetterSpacing`。它按画布短边百分比换算，并参与真实测量、换行和 Flex 本征宽度；不要用拉宽图片或字符中手工插空格替代字距。
- 当完整前景 PNG 定义了固定照片窗、且用户要求不同来源比例都不能出现黑边时，在 `ImageProperties` 写入 `CoverPhoto=true` 与正数 `CoverPhotoAspectRatio`。渲染器会对主照片做一次居中等比 cover 裁切；它不会拉伸图片，超出照片窗的边缘会被裁掉。该比例必须等于照片内容区的宽÷高，而不是整张卡片的宽÷高。
- 参考图里的机型、镜头、光圈、焦距、快门、ISO、时间、坐标和照片编号都只是预览样本。承担照片信息语义的文字必须配置非空 EXIF `Key`，不得把参考值写进空 Key 的 `Prefix`。空 Key 只用于栏目名、标题等与照片无关的装饰文案。
- 不使用 `order`、`flex-basis`、`min/max size`、`align-self` 或 `flex-wrap`；项目没有这些字段。
- 只使用 `Style` 表达布局。不要输出容器顶层的 `ContainerAlignment/Orientation/HorizontalAlignment/VerticalAlignment/WidthPercent/HeightPercent/XOffset/YOffset/Angle`，也不要输出节点顶层的 `Margin/Transform`。
- 画布尺寸手柄的默认语义是 Resize：修改 `Style.Width/Height`，文字左右手柄只修改文本框宽度，角点修改 `FontSize` 并让高度继续保持 Auto；文字实时框按内容尺寸缩放，`BorderWidth/BorderPadding` 作为固定 inset 不随字号缩放。Logo 的侧边手柄只改变被拖动轴并让图片填满新尺寸，角点在比例锁定开启时才等比调整。`Style.Transform.ScaleX/Y` 只用于 Absolute 节点的高级整体缩放，不要用 Scale 代替普通尺寸调整。
- 分割线只有长度和粗细两种尺寸：横线长度写 `Style.Width`、竖线长度写 `Style.Height`，另一轴保持 Auto 并由 `Thickness` 决定。横线只提供左右长度手柄，竖线只提供上下长度手柄；单轴 Resize 必须保持垂直轴位置和对侧锚点，不能把指针的垂直轴抖动写入位置。透明拖动命中区只改善细线操作，不得改变 Bounds、线条粗细或持久化配置。

若使用 C# 构建对象，直接创建节点、完整设置 `WMStyle`，并通过 `Global.CanvasSerialize` 生成扁平 JSON。

### 4. 校验

先运行确定性校验器：

```bash
python3 .agents/skills/watermark-template-v2/scripts/validate_template_v2.py /absolute/path/to/config.json
```

交付包含本地图片或字体资源的模板时，增加：

```bash
python3 .agents/skills/watermark-template-v2/scripts/validate_template_v2.py /absolute/path/to/config.json --template-dir /absolute/path/to/template --strict-assets
```

修复所有 error。逐条判断 warning；不要用忽略 warning 代替资源核对。

### 5. 使用真实渲染链验证

在项目环境可运行时：

1. 用 `Global.ReadConfig` 读取配置，再用 `Global.CanvasSerialize` 回写，确认节点、顺序和 Style 未丢失。
2. 复用 `WatermarkHelper.GenerationDesignPreviewAsync` 或当前模板预览入口，不创建平行渲染器。
3. 分别使用横图、竖图和至少两组 EXIF 样本渲染。启用 `CoverPhoto` 时，横图、竖图和方图都必须覆盖完整照片窗；确认没有黑边或透明露底，且源图没有被非等比拉伸。
4. 两组 EXIF 必须使用明显不同的机型、曝光、时间、坐标或编号，并确认相应文字像素确实随元数据变化；不能只检查配置里存在 Key。
5. 使用空元数据再渲染一次：V2 动态片段缺少 Key 时，其 Prefix/Value/Suffix 应整体隐藏，不能留下 `F mm S` 一类占位符；装饰性固定文字仍应显示。
6. 对动态文本使用最长样本，验证不重叠、不越界、右/左/上/下锚点不漂移。
7. 对双行文字验证行间顺序、水平对齐、基线或居中关系。
8. 有参考图时先裁出水印/边框/卡片所在 ROI，比对文字起点、锚定边、图标中心、分割线和卡片边界；全图平均像素差只能作为背景参考，不能替代局部验收。
9. 使用 `EnableGaussianBlur=true` 时，用包含纹理或高频细节的源图验证：容器内细节方差降低、容器外像素不变、圆角裁切和 ZIndex 正确。纯色图不能证明背景模糊生效。
10. 对自动品牌 Logo 额外测试 `Make` 缺失和未知品牌，确认回退结果可接受。
11. 确认预览 Bounds 与最终像素输出一致。

只对已约定并实际测试过的最长文本样本承诺不重叠。当前 `nowrap + overflow:hidden` 在内容超过可用宽度时会裁切，不能承诺任意长度都完整显示。

无法运行渲染时，明确标记“结构校验通过、像素渲染未验证”，不能宣称模板视觉已通过。

### 6. 交付

交付用户指定目录；未指定时，在工作区创建独立模板目录，不直接写入用户的应用模板库。

至少提供：

- `config.json`；
- 配置引用的图片和字体资源；
- 第三方字体的许可文件；
- 模板结构摘要、关键布局决策、使用的 EXIF Key；
- 校验命令及结果；
- 已完成的渲染场景和仍未验证的限制。

## 不可违反的规则

- 所有根节点必须是 Absolute；Flow 节点必须是 Static。
- Flow 节点 Transform 必须保持默认值；缩放、旋转和平移只用于 Absolute。
- 画布尺寸手柄写入 `Style.Width/Height` 或文字 `FontSize`、线条长度；文字左右手柄不得改字号，角点必须按文字内容尺寸计算，提交后 `Style.Height` 保持 Auto；Logo 侧边手柄不得联动另一轴，角点按比例锁定决定是否等比，渲染内容必须填满图片框且不保留 contain 空白；`Transform.ScaleX/Y` 仅作为高级整体缩放保留，Resize 保持当前 Scale 不变。
- 分割线属性面板只提供方向、长度、粗细和颜色；长度与主轴尺寸是同一个属性，粗细与垂直轴尺寸是同一个属性。拖动线条或选框移动整个节点，拖动长度手柄只改变主轴尺寸并保持另一轴坐标稳定。
- `Gap/Margin/Padding` 都相对画布短边，不相对当前容器。
- `WMText.LetterSpacing` 相对画布短边，推荐范围 `-1–3`；它改变本征宽度，必须重新做最长文本压力测试。
- 任何机型、镜头、曝光、拍摄时间、GPS 或照片编号节点必须绑定真实 EXIF Key；固定示例值不能作为模板内容交付。
- 通用模板直接显示坐标时使用 `GPSLatitude` 与 `GPSLongitude` 两个片段；`LongitudeLatitude` 会进入地址解析和授权流程，只在用户明确要求省市/地址时使用。
- 动态文本组合必须让固定图标和线条 `Flex=None`，让文本或文本组 `Flex=Initial`。
- 玻璃卡片使用 `ContainerProperties.EnableGaussianBlur/GaussianDeep` 读取容器下方已经合成的像素；背景色必须保留透明度，否则模糊会被不透明填充遮住。
- `AutoSetLogo=true` 在普通模板渲染中会按 EXIF `Make` 查找品牌图，但当前 `WMZipedTemplate` 分支仍按 `WMLogo.Path` 读取压缩包图片。生成可导入压缩模板或市场资源时，必须同时提供可打包的 `Path` 作为固定/回退 Logo，或明确报告当前渲染器限制；不得把空 `Path` 的自动 Logo 宣称为完整交付。
- `Overflow=Hidden` 会裁切内容；需要溢出可见时必须由用户场景证明合理。
- 层面板顺序、Flex 顺序和 `PNode.SEQ` 必须一致。
- 不生成循环、孤儿节点、重复 ID、重复同父 SEQ 或超出两层容器的配置。
- 只接受并输出 `LayoutSchemaVersion=2` 配置。
