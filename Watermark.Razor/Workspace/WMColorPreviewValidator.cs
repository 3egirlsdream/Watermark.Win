#nullable enable

using SkiaSharp;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed class WMColorPreviewValidator
{
    public const double MaximumAverageDeltaE = 0.5;
    public const double MaximumPeakDeltaE = 2.0;
    public const int MaximumAcceptedChannelError = 2;
    public const double MinimumChannelPassRate = 0.999;

    public WMColorPreviewValidationRequest CreateRequest()
    {
        const int width = 32;
        const int height = 24;
        var sourceBytes = CreateChart(width, height);
        using var source = CreateBitmap(width, height, sourceBytes);
        var automatic = new WMColorGradeSettings
        {
            Exposure = 0.18f,
            Contrast = 12,
            Highlights = -8,
            Shadows = 11,
            Temperature = 9,
            Tint = -6,
            Saturation = 7,
            MasterCurve =
            [
                new WMCurvePoint { X = 0, Y = 0.01f },
                new WMCurvePoint { X = 0.5f, Y = 0.53f },
                new WMCurvePoint { X = 1, Y = 0.99f }
            ]
        };
        var manual = new WMColorGradeSettings
        {
            Exposure = -0.07f,
            Contrast = 6,
            Highlights = 5,
            Shadows = -4,
            Vibrance = 9,
            Saturation = -3,
            RedCurve =
            [
                new WMCurvePoint { X = 0, Y = 0 },
                new WMCurvePoint { X = 0.5f, Y = 0.48f },
                new WMCurvePoint { X = 1, Y = 1 }
            ],
            GreenCurve =
            [
                new WMCurvePoint { X = 0, Y = 0.005f },
                new WMCurvePoint { X = 0.45f, Y = 0.47f },
                new WMCurvePoint { X = 1, Y = 0.995f }
            ],
            BlueCurve =
            [
                new WMCurvePoint { X = 0, Y = 0.015f },
                new WMCurvePoint { X = 0.55f, Y = 0.52f },
                new WMCurvePoint { X = 1, Y = 0.985f }
            ]
        };
        var bands = Enum.GetValues<WMHslBand>();
        for (var index = 0; index < bands.Length; index++)
            manual.Hsl[bands[index]] = new WMHslAdjustment
            {
                Hue = index % 2 == 0 ? index + 1 : -(index + 1),
                Saturation = index % 3 == 0 ? 6 : -3,
                Luminance = index % 2 == 0 ? -2 : 3
            };
        automatic.Normalize();
        manual.Normalize();
        var generated = new WMGeneratedColorLook(
            automatic,
            CreateValidationLut(17),
            $"validation-v{WMColorPipelineVersion.Current}");
        using var expected = WMHighPrecisionColorPipeline.ApplyPreviewReference(source, generated, manual);
        return new WMColorPreviewValidationRequest(
            width,
            height,
            sourceBytes,
            ReadRgba(expected),
            WMColorPreviewLook.From(generated),
            WMColorPreviewParameters.From(manual));
    }

    public WMColorPreviewValidationResult Evaluate(
        WMColorPreviewValidationRequest request,
        byte[] actualRgba)
    {
        var requiredLength = request.Width * request.Height * 4;
        if (actualRgba.Length != requiredLength)
            return new WMColorPreviewValidationResult(
                false, double.MaxValue, double.MaxValue, 255, 0,
                $"GPU返回了{actualRgba.Length}字节，预期{requiredLength}字节。");

        var totalDeltaE = 0d;
        var maximumDeltaE = 0d;
        var maximumChannelError = 0;
        var acceptedChannels = 0;
        var channelCount = request.Width * request.Height * 3;
        for (var index = 0; index < requiredLength; index += 4)
        {
            var expected = new SKColor(
                request.ExpectedRgba[index], request.ExpectedRgba[index + 1], request.ExpectedRgba[index + 2]);
            var actual = new SKColor(actualRgba[index], actualRgba[index + 1], actualRgba[index + 2]);
            var deltaE = DeltaE2000(expected, actual);
            totalDeltaE += deltaE;
            maximumDeltaE = Math.Max(maximumDeltaE, deltaE);
            for (var channel = 0; channel < 3; channel++)
            {
                var error = Math.Abs(request.ExpectedRgba[index + channel] - actualRgba[index + channel]);
                maximumChannelError = Math.Max(maximumChannelError, error);
                if (error <= MaximumAcceptedChannelError) acceptedChannels++;
            }
        }
        var averageDeltaE = totalDeltaE / (request.Width * request.Height);
        var passRate = acceptedChannels / (double)channelCount;
        var passed = averageDeltaE <= MaximumAverageDeltaE
                     && maximumDeltaE <= MaximumPeakDeltaE
                     && passRate >= MinimumChannelPassRate;
        return new WMColorPreviewValidationResult(
            passed,
            averageDeltaE,
            maximumDeltaE,
            maximumChannelError,
            passRate,
            passed
                ? null
                : $"GPU色差超限：平均ΔE {averageDeltaE:0.###}，最大ΔE {maximumDeltaE:0.###}，通道通过率 {passRate:P2}。");
    }

    private static byte[] CreateChart(int width, int height)
    {
        var accents = new[]
        {
            new SKColor(0, 0, 0), new SKColor(255, 255, 255), new SKColor(190, 120, 90),
            new SKColor(230, 35, 45), new SKColor(35, 210, 80), new SKColor(30, 85, 230),
            new SKColor(245, 210, 35), new SKColor(190, 45, 210)
        };
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            SKColor color;
            if (y < 8)
            {
                var value = (byte)Math.Round(x / (double)(width - 1) * 255);
                color = new SKColor(value, value, value);
            }
            else if (y < 16)
            {
                color = accents[Math.Min(accents.Length - 1, x * accents.Length / width)];
            }
            else
            {
                color = new SKColor(
                    (byte)Math.Round(x / (double)(width - 1) * 255),
                    (byte)Math.Round((height - 1 - y) / 7d * 255),
                    (byte)Math.Round((x + y) / (double)(width + height - 2) * 255));
            }
            bytes[offset] = color.Red;
            bytes[offset + 1] = color.Green;
            bytes[offset + 2] = color.Blue;
            bytes[offset + 3] = 255;
        }
        return bytes;
    }

    private static WMColorLut3D CreateValidationLut(int size)
    {
        var values = new float[size * size * size * 3];
        var offset = 0;
        for (var b = 0; b < size; b++)
        for (var g = 0; g < size; g++)
        for (var r = 0; r < size; r++)
        {
            var red = r / (float)(size - 1);
            var green = g / (float)(size - 1);
            var blue = b / (float)(size - 1);
            values[offset++] = Math.Clamp(red * 0.985f + green * 0.015f, 0, 1);
            values[offset++] = Math.Clamp(green * 0.99f + blue * 0.01f, 0, 1);
            values[offset++] = Math.Clamp(blue * 0.98f + red * 0.02f, 0, 1);
        }
        return new WMColorLut3D { Size = size, Values = values };
    }

    private static SKBitmap CreateBitmap(int width, int height, byte[] rgba)
    {
        var bitmap = new SKBitmap(new SKImageInfo(
            width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb()));
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            bitmap.SetPixel(x, y, new SKColor(
                rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]));
        }
        return bitmap;
    }

    private static byte[] ReadRgba(SKBitmap bitmap)
    {
        var bytes = new byte[bitmap.Width * bitmap.Height * 4];
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            var offset = (y * bitmap.Width + x) * 4;
            bytes[offset] = color.Red;
            bytes[offset + 1] = color.Green;
            bytes[offset + 2] = color.Blue;
            bytes[offset + 3] = color.Alpha;
        }
        return bytes;
    }

    internal static double DeltaE2000(SKColor left, SKColor right)
    {
        var (l1, a1, b1) = ColorAnalyzer.ToLab(left);
        var (l2, a2, b2) = ColorAnalyzer.ToLab(right);
        var c1 = Math.Sqrt(a1 * a1 + b1 * b1);
        var c2 = Math.Sqrt(a2 * a2 + b2 * b2);
        var meanC = (c1 + c2) / 2;
        var meanC7 = Math.Pow(meanC, 7);
        var g = 0.5 * (1 - Math.Sqrt(meanC7 / (meanC7 + Math.Pow(25, 7))));
        var adjustedA1 = (1 + g) * a1;
        var adjustedA2 = (1 + g) * a2;
        var adjustedC1 = Math.Sqrt(adjustedA1 * adjustedA1 + b1 * b1);
        var adjustedC2 = Math.Sqrt(adjustedA2 * adjustedA2 + b2 * b2);
        var h1 = HueDegrees(adjustedA1, b1);
        var h2 = HueDegrees(adjustedA2, b2);
        var deltaL = l2 - l1;
        var deltaC = adjustedC2 - adjustedC1;
        var deltaHAngle = adjustedC1 * adjustedC2 == 0 ? 0
            : Math.Abs(h2 - h1) <= 180 ? h2 - h1
            : h2 <= h1 ? h2 - h1 + 360 : h2 - h1 - 360;
        var deltaH = 2 * Math.Sqrt(adjustedC1 * adjustedC2) * Math.Sin(ToRadians(deltaHAngle / 2));
        var meanL = (l1 + l2) / 2;
        var meanAdjustedC = (adjustedC1 + adjustedC2) / 2;
        var meanH = adjustedC1 * adjustedC2 == 0 ? h1 + h2
            : Math.Abs(h1 - h2) <= 180 ? (h1 + h2) / 2
            : h1 + h2 < 360 ? (h1 + h2 + 360) / 2 : (h1 + h2 - 360) / 2;
        var t = 1 - 0.17 * Math.Cos(ToRadians(meanH - 30)) + 0.24 * Math.Cos(ToRadians(2 * meanH))
            + 0.32 * Math.Cos(ToRadians(3 * meanH + 6)) - 0.20 * Math.Cos(ToRadians(4 * meanH - 63));
        var sl = 1 + 0.015 * Math.Pow(meanL - 50, 2) / Math.Sqrt(20 + Math.Pow(meanL - 50, 2));
        var sc = 1 + 0.045 * meanAdjustedC;
        var sh = 1 + 0.015 * meanAdjustedC * t;
        var deltaTheta = 30 * Math.Exp(-Math.Pow((meanH - 275) / 25, 2));
        var meanC7Adjusted = Math.Pow(meanAdjustedC, 7);
        var rc = 2 * Math.Sqrt(meanC7Adjusted / (meanC7Adjusted + Math.Pow(25, 7)));
        var rt = -rc * Math.Sin(ToRadians(2 * deltaTheta));
        var lTerm = deltaL / sl;
        var cTerm = deltaC / sc;
        var hTerm = deltaH / sh;
        return Math.Sqrt(lTerm * lTerm + cTerm * cTerm + hTerm * hTerm + rt * cTerm * hTerm);
    }

    private static double HueDegrees(double a, double b)
    {
        if (Math.Abs(a) < double.Epsilon && Math.Abs(b) < double.Epsilon) return 0;
        var degrees = Math.Atan2(b, a) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
