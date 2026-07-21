# V2 模型与序列化参考

## 目录

1. 真实实现边界
2. 顶层配置
3. 扁平层级
4. 统一 Style
5. 枚举值
6. 单位与范围
7. 节点字段
8. EXIF 文本
9. 当前实现注意事项

## 1. 真实实现边界

V2 的当前版本是 `LayoutSchemaVersion = 2`。布局由 `WMStyle` 和 `WMLayoutEngine` 驻留在共享层；预览、绘制和编辑器应复用同一布局结果。

配置文件使用扁平序列化结构，不是嵌套 `Children` JSON：

```text
WMCanvasSerialize
├─ Containers[]
├─ Texts[]
├─ Logos[]
└─ Lines[]
```

每个节点通过 `PNode` 指向父节点。`Global.ReadConfig` 目前只重建两层容器，因此生成器不得输出更深的容器树。

## 2. 顶层配置

新模板至少提供这些字段：

| 字段 | 说明 |
|---|---|
| `ID` | 模板 ID，建议 32 位大写十六进制 |
| `Name` | 中文模板名 |
| `LayoutSchemaVersion` | 固定 `2` |
| `CanvasType` | `0` 普通，`1` 拼图/自定义画布 |
| `CustomWidth/CustomHeight` | `CanvasType=1` 时的参考尺寸 |
| `BorderThickness` | 图片外边距，上右下左数值按画布边距规则计算 |
| `BackgroundColor` | `#RRGGBBAA` |
| `ImageProperties` | 主图片显示、圆角、阴影、模糊等 |
| `BorderSameWidth` | 外边距等宽配置 |
| `FrameProperties` | 外框配置 |
| `Containers/Texts/Logos/Lines` | 必须存在且为数组，空类型写 `[]` |

不要在配置里写画布 `Path`；`WMCanvas.Path` 被 `JsonIgnore`，设计图片由应用会话提供。

## 3. 扁平层级

`PNode`：

```json
"PNode": { "SEQ": 0, "PID": "0" }
```

- 顶级容器：`PID = "0"`。
- 二级容器：`PID = 顶级容器 ID`。
- 叶子节点：`PID = 直接父容器 ID`。
- `SEQ` 从 0 开始，在同一父节点内连续且唯一。
- 视觉顺序、层面板顺序和 Flex 顺序都使用 `SEQ`；没有额外 `order`。
- 同一父节点下，不同节点类型会在读取后合并并按 `SEQ` 排序。

当前可持久化深度：

```text
顶级容器
├─ 文本 / Logo / 分割线
└─ 二级容器
   └─ 文本 / Logo / 分割线
```

不要在二级容器下继续放容器。

## 4. 统一 Style

所有容器和叶子节点都使用同一结构：

```json
"Style": {
  "Position": 0,
  "Width": { "Unit": 0, "Value": 0.0 },
  "Height": { "Unit": 0, "Value": 0.0 },
  "Margin": { "Bottom": 0.0, "Left": 0.0, "Right": 0.0, "Top": 0.0 },
  "Padding": { "Bottom": 0.0, "Left": 0.0, "Right": 0.0, "Top": 0.0 },
  "Overflow": 1,
  "Top": null,
  "Right": null,
  "Bottom": null,
  "Left": null,
  "ZIndex": 0,
  "Flex": 1,
  "FlexDirection": 0,
  "FlexReverse": false,
  "JustifyContent": 0,
  "AlignItems": 1,
  "Gap": 0.0,
  "Transform": {
    "OffsetXPercent": 0.0,
    "OffsetYPercent": 0.0,
    "ScaleX": 1.0,
    "ScaleY": 1.0,
    "Rotation": 0.0
  }
}
```

叶子节点的 Flex-container 字段可以保留默认值；没有 children 时不生效。

## 5. 枚举值

JSON 使用 Newtonsoft 默认数值枚举。生成时使用数值，确保读取行为稳定。

| 字段 | 数值 | 语义 |
|---|---:|---|
| `Position` | 0 | Static，参加父 Flex |
|  | 1 | Absolute，脱离 Flex |
| `Width/Height.Unit` | 0 | Auto |
|  | 1 | Percent |
| `Flex` | 0 | None：`0 0 auto` |
|  | 1 | Initial：`0 1 auto` |
|  | 2 | Fill：`1 1 0%` |
| `FlexDirection` | 0 | Horizontal / row |
|  | 1 | Vertical / column |
| `JustifyContent` | 0 | Start |
|  | 1 | Center |
|  | 2 | End |
| `AlignItems` | 0 | Start |
|  | 1 | Center |
|  | 2 | End |
|  | 3 | Baseline |
| `Overflow` | 0 | Visible |
|  | 1 | Hidden |
| `CanvasType` | 0 | Normal |
|  | 1 | Split |
| 组件 `Orientation` | 0 | Horizontal |
|  | 1 | Vertical |

`FlexReverse=true` 等价于 `row-reverse` 或 `column-reverse`，只改变视觉前进方向，不改变 `SEQ`。

## 6. 单位与范围

| 属性 | 当前实现单位 | 有效范围 |
|---|---|---|
| 根容器 `Width/Height/inset` | 画布宽高百分比 | 尺寸 0–100；inset -25–125 |
| 内部 Absolute `Width/Height/inset` | 父内容区百分比 | 同上 |
| `Margin` | 画布短边百分比 | -25–25 |
| `Padding/Gap` | 画布短边百分比 | 0–25 |
| `Style.Transform.ScaleX/Y` | 比例 | 0.1–4 |
| `Style.Transform.Rotation` | 度 | -180（含）至 180（不含） |
| `WMText.FontSize` | 画布短边百分比 | 编辑器 0.1–25 |
| `WMText.LetterSpacing` | 画布短边百分比 | -1–3，默认 0 |
| `WMLogo.Percent` | 画布短边百分比 | 建议 >0，常用 2–12 |
| `WMLine.Percent` | 沿线方向占父容器百分比 | 1–100 |
| `WMLine.Thickness` | 设计像素，随输出比例缩放 | 编辑器 1–20 |
| 文本 `BorderWidth/Padding/Radius` | 设计像素，随输出比例缩放 | 编辑器宽 0–20、内边距/圆角 0–60 |

`Width/Height` 只有 Auto 和 Percent，没有像素、Fill、Min/Max 或 flex-basis。`Flex=Fill` 是主轴剩余空间预设，不是尺寸单位。

## 7. 节点字段

### 所有节点

保留：`Enabled`、`Name`、`ID`、`PNode`、`Style`、`IsLocked`。

布局属性只写入 `Style`。不要生成节点顶层的 `Margin/Transform`。`Percent` 只作为 Logo 和 Line 的业务尺寸参数使用，文字节点不需要它。

### WMContainer

- `Controls` 在扁平配置中写 `[]` 或省略；层级由 `PNode` 重建。
- `BackgroundColor` 使用 `#RRGGBBAA`，透明可用 `#00000000`。
- `Path` 为容器背景资源的模板相对路径。
- `ContainerProperties` 控制背景图片的裁切、阴影、圆角和模糊。V2 的 `EnableGaussianBlur=true` 会模糊该容器下方已合成的像素，再按容器矩形/圆角裁切；`GaussianDeep` 使用设计像素并随输出比例缩放。
- 背景模糊通常配合半透明 `BackgroundColor`。若背景色 Alpha 为 `FF`，模糊结果会被不透明填充遮住。
- V2 排版读取 `Style.FlexDirection/JustifyContent/AlignItems/Gap`。
- 二级内容组需要按内容收缩时使用 `Style.Width=Auto`。
- 当前完整 auto-height 尚未实现；二级内容组使用明确的 `Style.Height=Percent`。

### WMText

- `Exifs` 是按顺序拼接的片段数组；片段之间自动加入一个空格。
- V2 中，非空 `Key` 没有读取到元数据时，该片段的 Prefix/Value/Suffix 会整体隐藏；不会留下单位或标签占位符。空 `Key` 片段仍用于装饰性固定文字。
- `LetterSpacing` 是全节点字距，按画布短边百分比换算。它在逐字素簇绘制时生效，并参与文字本征宽度、自动宽度、Flex 排列和换行测量。
- 字距应写数值 `0` 表示字体默认间距；不要通过在固定文字中重复插入空格模拟字距。
- `TextWrap=false` 为单行；`true` 时只在文本自身指定可用宽度后换行。
- 使用 `Style.Width=Percent` 给换行文本明确宽度。
- `EnableBorder` 为 true 时，边框、内边距和圆角参与 V2 Flex 测量。
- `BindedContainerId` 为空时使用当前上下文元数据；指定时应引用有效画布/容器元数据来源。

### WMLogo

- `Percent` 是 Logo 短边相对画布短边的百分比。
- `Path` 使用模板目录内相对路径，不允许逃逸目录。
- 普通模板渲染时，`AutoSetLogo=true` 会按相机 `Make` 从应用品牌 Logo 目录查找图片。
- 当前压缩模板 `WMZipedTemplate` 渲染分支不会按 `Make` 改写资源键，而是直接从压缩包图片字典读取 `WMLogo.Path`。需要打包或上架时必须提供非空、可打包的 `Path` 作为固定/回退 Logo；仅本地普通模板可在接受空白回退的前提下省略它。
- `Make` 缺失、未知品牌或资源不存在时，当前渲染器会得到空白占位图；生成后必须覆盖这些样本，不能假定所有相机品牌资源都已安装。
- 连续信息组通常设 `Style.Flex=0`，防止压缩。

### WMLine

- `Orientation=0` 为横线，`Percent` 控制宽度。
- `Orientation=1` 为竖线，`Percent` 控制高度。
- 固定分隔线通常设 `Style.Flex=0`。

## 8. EXIF 文本

常用 Key：

| 内容 | Key | 常见修饰 |
|---|---|---|
| 品牌 | `Make` | 与 `Model` 组合 |
| 机型 | `Model` | 可转大写 |
| 镜头 | `LensModel` | 单独第二行 |
| 光圈 | `FNumber` | `Prefix: "F"` |
| 35mm 焦距 | `FocalLengthIn35mmFilm` | `Suffix: "mm"` |
| 快门 | `ExposureTime` | `Suffix: "S"`，实际值可能为分数 |
| ISO | `ISOSpeedRatings` | `Prefix: "ISO "` |
| 拍摄时间 | `DateTimeOriginal` | 默认使用；设置 `DateTimeFormat` |
| 文件时间 | `DateTime` | 仅用户明确需要文件时间时使用 |
| 原始纬度/经度 | `GPSLatitude` + `GPSLongitude` | 通用模板优先使用两个片段直接显示 |
| 地址解析 | `LongitudeLatitude` | 会进入 `LocationType` 地址解析与会员授权逻辑；仅在明确需要地名时使用 |
| 照片编号 | `ImageNumber` | 部分相机提供的兼容字段 |
| 连拍序号 | `SequenceNumber` | 不是所有相机都会提供 |
| 固定文字 | 空 `Key` | 将文字写在 `Prefix` |

片段结构：

```json
{
  "Prefix": "F",
  "Suffix": "",
  "Value": "",
  "Key": "FNumber",
  "ToLower": false,
  "ToUpper": false,
  "GanZhi": false,
  "LocationType": -1,
  "DateTimeFormat": "yyyy-MM-dd HH:mm:ss",
  "RemoveString": ""
}
```

Prefix/Value/Suffix 各有可选 `WMFontStyle`。当前渲染会统一使用文本节点的 FontSize；局部样式适合改粗斜体、颜色或字体，不要依赖它设置不同字号。

参考图中出现的 `X-T5`、`F2.8`、`35mm`、`1/250S`、`ISO 800`、日期、GPS 和编号都只能作为测试元数据，不能写成固定 Prefix。栏目名、系列标题和纯装饰编号才允许使用空 Key；如果一个节点名称表达“相机参数、曝光、拍摄时间、地点、坐标或编号”，校验器会在它没有绑定 Key 时给出警告。

一个片段不能声明“优先 `DateTimeOriginal`、缺失时回退 `DateTime`”。需要回退时只能由生成前的数据处理、额外节点显隐逻辑或应用代码完成；配置里并列两个 Key 会把两个值同时拼接。

## 9. 当前实现注意事项

- 根容器由 `WMLayoutEngine.Arrange` 定位；内部内容由 `ArrangeContainer` 排列。
- Static 节点的 Transform 会被验证器拒绝并在渲染时忽略。
- `Overflow=Hidden` 的 clip box 是容器内容区；Padding 会缩小它。
- Absolute 子项不参与父 Flex 占用尺寸，可用负 `ZIndex` 做背景。
- V2 容器背景模糊按 ZIndex/绘制顺序读取已经存在的下层像素，不会模糊后绘制的兄弟。根容器和二级容器均支持；模糊不会创建另一条预览或导出管线。
- 容器模型当前没有独立描边字段。需要玻璃卡片细描边时，在目标卡片后放一个略大的同圆角 Absolute 容器作为描边/阴影层，而不是用四条线拼接圆角。
- `Flex=Fill` 平分正的剩余主轴空间；空间不足时只有 `Flex=Initial` 按本征主轴尺寸比例收缩，`Flex=None` 不收缩。
- `JustifyContent` 的负剩余空间不会产生负偏移；过长内容从起点溢出并由 overflow 决定是否裁切。
- Baseline 仅对水平布局有实际意义；Logo/线条的测量基线不等同文字字形基线，复杂混排必须渲染验证。
- 读取配置时数组为 null 会导致整个读取回退为空画布，因此四个节点数组始终显式写出。
- 资源路径应相对模板目录；禁止 `..` 逃逸。Logo 缺失在应用验证器中是 warning，容器背景缺失是 error。
- 结构校验无法证明任意长度文本都完整显示。`Flex=Initial` 只负责收缩可用主轴尺寸，`TextWrap=false` 且 `Overflow=Hidden` 时超长内容最终会裁切。
