# 仿色预设功能设计文档

## 概述

基于直方图匹配和色彩统计分析，实现「仿色预设」功能。用户可从参考图自动分析生成调色参数，也可手动调整，保存为可复用的预设。

## 预设数据结构

```csharp
public class WMColorPreset
{
    public string Id { get; set; }           // 唯一标识
    public string Name { get; set; }         // 预设名称
    public DateTime CreatedAt { get; set; }   // 创建时间

    // 基础调色参数
    public float Brightness { get; set; }     // 亮度: -100 ~ 100
    public float Contrast { get; set; }       // 对比度: -100 ~ 100
    public float Saturation { get; set; }     // 饱和度: -100 ~ 100
    public float Temperature { get; set; }    // 色温: -50 ~ 50 (负=冷/正=暖)
    public float Tint { get; set; }           // 色调: -50 ~ 50 (负=绿/正=紫)
    public float Exposure { get; set; }       // 曝光: -100 ~ 100
    public float Highlights { get; set; }     // 高光: -100 ~ 100
    public float Shadows { get; set; }        // 阴影: -100 ~ 100

    // 曲线参数 (控制点列表，0-255 范围)
    public List<Point> RCurve { get; set; }   // 红通道曲线
    public List<Point> GCurve { get; set; }   // 绿通道曲线
    public List<Point> BCurve { get; set; }   // 蓝通道曲线
    public List<Point> MasterCurve { get; set; } // 亮度曲线
}

public class Point
{
    public float X { get; set; }             // 0-255
    public float Y { get; set; }             // 0-255
}
```

## 自动分析算法

### 输入
参考图片路径

### 输出
`WMColorPreset` 实例

### 算法流程

```
1. 加载参考图到 SKBitmap
   ↓
2. RGB 直方图分析
   - 计算 R/G/B 各通道 0-255 的像素分布
   - 提取均值、标准差、百分位数
   ↓
3. 白平衡检测
   - 查找图像中的中性灰/白色点
   - 计算 R/B 相对于 G 的偏移量
   → 映射到 Temperature 参数
   ↓
4. 对比度计算
   - 计算高光 (p95) 和阴影 (p5) 的差值
   - 与中性灰 (p50) 的距离
   → 映射到 Contrast, Highlights, Shadows 参数
   ↓
5. 饱和度估算
   - 计算 HSL 空间中的平均饱和度
   → 映射到 Saturation 参数
   ↓
6. 色温估算
   - 计算全图 R/B 均值比例
   - 偏离 1.0 的程度 → Temperature
   ↓
7. 曲线提取
   - 将直方图分布转换为累积分布函数 (CDF)
   - 采样 5-8 个控制点生成曲线
   → 填充 RCurve/GCurve/BCurve/MasterCurve
   ↓
8. 返回 WMColorPreset
```

## 应用算法

### 顺序
```
原图 → 曲线变换 → 基础参数 → 色温/色调 → 返回结果
```

### 曲线变换
```csharp
// 控制点转 LUT (256 字节查找表)
byte[] BuildCurveLUT(List<Point> controlPoints)
{
    var lut = new byte[256];
    // 以 controlPoints 为锚点做分段线性插值
    // 未覆盖区域保持 y=x (不变)
    return lut;
}

// 应用曲线
void ApplyCurve(SKBitmap bitmap, byte[] lut)
{
    // 遍历每个像素，通过 LUT 替换 R/G/B 值
}
```

### 基础参数
```csharp
void ApplyBrightnessContrast(SKBitmap bitmap, float brightness, float contrast)
{
    // brightness: RGB += brightness * 2.55
    // contrast: 以 128 为中心，factor = (259*(C+255))/(255*(259-C))
    //           R = factor * (R - 128) + 128
}

void ApplySaturation(SKBitmap bitmap, float saturation)
{
    // 转换到 HSL 空间
    // S *= (saturation + 100) / 100
    // 转回 RGB
}

void ApplyTemperature(SKBitmap bitmap, float temperature)
{
    // temperature > 0: R *= 1.1, B *= 0.9 (暖)
    // temperature < 0: R *= 0.9, B *= 1.1 (冷)
}
```

## 用户交互流程

```
1. 点击「仿色」Tab → 打开仿色面板
   ↓
2. 选择参考图（从相册选择）
   ↓
3. 系统分析 → 生成预设参数 → 实时预览
   ↓
4. 用户可拖动滑块微调 / 编辑曲线
   ↓
5. 点击「保存预设」→ 输入名称 → 保存
   ↓
6. 预设出现在列表中，点击应用
```

## 预设管理

### 存储
- 路径：`AppPath.PresetsFolder/color/`
- 格式：每个预设一个 `.json` 文件
- 文件名：`{guid}.json`

### 操作
- 创建：分析参考图生成 / 手动调整参数保存
- 读取：启动时扫描目录加载所有预设
- 更新：修改参数后保存
- 删除：删除对应 JSON 文件

## 文件结构

```
Watermark.Shared/
├── Models/
│   ├── WMColorPreset.cs          # 预设数据模型
│   ├── ColorAnalyzer.cs          # 参考图分析引擎
│   ├── ColorApplier.cs           # 参数应用引擎
│   └── PresetManager.cs          # 预设增删改查
Watermark.Win/
├── BlazorPages/
│   ├── ColorPresetView.razor     # 仿色面板 UI
│   └── ColorPresetView.razor.css # 样式
Watermark.Andorid/
├── Views/
│   └── ColorPresetPage.xaml      # Android 原生页面 (可选)
```

## 依赖

- SkiaSharp (已引入)
- System.Text.Json (内置)

## 实现顺序

1. `WMColorPreset` 数据模型
2. `ColorApplier` 参数应用引擎（核心）
3. `ColorAnalyzer` 参考图分析引擎
4. `PresetManager` 预设管理
5. `ColorPresetView.razor` UI 面板
6. 集成到主界面
