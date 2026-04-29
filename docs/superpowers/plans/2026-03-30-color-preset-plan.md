# 仿色预设功能实现计划

> **注意**：本计划不包含 git 提交，所有代码变更保留在工作区供后续回顾。

**目标**：实现仿色预设功能，用户可从参考图自动分析生成调色参数，也可手动调整，保存为可复用预设。

**架构**：采用直方图匹配 + 色彩统计分析，在 SkiaSharp 上扩展。预设存储为 JSON 文件。

**技术栈**：SkiaSharp（已引入）、System.Text.Json（内置）

---

## 文件结构

```
Watermark.Shared/Models/
├── WMColorPreset.cs           # 预设数据模型
├── ColorApplier.cs            # 调色参数应用引擎
├── ColorAnalyzer.cs           # 参考图分析引擎
└── PresetManager.cs           # 预设增删改查

Watermark.Shared/Models/
└── WMAppPath.cs               # 修改：添加 PresetsFolder 路径

Watermark.Shared/Models/
└── Global.cs                  # 修改：添加 PresetsFolder 初始化

Watermark.Win/BlazorPages/
├── ColorPresetView.razor      # 仿色面板 UI
└── ColorPresetView.razor.css  # 仿色面板样式
```

---

## Task 1: 添加 PresetsFolder 路径

**文件：**
- 修改：`Watermark.Shared/Models/WMAppPath.cs:3-14`
- 修改：`Watermark.Shared/Models/Global.cs:22-41`

**步骤：**

- [ ] Step 1: 修改 `WMAppPath.cs`，添加 `PresetsFolder` 属性

```csharp
namespace Watermark.Shared.Models
{
    public class WMAppPath
    {
        public string BasePath { get; set; }

        public static string AppId = "DFM";
        public string TemplatesFolder { get; set; }
        public string ThumbnailFolder { get; set; }
        public string LogoesFolder { get; set; }
        public string OutputFolder { get; set; }
        public string MarketFolder { get; set; }
        public string FontFolder { get; set; }
        public string PresetsFolder { get; set; }  // 新增
    }
}
```

- [ ] Step 2: 修改 `Global.cs` 的 `CP` 和 `AP` 初始化，添加 `PresetsFolder`

在 `CP` 静态初始化块中（约第 30 行后）添加：
```csharp
FontFolder = AppDomain.CurrentDomain.BaseDirectory + "fonts" + Path.DirectorySeparatorChar,
PresetsFolder = AppDomain.CurrentDomain.BaseDirectory + "Presets" + Path.DirectorySeparatorChar + "color" + Path.DirectorySeparatorChar
```

在 `AP` 静态初始化块中（约第 40 行后）添加：
```csharp
FontFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal) + Path.DirectorySeparatorChar + WMAppPath.AppId + Path.DirectorySeparatorChar + "fonts" + Path.DirectorySeparatorChar,
PresetsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal) + Path.DirectorySeparatorChar + WMAppPath.AppId + Path.DirectorySeparatorChar + "Presets" + Path.DirectorySeparatorChar + "color" + Path.DirectorySeparatorChar
```

---

## Task 2: 创建 WMColorPreset 数据模型

**文件：**
- 创建：`Watermark.Shared/Models/WMColorPreset.cs`

**步骤：**

- [ ] Step 1: 创建 `WMColorPreset.cs`，包含基础调色参数和曲线参数

```csharp
using System;
using System.Collections.Generic;

namespace Watermark.Shared.Models
{
    public class WMColorPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "未命名预设";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // 基础调色参数 (-100 ~ 100)
        public float Brightness { get; set; } = 0;
        public float Contrast { get; set; } = 0;
        public float Saturation { get; set; } = 0;
        public float Temperature { get; set; } = 0;  // 负=冷/正=暖 (-50~50)
        public float Tint { get; set; } = 0;         // 负=绿/正=紫 (-50~50)
        public float Exposure { get; set; } = 0;     // -100~100
        public float Highlights { get; set; } = 0; // -100~100
        public float Shadows { get; set; } = 0;      // -100~100

        // 曲线参数 (控制点列表，X和Y范围 0-255)
        public List<CurvePoint> RCurve { get; set; } = new() { new CurvePoint { X = 0, Y = 0 }, new CurvePoint { X = 255, Y = 255 } };
        public List<CurvePoint> GCurve { get; set; } = new() { new CurvePoint { X = 0, Y = 0 }, new CurvePoint { X = 255, Y = 255 } };
        public List<CurvePoint> BCurve { get; set; } = new() { new CurvePoint { X = 0, Y = 0 }, new CurvePoint { X = 255, Y = 255 } };
        public List<CurvePoint> MasterCurve { get; set; } = new() { new CurvePoint { X = 0, Y = 0 }, new CurvePoint { X = 255, Y = 255 } };
    }

    public class CurvePoint
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}
```

---

## Task 3: 创建 ColorApplier 调色应用引擎

**文件：**
- 创建：`Watermark.Shared/Models/ColorApplier.cs`

**步骤：**

- [ ] Step 1: 创建 `ColorApplier.cs`，实现调色参数应用到 SKBitmap

```csharp
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Watermark.Shared.Models
{
    public class ColorApplier
    {
        /// <summary>
        /// 将预设应用到图片
        /// </summary>
        /// <param name="bitmap">源图片</param>
        /// <param name="preset">预设</param>
        /// <returns>处理后的图片副本</returns>
        public static SKBitmap Apply(SKBitmap bitmap, WMColorPreset preset)
        {
            var result = bitmap.Copy();

            // 应用顺序：曲线 → 基础参数 → 色温/色调
            ApplyCurves(result, preset);
            ApplyBasicParams(result, preset);
            ApplyTemperatureTint(result, preset.Temperature, preset.Tint);

            return result;
        }

        /// <summary>
        /// 同步版本
        /// </summary>
        public static SKBitmap ApplySync(SKBitmap bitmap, WMColorPreset preset)
        {
            return Apply(bitmap, preset);
        }

        /// <summary>
        /// 异步版本
        /// </summary>
        public static Task<SKBitmap> ApplyAsync(SKBitmap bitmap, WMColorPreset preset)
        {
            return Task.Run(() => Apply(bitmap, preset));
        }

        private static void ApplyCurves(SKBitmap bitmap, WMColorPreset preset)
        {
            var rLut = BuildCurveLUT(preset.RCurve);
            var gLut = BuildCurveLUT(preset.GCurve);
            var bLut = BuildCurveLUT(preset.BCurve);
            var masterLut = BuildCurveLUT(preset.MasterCurve);

            IntPtr pixels = bitmap.GetPixels();
            int width = bitmap.Width;
            int height = bitmap.Height;
            int rowBytes = bitmap.RowBytes;

            unsafe
            {
                byte* ptr = (byte*)pixels.ToPointer();
                Parallel.For(0, height, y =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * rowBytes + x * 4;

                        // 先应用通道曲线
                        byte r = rLut[ptr[index + 2]];
                        byte g = gLut[ptr[index + 1]];
                        byte b = bLut[ptr[index]];

                        // 再应用亮度曲线到所有通道
                        r = masterLut[r];
                        g = masterLut[g];
                        b = masterLut[b];

                        ptr[index + 2] = r;
                        ptr[index + 1] = g;
                        ptr[index] = b;
                    }
                });
            }
        }

        /// <summary>
        /// 将控制点列表转换为 256 字节的查找表
        /// </summary>
        private static byte[] BuildCurveLUT(List<CurvePoint> controlPoints)
        {
            var lut = new byte[256];

            if (controlPoints == null || controlPoints.Count < 2)
            {
                // 默认：恒等变换
                for (int i = 0; i < 256; i++) lut[i] = (byte)i;
                return lut;
            }

            // 按 X 排序
            var sorted = new List<CurvePoint>(controlPoints);
            sorted.Sort((a, b) => a.X.CompareTo(b.X));

            // 确保起点是 (0, y) 和终点是 (255, y)
            if (sorted[0].X > 0) sorted.Insert(0, new CurvePoint { X = 0, Y = sorted[0].Y });
            if (sorted[sorted.Count - 1].X < 255) sorted.Add(new CurvePoint { X = 255, Y = sorted[sorted.Count - 1].Y });

            // 分段线性插值填充 LUT
            int cpIndex = 0;
            for (int i = 0; i < 256; i++)
            {
                // 找到当前点所在的线段
                while (cpIndex < sorted.Count - 2 && sorted[cpIndex + 1].X < i)
                {
                    cpIndex++;
                }

                var p1 = sorted[cpIndex];
                var p2 = sorted[cpIndex + 1];

                float t = (p2.X == p1.X) ? 0 : (i - p1.X) / (float)(p2.X - p1.X);
                float y = p1.Y + t * (p2.Y - p1.Y);

                lut[i] = (byte)Math.Clamp(y, 0, 255);
            }

            return lut;
        }

        private static void ApplyBasicParams(SKBitmap bitmap, WMColorPreset preset)
        {
            IntPtr pixels = bitmap.GetPixels();
            int width = bitmap.Width;
            int height = bitmap.Height;
            int rowBytes = bitmap.RowBytes;

            // 预计算参数
            float brightness = preset.Brightness * 2.55f;
            float contrastFactor = (259f * (preset.Contrast + 255f)) / (255f * (259f - preset.Contrast));
            float saturationFactor = (preset.Saturation + 100f) / 100f;
            float exposureFactor = (preset.Exposure + 100f) / 100f;
            float highlightsFactor = preset.Highlights / 100f;
            float shadowsFactor = preset.Shadows / 100f;

            unsafe
            {
                byte* ptr = (byte*)pixels.ToPointer();
                Parallel.For(0, height, y =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * rowBytes + x * 4;

                        // 读取像素
                        float b = ptr[index];
                        float g = ptr[index + 1];
                        float r = ptr[index + 2];
                        float a = ptr[index + 3] / 255f;

                        // 1. 曝光
                        r *= exposureFactor;
                        g *= exposureFactor;
                        b *= exposureFactor;

                        // 2. 高光/阴影调整
                        float luminance = 0.299f * r + 0.587f * g + 0.114f * b;
                        float highlightWeight = Math.Max(0, (luminance - 128) / 128f);
                        float shadowWeight = Math.Max(0, (128 - luminance) / 128f);

                        r += highlightsFactor * highlightWeight * 50;
                        g += highlightsFactor * highlightWeight * 50;
                        b += highlightsFactor * highlightWeight * 50;

                        r += shadowsFactor * shadowWeight * 50;
                        g += shadowsFactor * shadowWeight * 50;
                        b += shadowsFactor * shadowWeight * 50;

                        // 3. 亮度
                        r += brightness;
                        g += brightness;
                        b += brightness;

                        // 4. 对比度
                        r = contrastFactor * (r - 128) + 128;
                        g = contrastFactor * (g - 128) + 128;
                        b = contrastFactor * (b - 128) + 128;

                        // 5. 饱和度 (转换到 HSL)
                        float max = Math.Max(r, Math.Max(g, b));
                        float min = Math.Min(r, Math.Min(g, b));
                        float l = (max + min) / 2f;
                        float s = 0;
                        if (max != min)
                        {
                            float d = max - min;
                            s = l > 128 ? d / (510 - max - min) : d / (max + min);
                        }
                        float gray = l * 2.55f;
                        r = gray + (r - gray) * saturationFactor;
                        g = gray + (g - gray) * saturationFactor;
                        b = gray + (b - gray) * saturationFactor;

                        // 限幅
                        ptr[index] = (byte)Math.Clamp(b, 0, 255);
                        ptr[index + 1] = (byte)Math.Clamp(g, 0, 255);
                        ptr[index + 2] = (byte)Math.Clamp(r, 0, 255);
                    }
                });
            }
        }

        private static void ApplyTemperatureTint(SKBitmap bitmap, float temperature, float tint)
        {
            if (Math.Abs(temperature) < 0.5f && Math.Abs(tint) < 0.5f) return;

            IntPtr pixels = bitmap.GetPixels();
            int width = bitmap.Width;
            int height = bitmap.Height;
            int rowBytes = bitmap.RowBytes;

            float tempFactor = temperature / 50f;  // -1 ~ 1
            float tintFactor = tint / 50f;          // -1 ~ 1

            unsafe
            {
                byte* ptr = (byte*)pixels.ToPointer();
                Parallel.For(0, height, y =>
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * rowBytes + x * 4;

                        // 色温：正=暖(红增蓝减)，负=冷(红减蓝增)
                        ptr[index + 2] = (byte)Math.Clamp(ptr[index + 2] * (1 + tempFactor * 0.2f), 0, 255); // R
                        ptr[index] = (byte)Math.Clamp(ptr[index] * (1 - tempFactor * 0.2f), 0, 255);         // B

                        // 色调：正=紫(绿减红蓝增)，负=绿(绿增红蓝减)
                        ptr[index + 1] = (byte)Math.Clamp(ptr[index + 1] * (1 - tintFactor * 0.2f), 0, 255);   // G
                    }
                });
            }
        }
    }
}
```

---

## Task 4: 创建 ColorAnalyzer 参考图分析引擎

**文件：**
- 创建：`Watermark.Shared/Models/ColorAnalyzer.cs`

**步骤：**

- [ ] Step 1: 创建 `ColorAnalyzer.cs`，实现从参考图自动分析生成预设

```csharp
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Watermark.Shared.Models
{
    public class ColorAnalyzer
    {
        /// <summary>
        /// 从参考图分析生成预设
        /// </summary>
        /// <param name="imagePath">参考图路径</param>
        /// <returns>生成的预设</returns>
        public static WMColorPreset Analyze(string imagePath)
        {
            using var bitmap = SKBitmap.Decode(imagePath);
            if (bitmap == null)
                throw new InvalidOperationException($"无法解码图片: {imagePath}");
            return Analyze(bitmap);
        }

        /// <summary>
        /// 从 SKBitmap 分析生成预设
        /// </summary>
        public static WMColorPreset Analyze(SKBitmap bitmap)
        {
            var preset = new WMColorPreset();

            // 1. 分析直方图
            var histogram = ComputeHistogram(bitmap);

            // 2. 分析白平衡 → 色温
            preset.Temperature = AnalyzeTemperature(bitmap);

            // 3. 分析对比度、高光、阴影
            AnalyzeContrast(histogram, out float contrast, out float highlights, out float shadows);
            preset.Contrast = contrast;
            preset.Highlights = highlights;
            preset.Shadows = shadows;

            // 4. 分析饱和度
            preset.Saturation = AnalyzeSaturation(bitmap);

            // 5. 生成 RGB 曲线
            preset.RCurve = GenerateCurveFromHistogram(histogram.rHistogram);
            preset.GCurve = GenerateCurveFromHistogram(histogram.gHistogram);
            preset.BCurve = GenerateCurveFromHistogram(histogram.bHistogram);
            preset.MasterCurve = GenerateMasterCurve(histogram);

            return preset;
        }

        private static (int[] rHistogram, int[] gHistogram, int[] bHistogram, int[] lumHistogram) ComputeHistogram(SKBitmap bitmap)
        {
            var rHist = new int[256];
            var gHist = new int[256];
            var bHist = new int[256];
            var lumHist = new int[256];

            IntPtr pixels = bitmap.GetPixels();
            int width = bitmap.Width;
            int height = bitmap.Height;
            int rowBytes = bitmap.RowBytes;

            unsafe
            {
                byte* ptr = (byte*)pixels.ToPointer();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * rowBytes + x * 4;
                        byte b = ptr[index];
                        byte g = ptr[index + 1];
                        byte r = ptr[index + 2];

                        rHist[r]++;
                        gHist[g]++;
                        bHist[b]++;

                        // 亮度
                        byte lum = (byte)(0.299f * r + 0.587f * g + 0.114f * b);
                        lumHist[lum]++;
                    }
                }
            }

            return (rHist, gHist, bHist, lumHist);
        }

        private static float AnalyzeTemperature(SKBitmap bitmap)
        {
            IntPtr pixels = bitmap.GetPixels();
            int width = bitmap.Width;
            int height = bitmap.Height;
            int rowBytes = bitmap.RowBytes;

            long sumR = 0, sumB = 0;
            int count = 0;

            unsafe
            {
                byte* ptr = (byte*)pixels.ToPointer();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * rowBytes + x * 4;
                        byte b = ptr[index];
                        byte g = ptr[index + 1];
                        byte r = ptr[index + 2];

                        // 排除过暗或过亮的像素
                        byte max = Math.Max(r, Math.Max(g, b));
                        byte min = Math.Min(r, Math.Min(g, b));
                        if (max < 250 && min > 5)
                        {
                            sumR += r;
                            sumB += b;
                            count++;
                        }
                    }
                }
            }

            if (count == 0) return 0;

            float avgR = sumR / (float)count;
            float avgB = sumB / (float)count;

            // R/B 比例偏离 1.0 表示色温偏移
            float ratio = avgR / Math.Max(avgB, 1f);

            // 映射到 -50 ~ 50
            if (ratio > 1.0f)
                return Math.Min(50, (ratio - 1f) * 30f);  // 偏暖
            else
                return Math.Max(-50, (ratio - 1f) * 30f); // 偏冷
        }

        private static void AnalyzeContrast((int[] rHistogram, int[] gHistogram, int[] bHistogram, int[] lumHistogram) histogram,
            out float contrast, out float highlights, out float shadows)
        {
            var lumHist = histogram.lumHistogram;
            int total = 0;
            for (int i = 0; i < 256; i++) total += lumHist[i];

            // 计算百分位数 p5, p50, p95
            int p5 = GetPercentile(lumHist, total, 5);
            int p50 = GetPercentile(lumHist, total, 50);
            int p95 = GetPercentile(lumHist, total, 95);

            // 对比度：p95 和 p5 的距离
            contrast = Math.Clamp((p95 - p5 - 180) / 2f, -100, 100);

            // 高光：p95 距离 255 有多少
            highlights = Math.Clamp((255 - p95) / 3f, -100, 100);

            // 阴影：p5 距离 0 有多少
            shadows = Math.Clamp((p5 - 0) / 3f, -100, 100);
        }

        private static int GetPercentile(int[] histogram, int total, int percentile)
        {
            int target = total * percentile / 100;
            int sum = 0;
            for (int i = 0; i < 256; i++)
            {
                sum += histogram[i];
                if (sum >= target) return i;
            }
            return 255;
        }

        private static float AnalyzeSaturation(SKBitmap bitmap)
        {
            IntPtr pixels = bitmap.GetPixels();
            int width = bitmap.Width;
            int height = bitmap.Height;
            int rowBytes = bitmap.RowBytes;

            float totalSaturation = 0;
            int count = 0;

            unsafe
            {
                byte* ptr = (byte*)pixels.ToPointer();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * rowBytes + x * 4;
                        byte b = ptr[index];
                        byte g = ptr[index + 1];
                        byte r = ptr[index + 2];

                        float rN = r / 255f, gN = g / 255f, bN = b / 255f;
                        float max = Math.Max(rN, Math.Max(gN, bN));
                        float min = Math.Min(rN, Math.Min(gN, bN));
                        float l = (max + min) / 2f;
                        float s = 0;
                        if (max != min)
                        {
                            s = l > 0.5f ? (max - min) / (2 - max - min) : (max - min) / (max + min);
                        }
                        totalSaturation += s;
                        count++;
                    }
                }
            }

            if (count == 0) return 0;

            float avgSat = totalSaturation / count;
            // 映射到 -100 ~ 100，假设平均饱和度 0.3 为中性
            return Math.Clamp((avgSat - 0.3f) * 200f, -100, 100);
        }

        private static List<CurvePoint> GenerateCurveFromHistogram(int[] histogram)
        {
            // 将直方图转换为 CDF，然后匹配到中性分布
            int total = 0;
            for (int i = 0; i < 256; i++) total += histogram[i];

            var cdf = new float[256];
            int cumsum = 0;
            for (int i = 0; i < 256; i++)
            {
                cumsum += histogram[i];
                cdf[i] = cumsum / (float)total;
            }

            // 采样 5 个控制点
            var points = new List<CurvePoint>();
            int[] samples = { 0, 64, 128, 192, 255 };
            foreach (var s in samples)
            {
                // 目标 CDF（中性分布略有扩展）
                float targetCdf = s / 255f;
                // 找到最接近的输入值
                int match = 0;
                for (int i = 0; i < 256; i++)
                {
                    if (Math.Abs(cdf[i] - targetCdf) < Math.Abs(cdf[match] - targetCdf))
                        match = i;
                }
                points.Add(new CurvePoint { X = s, Y = match });
            }

            return points;
        }

        private static List<CurvePoint> GenerateMasterCurve((int[] rHistogram, int[] gHistogram, int[] bHistogram, int[] lumHistogram) histogram)
        {
            // 亮度曲线：基于亮度直方图
            var lumHist = histogram.lumHistogram;
            int total = 0;
            for (int i = 0; i < 256; i++) total += lumHist[i];

            var cdf = new float[256];
            int cumsum = 0;
            for (int i = 0; i < 256; i++)
            {
                cumsum += lumHist[i];
                cdf[i] = cumsum / (float)total;
            }

            // 计算均值作为参考
            long sum = 0;
            for (int i = 0; i < 256; i++) sum += lumHist[i] * i;
            float mean = sum / (float)total;

            // 如果均值偏暗/偏亮，调整曲线
            var points = new List<CurvePoint>();
            int[] samples = { 0, 64, 128, 192, 255 };
            foreach (var s in samples)
            {
                float targetCdf = s / 255f;
                int match = 0;
                for (int i = 0; i < 256; i++)
                {
                    if (Math.Abs(cdf[i] - targetCdf) < Math.Abs(cdf[match] - targetCdf))
                        match = i;
                }

                // 轻微调整：将 match 值向中性点 128 做一点偏移
                float adjustedY = match + (128 - match) * 0.1f;
                points.Add(new CurvePoint { X = s, Y = Math.Clamp(adjustedY, 0, 255) });
            }

            return points;
        }
    }
}
```

---

## Task 5: 创建 PresetManager 预设管理器

**文件：**
- 创建：`Watermark.Shared/Models/PresetManager.cs`

**步骤：**

- [ ] Step 1: 创建 `PresetManager.cs`，实现预设的 CRUD 操作

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Watermark.Shared.Models
{
    public class PresetManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// 获取预设文件夹路径
        /// </summary>
        public static string GetPresetsFolder()
        {
            var folder = Global.AppPath.PresetsFolder;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            return folder;
        }

        /// <summary>
        /// 加载所有预设
        /// </summary>
        public static List<WMColorPreset> LoadAll()
        {
            var presets = new List<WMColorPreset>();
            var folder = GetPresetsFolder();

            if (!Directory.Exists(folder)) return presets;

            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var preset = JsonSerializer.Deserialize<WMColorPreset>(json, JsonOptions);
                    if (preset != null)
                    {
                        presets.Add(preset);
                    }
                }
                catch
                {
                    // 忽略损坏的文件
                }
            }

            return presets.OrderByDescending(p => p.CreatedAt).ToList();
        }

        /// <summary>
        /// 保存预设
        /// </summary>
        public static async Task SaveAsync(WMColorPreset preset)
        {
            if (string.IsNullOrEmpty(preset.Id))
            {
                preset.Id = Guid.NewGuid().ToString("N");
            }

            var folder = GetPresetsFolder();
            var filePath = Path.Combine(folder, $"{preset.Id}.json");
            var json = JsonSerializer.Serialize(preset, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// 同步保存预设
        /// </summary>
        public static void Save(WMColorPreset preset)
        {
            if (string.IsNullOrEmpty(preset.Id))
            {
                preset.Id = Guid.NewGuid().ToString("N");
            }

            var folder = GetPresetsFolder();
            var filePath = Path.Combine(folder, $"{preset.Id}.json");
            var json = JsonSerializer.Serialize(preset, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// 删除预设
        /// </summary>
        public static bool Delete(string presetId)
        {
            var folder = GetPresetsFolder();
            var filePath = Path.Combine(folder, $"{presetId}.json");

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 根据 ID 获取预设
        /// </summary>
        public static WMColorPreset GetById(string presetId)
        {
            var folder = GetPresetsFolder();
            var filePath = Path.Combine(folder, $"{presetId}.json");

            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<WMColorPreset>(json, JsonOptions);
            }
            return null;
        }
    }
}
```

---

## Task 6: 创建 ColorPresetView.razor UI 面板

**文件：**
- 创建：`Watermark.Win/BlazorPages/ColorPresetView.razor`
- 创建：`Watermark.Win/BlazorPages/ColorPresetView.razor.css`

**步骤：**

- [ ] Step 1: 创建 `ColorPresetView.razor`

```razor
@using System.IO
@using Microsoft.AspNetCore.Components.Forms
@using Watermark.Shared.Models
@inject IClientInstance ClientInstance

<div class="color-preset-panel">
    <div class="preset-header">
        <span class="preset-title">仿色预设</span>
        <MButton Icon Small @onclick="LoadPresets">
            <MIcon>mdi-refresh</MIcon>
        </MButton>
    </div>

    <div class="preset-actions">
        <MButton Block Small Color="primary" OnClick="SelectReferenceImage">
            <MIcon Left>mdi-image-plus</MIcon>
            选择参考图
        </MButton>
    </div>

    @if (currentPreset != null)
    {
        <div class="preset-preview-section">
            <div class="preview-label">当前预设: @currentPreset.Name</div>
            <div class="preview-buttons">
                <MButton Small OnClick="SaveCurrentPreset">
                    <MIcon Left>mdi-content-save</MIcon>
                    保存
                </MButton>
                <MButton Small OnClick="ResetPreset">
                    <MIcon Left>mdi-restore</MIcon>
                    重置
                </MButton>
            </div>
        </div>

        <div class="preset-sliders">
            <div class="slider-group">
                <div class="slider-header">
                    <span>亮度</span>
                    <span class="slider-value">@currentPreset.Brightness</span>
                </div>
                <MSlider @bind-Value="currentPreset.Brightness" Min="-100" Max="100" Step="1" Dense />
            </div>

            <div class="slider-group">
                <div class="slider-header">
                    <span>对比度</span>
                    <span class="slider-value">@currentPreset.Contrast</span>
                </div>
                <MSlider @bind-Value="currentPreset.Contrast" Min="-100" Max="100" Step="1" Dense />
            </div>

            <div class="slider-group">
                <div class="slider-header">
                    <span>饱和度</span>
                    <span class="slider-value">@currentPreset.Saturation</span>
                </div>
                <MSlider @bind-Value="currentPreset.Saturation" Min="-100" Max="100" Step="1" Dense />
            </div>

            <div class="slider-group">
                <div class="slider-header">
                    <span>色温</span>
                    <span class="slider-value">@currentPreset.Temperature</span>
                </div>
                <MSlider @bind-Value="currentPreset.Temperature" Min="-50" Max="50" Step="1" Dense />
            </div>

            <div class="slider-group">
                <div class="slider-header">
                    <span>色调</span>
                    <span class="slider-value">@currentPreset.Tint</span>
                </div>
                <MSlider @bind-Value="currentPreset.Tint" Min="-50" Max="50" Step="1" Dense />
            </div>

            <div class="slider-group">
                <div class="slider-header">
                    <span>曝光</span>
                    <span class="slider-value">@currentPreset.Exposure</span>
                </div>
                <MSlider @bind-Value="currentPreset.Exposure" Min="-100" Max="100" Step="1" Dense />
            </div>

            <div class="slider-group">
                <div class="slider-header">
                    <span>高光</span>
                    <span class="slider-value">@currentPreset.Highlights</span>
                </div>
                <MSlider @bind-Value="currentPreset.Highlights" Min="-100" Max="100" Step="1" Dense />
            </div>

            <div class="slider-group">
                <div class="slider-header">
                    <span>阴影</span>
                    <span class="slider-value">@currentPreset.Shadows</span>
                </div>
                <MSlider @bind-Value="currentPreset.Shadows" Min="-100" Max="100" Step="1" Dense />
            </div>
        </div>

        <div class="preset-apply">
            <MButton Block Color="success" OnClick="ApplyPreset">
                <MIcon Left>mdi-check</MIcon>
                应用到当前图片
            </MButton>
        </div>
    }

    <div class="preset-list-section">
        <div class="section-title">已保存的预设</div>
        @if (presets.Count == 0)
        {
            <div class="empty-message">暂无保存的预设</div>
        }
        else
        {
            @foreach (var preset in presets)
            {
                <div class="preset-item" @onclick="() => SelectPreset(preset)">
                    <div class="preset-item-name">@preset.Name</div>
                    <div class="preset-item-date">@preset.CreatedAt.ToString("MM/dd HH:mm")</div>
                    <MButton Icon Small @onclick.stop="() => DeletePreset(preset)">
                        <MIcon Small>mdi-delete</MIcon>
                    </MButton>
                </div>
            }
        }
    </div>
</div>

@code {
    [Parameter]
    public EventCallback<WMColorPreset> OnPresetApplied { get; set; }

    private List<WMColorPreset> presets = new();
    private WMColorPreset currentPreset;
    private string pendingPresetName = "";

    protected override void OnInitialized()
    {
        LoadPresets();
    }

    private void LoadPresets()
    {
        presets = PresetManager.LoadAll();
        StateHasChanged();
    }

    private async Task SelectReferenceImage()
    {
        var path = await ClientInstance.PickAsync();
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                currentPreset = ColorAnalyzer.Analyze(path);
                currentPreset.Name = "参考图预设";
                StateHasChanged();
            }
            catch (Exception ex)
            {
                // 处理错误
            }
        }
    }

    private void SelectPreset(WMColorPreset preset)
    {
        currentPreset = new WMColorPreset
        {
            Id = preset.Id,
            Name = preset.Name,
            CreatedAt = preset.CreatedAt,
            Brightness = preset.Brightness,
            Contrast = preset.Contrast,
            Saturation = preset.Saturation,
            Temperature = preset.Temperature,
            Tint = preset.Tint,
            Exposure = preset.Exposure,
            Highlights = preset.Highlights,
            Shadows = preset.Shadows,
            RCurve = new List<CurvePoint>(preset.RCurve),
            GCurve = new List<CurvePoint>(preset.GCurve),
            BCurve = new List<CurvePoint>(preset.BCurve),
            MasterCurve = new List<CurvePoint>(preset.MasterCurve)
        };
        StateHasChanged();
    }

    private async Task SaveCurrentPreset()
    {
        if (currentPreset == null) return;

        // 简化：直接使用当前名称保存
        if (string.IsNullOrEmpty(currentPreset.Name))
        {
            currentPreset.Name = "预设 " + DateTime.Now.ToString("HH:mm:ss");
        }

        await PresetManager.SaveAsync(currentPreset);
        LoadPresets();
    }

    private void ResetPreset()
    {
        currentPreset = new WMColorPreset
        {
            Name = "新建预设",
            RCurve = new List<CurvePoint> { new CurvePoint { X = 0, Y = 0 }, new CurvePoint { X = 255, Y = 255 } },
            GCurve = new List<CurvePoint> { new CurvePoint { X = 0, Y = 0 }, new CurvePoint { X = 255, Y = 255 } },
            BCurve = new List<CurvePoint> { new CurvePoint { X = 0, Y = 0 }, new CurvePoint { X = 255, Y = 255 } },
            MasterCurve = new List<CurvePoint> { new CurvePoint { X = 0, Y = 0 }, new CurvePoint { X = 255, Y = 255 } }
        };
        StateHasChanged();
    }

    private async Task ApplyPreset()
    {
        if (currentPreset != null && OnPresetApplied.HasDelegate)
        {
            await OnPresetApplied.InvokeAsync(currentPreset);
        }
    }

    private void DeletePreset(WMColorPreset preset)
    {
        PresetManager.Delete(preset.Id);
        LoadPresets();
        if (currentPreset?.Id == preset.Id)
        {
            currentPreset = null;
        }
    }
}
```

- [ ] Step 2: 创建 `ColorPresetView.razor.css`

```css
.color-preset-panel {
    padding: 12px;
    height: 100%;
    overflow-y: auto;
}

.color-preset-panel::-webkit-scrollbar {
    width: 4px;
}

.color-preset-panel::-webkit-scrollbar-thumb {
    background: rgba(0, 0, 0, 0.12);
    border-radius: 2px;
}

.preset-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 12px;
}

.preset-title {
    font-size: 14px;
    font-weight: 500;
    color: #333;
}

.preset-actions {
    margin-bottom: 12px;
}

.preset-preview-section {
    background: #f5f5f5;
    border-radius: 6px;
    padding: 8px;
    margin-bottom: 12px;
}

.preview-label {
    font-size: 12px;
    color: #666;
    margin-bottom: 6px;
}

.preview-buttons {
    display: flex;
    gap: 8px;
}

.preset-sliders {
    margin-bottom: 12px;
}

.slider-group {
    margin-bottom: 8px;
}

.slider-header {
    display: flex;
    justify-content: space-between;
    font-size: 12px;
    color: #555;
    margin-bottom: 2px;
}

.slider-value {
    min-width: 36px;
    text-align: right;
    font-family: monospace;
}

.preset-apply {
    margin-bottom: 16px;
}

.preset-list-section {
    border-top: 1px solid rgba(0, 0, 0, 0.08);
    padding-top: 12px;
}

.section-title {
    font-size: 12px;
    color: #888;
    margin-bottom: 8px;
}

.empty-message {
    font-size: 12px;
    color: #aaa;
    text-align: center;
    padding: 16px;
}

.preset-item {
    display: flex;
    align-items: center;
    padding: 6px 8px;
    border-radius: 4px;
    cursor: pointer;
    transition: background 0.15s;
    gap: 8px;
}

.preset-item:hover {
    background: rgba(0, 0, 0, 0.04);
}

.preset-item-name {
    flex: 1;
    font-size: 13px;
    color: #333;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.preset-item-date {
    font-size: 11px;
    color: #999;
}
```

---

## Task 7: 将 ColorPresetView 集成到主界面

**文件：**
- 修改：`Watermark.Win/BlazorPages/MainView.razor`

**步骤：**

- [ ] Step 1: 在 MainView.razor 的左侧面板 tabs 中添加「仿色」Tab

在 `MTabs` 区域（约第 426-429 行）添加：

```razor
<MTab Value="@("模板")">模板</MTab>
<MTab Value="@("拼图")">拼图</MTab>
<MTab Value="@("仿色")">仿色</MTab>  <!-- 新增 -->
```

- [ ] Step 2: 在 `MTabsItems` 区域添加仿色面板

在现有 `MTabItem` 之后添加：

```razor
<MTabItem Value="@("仿色")" Transition="" ReverseTransition="">
    <ColorPresetView OnPresetApplied="ApplyColorPreset" />
</MTabItem>
```

- [ ] Step 3: 在 `@code` 区域添加处理方法

在 `MainView.razor` 的 `@code` 块中添加：

```csharp
private async Task ApplyColorPreset(WMColorPreset preset)
{
    // 获取当前选中的图片
    if (CurrentImage == null || string.IsNullOrEmpty(CurrentImage.Path))
    {
        return;
    }

    try
    {
        // 加载原图
        using var originalBitmap = SKBitmap.Decode(CurrentImage.Path);
        if (originalBitmap == null) return;

        // 应用预设
        var resultBitmap = ColorApplier.Apply(originalBitmap, preset);

        // 转换为 Base64 显示
        using var image = SKImage.FromBitmap(resultBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        var src = await Global.Byte2Url(JSRuntime, bytes);

        CurrentImage.Src = src;

        resultBitmap.Dispose();
        originalBitmap.Dispose();

        StateHasChanged();
    }
    catch (Exception ex)
    {
        // 错误处理
    }
}
```

- [ ] Step 4: 在 `@using` 区域确保已引入需要的命名空间

确认文件头部有以下 using（如果没有则添加）：

```razor
@using SkiaSharp
```

---

## 验证清单

完成所有 Task 后，确认以下内容：

- [ ] `WMAppPath.cs` 中新增了 `PresetsFolder` 属性
- [ ] `Global.cs` 中 `CP` 和 `AP` 都初始化了 `PresetsFolder`
- [ ] `WMColorPreset.cs` 包含所有调色参数和曲线参数
- [ ] `ColorApplier.cs` 的 `Apply` 方法可以正确处理图片
- [ ] `ColorAnalyzer.cs` 的 `Analyze` 方法可以从参考图生成预设
- [ ] `PresetManager.cs` 可以保存/加载/删除预设
- [ ] `ColorPresetView.razor` 界面包含所有滑块和预设列表
- [ ] `MainView.razor` 中左侧面板有「仿色」Tab
- [ ] 应用预设后图片可以正确预览

---

## 依赖

- SkiaSharp（已引入）
- System.Text.Json（内置）
- System.IO（内置）
- System.Threading（内置）
