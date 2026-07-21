# V2 布局配方

## 目录

1. 选择 Flow 或 Absolute
2. 顶级区域锚定
3. 左侧连续信息
4. 右侧动态组合
5. 双行文本组
6. 上下基准连续布局
7. 基线混排
8. 背景、印章和自由装饰
9. 字体字面与字距校准
10. 玻璃卡片与纸张纹理
11. 动态内容压力测试

## 1. 选择 Flow 或 Absolute

使用 Flow（Static）：

- 文本长度会随 EXIF 改变；
- 组件必须保持连续且不能互相覆盖；
- 图标、分割线和文字需要整体左移或右移；
- 双行文本需要作为一个内容组参与外层布局。

使用 Absolute：

- 背景需要填满父容器但不挤占正文；
- 印章、角标或装饰固定在某个角；
- 节点允许与正文覆盖且有明确层叠关系；
- 用户明确要求自由定位。

不要为了视觉微调把动态正文改成 Absolute。先调整父容器的 Flex，再用 Gap 和局部 Margin。

## 2. 顶级区域锚定

顶级容器固定 `Position=Absolute`。

### 左下区域

```text
Left=0%, Bottom=0%, Width=50%, Height=13%
```

### 右下区域

```text
Right=0%, Bottom=0%, Width=50%, Height=13%
```

### 右上印章区域

```text
Right=2%, Top=2%, Width=10%, Height=10%, ZIndex=10
```

### 拉伸背景

将 `Width/Height` 设为 Auto，同时设置 `Top=Right=Bottom=Left=0%`。只对 Absolute 节点使用。

避免同时保存互相矛盾的定位来源。例如右锚定节点优先写 `Right`，不再写 `Left`；需要拉伸时才同时写两侧 inset。

## 3. 左侧连续信息

场景：左下角两行相机信息，内容从左向右或从上向下增长。

外层根容器：

```text
Position=Absolute
Left=0%, Bottom=0%, Width=50%, Height=13%
FlexDirection=Vertical
FlexReverse=false
JustifyContent=Center
AlignItems=Start
Gap=0.6% 短边
Padding.Left=2% 短边
```

两行文本都使用 `Flex=Initial`、`Width=Auto`。第一行较粗，第二行较小。文本增长时保持左边缘，不覆盖兄弟节点。

## 4. 右侧动态组合

场景：

```text
图标 ｜ 竖线 ｜ 动态文本组
```

要求最右文字边缘保持在右内边距，文字变长时图标和竖线向左移动。

外层容器：

```text
FlexDirection=Horizontal
FlexReverse=false
JustifyContent=End
AlignItems=Center
Gap=1–2% 短边
Padding.Right=2% 短边
Overflow=Hidden
```

子项顺序必须是：

```text
SEQ 0 图标       Flex=None
SEQ 1 竖分割线   Flex=None
SEQ 2 文本组     Flex=Initial
```

文本组使用二级容器：

```text
Position=Static
Width=Auto
Height=100%
FlexDirection=Vertical
JustifyContent=Center
AlignItems=End（两行右边缘一致）
```

如果文本组内部的两行需要左边缘一致，将 `AlignItems` 改为 `Start`。外层的右锚定由 `JustifyContent=End` 负责，内层 `AlignItems` 只决定两行彼此的横向对齐，二者不要混用。

`LetterSpacing` 会增加文本组的本征宽度，因此也会把左侧固定图标和分割线向左推。需要匹配设计稿时，先校准字体与字号，再用字距修正字面宽度，最后才用局部 Margin 调整单个固定组件；每一步都重新核对最右文字边缘是否保持不动。

不要使用 `row-reverse` 来“看起来右对齐”，否则视觉顺序与用户描述容易相反。只有用户明确要求从右向左读取组件时才使用 `FlexReverse=true`。

如果动态文本必须单行：`TextWrap=false`，父容器 `Overflow=Hidden`。如果允许换行：给文本明确百分比宽度、设 `TextWrap=true`，并为文本组预留足够高度。

## 5. 双行文本组

双行文本先放进一个二级容器，再让整个组参加外层 Flex。不要把两行与 Logo、分割线全部放在同一 row 中。

```text
二级文本组：column + center + start + 小 Gap
├─ 主信息：较大、粗体、单行
└─ 次信息：较小、常规、单行或有限换行
```

这样外层只处理“一个文本组”的本征宽高，文字变化不会破坏 Logo 与分割线的连续关系。

同组两行需要右对齐时，将文本组 `AlignItems=End`；需要居中时使用 `Center`。不要用每行不同的 X 偏移模拟对齐。

## 6. 上下基准连续布局

### 从顶部向下增长

```text
FlexDirection=Vertical
FlexReverse=false
JustifyContent=Start
```

### 从底部向上增长

```text
FlexDirection=Vertical
FlexReverse=true
JustifyContent=Start
```

`FlexReverse` 只改变视觉前进方向，`SEQ` 仍保持图层面板的逻辑顺序。生成后必须在图层树和画布同时核对。

横向从右向左同理使用 `Horizontal + FlexReverse=true`，但右侧动态组合通常更适合普通 row + `JustifyContent=End`。

## 7. 基线混排

适合一行中有不同字号文字：

```text
FlexDirection=Horizontal
AlignItems=Baseline
```

如果同一行包含 Logo 或竖线，优先把不同字号文字先放进二级文本组，再让图标/线条与文本组 `AlignItems=Center`。当前 Logo/线条测量基线不是排版字形基线，直接 Baseline 混排需要像素验证。

## 8. 背景、印章和自由装饰

### 容器背景

```text
Position=Absolute
Top=Right=Bottom=Left=0%
Width=Auto, Height=Auto
ZIndex=-1
```

背景不参与 Flex 测量。父容器保持 `Overflow=Hidden` 时，背景被限制在内容区。

### 右上印章

```text
Position=Absolute
Top=0%, Right=0%
Width/Height=Auto
ZIndex=1
Transform 仅在确需缩放或旋转时设置
```

### 自由装饰

使用相对父内容区的 inset 和百分比尺寸。开启 `Overflow=Visible` 前确认装饰不会越过画布或遮挡其他顶级区域。

## 9. 字体字面与字距校准

参考图中的字体宽度不能只靠字号匹配。按此顺序校准：

1. 选择可打包且许可明确的字体文件；不要依赖只在当前电脑存在的系统字体。
2. 用字号匹配字面高度和行高。
3. 用 `LetterSpacing` 匹配整行宽度；单位是画布短边百分比，推荐 `-1–3`。
4. 重新检查同一文本组的 Auto 宽度、右/左锚点和兄弟组件位置。
5. 打包字体许可文件，并在横图、竖图上验证。

不要把连续正文转成图片来获得字体外观，也不要在 EXIF 片段之间堆叠手工空格。字距参与真实测量，手工空格会污染内容语义并让动态字段不可预测。

## 10. 玻璃卡片与纸张纹理

### 真实玻璃卡片

容器本身：

```text
Position=Absolute
BackgroundColor=#090B0DA0  # 必须半透明
ContainerProperties.EnableGaussianBlur=true
ContainerProperties.GaussianDeep=8–16 设计像素
ContainerProperties.EnableRadius=true
Overflow=Hidden
```

当前容器没有独立描边属性。需要 1px 左右的圆角描边和阴影时，在卡片后增加一个略大的 Absolute 容器：

```text
描边层：ZIndex=-1，宽高各增加约 0.1–0.4%，同一圆角，低 Alpha 灰色，可启用阴影
内容层：ZIndex=0，半透明背景 + 背景模糊，不重复阴影
```

验收时必须使用有岩石、树叶、文字或棋盘格等高频细节的源图。检查卡片内细节变柔、卡片外像素不变、圆角外没有模糊泄漏；仅比较纯色或全图平均差异无效。

### 只给边框增加纸张纹理

不要用一张全画布纹理覆盖照片。为上、右、下、左留白分别创建四个 `Position=Absolute、ZIndex<0` 的容器，引用同一张可平铺/可裁切纸张资源，尺寸只覆盖各自留白区。正文和注册点使用更高 ZIndex。这样纹理不会改变照片主体，也不参与正文 Flex 测量。

### 参考图局部验收

有设计稿时为每个关键区域记录 ROI，例如底栏、卡片或四周边框。至少比较：

- 容器边界和圆角；
- 文字首尾坐标与行基线；
- Logo 中心和实际像素尺寸；
- 分割线起止点；
- 动态文本变长前后的锚定边漂移。

全图 MAE 容易被照片主体稀释，只能作为附加指标，不能据此宣布模板一致。

## 11. 动态内容压力测试

至少验证以下样本：

```text
短机型：X-T5
长机型：HASSELBLAD X2D 100C
短镜头：35mm F2
长镜头：SIGMA 18-50mm F2.8 DC DN CONTEMPORARY
曝光：1/8000S、0.2S、30S
日期：2024-04-04、2024-04-04 19:15:43
```

检查：

- 所有照片信息节点是否绑定了真实 EXIF Key，而不是把参考图示例值写进 Prefix；
- 更换两组元数据后，机型、曝光、时间、GPS 和编号像素是否随之变化；
- 元数据缺失时，动态片段的前后缀是否随值整体隐藏；
- 锚定边是否保持不动；
- 固定 Logo/分割线是否保持尺寸；
- 文本增长是否只推动允许移动的兄弟；
- `Flex=Initial` 收缩后是否裁切或按预期换行；
- 横图、竖图中画布短边变化是否保持字号和 Gap 比例；
- `Make` 为空和未知品牌时，自动 Logo 是否有可接受的固定/空白回退；
- 负 Margin 是否造成不可控重叠；能用 Gap/分组解决时移除负 Margin。

把“最长样本”写进模板交付说明。超过该样本的文本若被 `Overflow=Hidden` 裁切，属于当前明确边界；不要把压力测试结论扩大为任意长度保证。
