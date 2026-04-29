# 仿色功能重构方案

**日期**: 2026-04-02  
**状态**: 已完成

---

## 1. 概述

仿色功能的核心目标是：**读取参考图的调色风格，将该风格套用到其他图片上**。

用户选择一张具有理想色调的参考图片，系统分析其色彩特征，然后将这种色彩风格"转移"到目标图片上，使目标图片呈现出与参考图相似的色彩氛围。

---

## 2. 问题诊断

### 旧实现的核心缺陷

| 问题类型 | 具体描述 |
|---------|---------|
| **算法层** | 在 RGB 空间使用直方图 CDF 匹配 + 启发式参数提取，信息从高维（百万像素）降到低维（8 参数 + 4×5 曲线点）不可逆 |
| **色温计算** | 仅用 R/B 比例，忽略 G 通道，结果不准确 |
| **分析-应用不对称** | `ColorAnalyzer` 提取的参数含义与 `ColorApplier` 应用的参数含义不对应 |
| **色彩空间错误** | RGB 空间不是感知均匀的，在其中做统计分析和调色会导致不自然的结果 |

---

## 3. 重构方案：Reinhard 色彩转移算法（CIELAB）

采用经典的 **Reinhard Color Transfer** 算法，在感知均匀的 CIELAB 色彩空间中进行色彩转移。

### 3.1 核心原理

1. 将参考图和目标图都转换到 **CIELAB 色彩空间**
2. 计算两者在 L\*、a\*、b\* 三个通道上的**均值**和**标准差**
3. 将目标图的颜色分布"变形"为参考图的颜色分布：

```
pixel_new = (pixel - mean_target) × (std_ref / std_target) + mean_ref
```

4. 转换回 RGB 空间

### 3.2 增强措施

| 措施 | 说明 |
|-----|------|
| **分区域转移** | 将像素按亮度分为暗部（L<40）/ 中间调（40-70）/ 高光（>70）三个区域，各自独立转移，边界平滑混合 |
| **强度控制** | 0-100% 混合原图与转移结果，默认 80% |
| **亮度保留模式** | 可选只转移色彩（a\*、b\*）不改变亮度（L\*） |

---

## 4. 技术实现

### 4.1 数据模型

**文件**: `Watermark.Shared/Models/WMColorPreset.cs`

#### WMColorPreset 类

```csharp
public class WMColorPreset
{
    public string Id { get; set; }
    public string Name { get; set; }
    public DateTime CreatedAt { get; set; }

    // 参考图 LAB 通道统计量（全局）
    public ChannelStats GlobalL { get; set; }  // L* 通道
    public ChannelStats GlobalA { get; set; }  // a* 通道
    public ChannelStats GlobalB { get; set; }  // b* 通道

    // 分区域统计（暗部/中间调/高光）
    public RegionStats Shadows { get; set; }
    public RegionStats Midtones { get; set; }
    public RegionStats Highlights { get; set; }

    // 用户可调参数
    public float Strength { get; set; } = 80f;           // 转移强度 0-100
    public bool PreserveLuminance { get; set; } = false; // 是否保留原图亮度
    public ColorTransferMode Mode { get; set; }          // 转移模式
}
```

#### 辅助类型

```csharp
// 单通道统计量（均值和标准差）
public class ChannelStats
{
    public float Mean { get; set; }
    public float StdDev { get; set; }
}

// 区域统计量（包含 L、a、b 三个通道的统计信息）
public class RegionStats
{
    public ChannelStats L { get; set; }
    public ChannelStats A { get; set; }
    public ChannelStats B { get; set; }
    public int PixelCount { get; set; }
}

// 色彩转移模式
public enum ColorTransferMode
{
    Full,              // 全局 Reinhard 转移
    RegionalAdaptive   // 分区域自适应转移
}
```

### 4.2 分析引擎

**文件**: `Watermark.Shared/Models/ColorAnalyzer.cs`

#### 核心流程

1. **加载参考图**，遍历像素
2. **RGB → XYZ → CIELAB 转换**（D65 白点）
3. **计算 L\*、a\*、b\* 三通道的全局均值和标准差**
4. **按亮度 L\* 分区**（暗部 L<40、中间调 40-70、高光 >70），分别计算各区域统计量
5. **返回 `WMColorPreset` 对象**

#### 色彩空间转换

**sRGB → CIELAB 转换流程**:

```
sRGB [0-255] 
    ↓ 归一化到 [0,1]
    ↓ sRGB gamma 解码
Linear RGB [0,1]
    ↓ D65 白点矩阵变换
XYZ
    ↓ 归一化 + f(t) 函数
CIELAB (L*, a*, b*)
```

**关键常数**:

```csharp
// D65 白点参考值
const float Xn = 0.95047f;
const float Yn = 1.0f;
const float Zn = 1.08883f;

// RGB → XYZ 矩阵
X = 0.4124564 * R + 0.3575761 * G + 0.1804375 * B
Y = 0.2126729 * R + 0.7151522 * G + 0.0721750 * B
Z = 0.0193339 * R + 0.1191920 * G + 0.9503041 * B
```

#### 性能优化

- **超大图缩放**: 对超过 2000px 的图片进行降采样分析
- **并行处理**: 使用 `Parallel.For` 并行遍历像素
- **unsafe 指针**: 直接操作位图内存

### 4.3 应用引擎

**文件**: `Watermark.Shared/Models/ColorApplier.cs`

#### Reinhard 转移算法

对目标图每个像素执行：

```csharp
// 1. RGB → LAB
ColorAnalyzer.RgbToLab(r, g, b, out float L, out float a, out float labB);

// 2. Reinhard 转移
float newL = (L - targetMeanL) * (refStdL / targetStdL) + refMeanL;
float newA = (a - targetMeanA) * (refStdA / targetStdA) + refMeanA;
float newB = (b - targetMeanB) * (refStdB / targetStdB) + refMeanB;

// 3. 亮度保留（可选）
if (preserveLuminance) newL = L;

// 4. 强度混合
newL = L + (newL - L) * strength;
newA = a + (newA - a) * strength;
newB = b + (newB - b) * strength;

// 5. LAB → RGB
ColorAnalyzer.LabToRgb(newL, newA, newB, out byte finalR, out byte finalG, out byte finalB);
```

#### Sigmoid 边界平滑

分区域转移时，使用 sigmoid 函数在区域边界实现平滑过渡：

```csharp
private static float Sigmoid(float x)
{
    return 1f / (1f + MathF.Exp(-SmoothingK * x));
}
```

其中 `SmoothingK = 0.3f` 控制过渡的陡峭程度。

#### 防护措施

| 措施 | 实现 |
|-----|------|
| **防止除零** | 当 `targetStd < Epsilon` 时设为 `Epsilon` (1e-6f) |
| **标准差比例限制** | 限制在 [0.1, 10.0] 范围内，防止极端变换 |
| **Gamut 映射** | LAB → RGB 转换时 `Math.Clamp` 限制到 [0, 255] |

### 4.4 UI 组件

**文件**: `Watermark.Win/BlazorPages/ColorPresetView.razor`

#### 新界面布局

```
+-------------------------------+
| [选择参考图]  [刷新]           |
+-------------------------------+
| [参考图缩略图预览 120x80]      |
+-------------------------------+
| 当前预设: xxx                  |
+-------------------------------+
| 转移强度  =====[80%]========   |
| 转移模式  [全局] [分区域自适应] |
| 保留原图亮度          [开关]   |
+-------------------------------+
| [应用到当前图片]               |
| [保存为预设]                   |
+-------------------------------+
| 已保存的预设                   |
| > 日落暖调  03/30 14:20  [x]  |
| > 胶片冷调  03/29 10:15  [x]  |
+-------------------------------+
```

#### 核心变化

- **移除**: 旧的 8 个滑块（亮度/对比度/饱和度/色温/色调/曝光/高光/阴影）
- **新增**: 参考图缩略图预览
- **新增**: 强度滑块（0-100%，默认 80%）
- **新增**: 转移模式切换（全局 / 分区域自适应）
- **新增**: 亮度保留开关
- **保留**: 预设列表的保存/加载/删除功能

---

## 5. 修改的文件列表

| 文件路径 | 操作 | 说明 |
|---------|------|------|
| `Watermark.Shared/Models/WMColorPreset.cs` | **重写** | 新数据模型，存储 LAB 统计量 |
| `Watermark.Shared/Models/ColorAnalyzer.cs` | **重写** | LAB 空间统计分析，sRGB↔XYZ↔CIELAB 转换 |
| `Watermark.Shared/Models/ColorApplier.cs` | **重写** | Reinhard 色彩转移算法实现 |
| `Watermark.Win/BlazorPages/ColorPresetView.razor` | **重写** | 新 UI 设计，移除旧滑块 |
| `Watermark.Win/BlazorPages/MainView.razor` | **小改** | 更新 `ApplyColorPreset` 方法签名 |
| `Watermark.Shared/Models/PresetManager.cs` | **不变** | JSON 序列化兼容新模型 |

---

## 6. 注意事项

### 6.1 旧预设不兼容

由于数据模型完全重写，**旧版本保存的预设文件将无法被新版本正确解析**。

建议处理方式：
- 用户升级后需重新从参考图创建预设
- 可考虑添加迁移工具或版本检测

### 6.2 调优建议

| 参数 | 建议值 | 说明 |
|-----|-------|------|
| **转移强度** | 70-90% | 过高可能导致颜色过度饱和 |
| **转移模式** | 分区域自适应 | 对高动态范围图片效果更好 |
| **亮度保留** | 视情况 | 开启后只转移色调，不改变明暗对比 |

### 6.3 性能考量

- 对于 4000×3000 像素的图片，应用预设约需 200-500ms
- 分析参考图时会自动降采样到 2000px 以内
- 使用 `Parallel.For` 并行处理，充分利用多核 CPU

### 6.4 已知限制

1. **极端对比度图片**: 当参考图或目标图某区域像素极少时，统计量可能不稳定
2. **HDR 内容**: 当前实现假设 sRGB 色域，HDR 内容可能需要额外处理
3. **透明通道**: Alpha 通道在转移过程中保持不变
