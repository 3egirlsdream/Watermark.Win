#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Platform-neutral policy used by host capability providers. Keeping resource
/// and feature-flag decisions here makes Android probing deterministic and
/// testable without loading a native ABI in unit tests.
/// </summary>
public static class WMImagingCapabilityPolicy
{
    private const long MiB = 1024L * 1024;

    public static WMImagingCapabilityStatus Evaluate(
        WMImagingFeature feature,
        WMImagingCapabilities native,
        bool featureEnabled,
        long availableMemoryBytes,
        long availableDiskBytes,
        long requiredDiskBytes = 0)
    {
        availableMemoryBytes = Math.Max(0, availableMemoryBytes);
        availableDiskBytes = Math.Max(0, availableDiskBytes);
        var backendLoaded = string.IsNullOrWhiteSpace(native.UnavailableReason);
        var backendAvailable = feature switch
        {
            WMImagingFeature.Raw => backendLoaded && native.CanDecodeRaw,
            WMImagingFeature.StarTrail => backendLoaded && native.CanMultiFrame,
            WMImagingFeature.MultiFrame => backendLoaded && native.CanMultiFrame && native.CanAlignStars,
            WMImagingFeature.Png16 => true,
            WMImagingFeature.Tiff16 => backendLoaded && native.CanEncodeTiff16,
            _ => false
        };
        var minimumMemory = MinimumMemory(feature);
        var minimumDisk = Math.Max(requiredDiskBytes, MinimumDisk(feature));
        // Heavy imaging fails closed when the host cannot probe resources. The
        // execution engine performs a second concrete disk check immediately
        // before work starts, but it must never be the first resource gate.
        var hasMemory = availableMemoryBytes >= minimumMemory;
        var hasDisk = availableDiskBytes >= minimumDisk;
        var available = backendAvailable && featureEnabled && hasMemory && hasDisk;
        var reason = available
            ? null
            : !backendAvailable
                ? BackendReason(feature, native.UnavailableReason)
                : !featureEnabled
                    ? "该影像能力尚未通过本地 QA 功能开关开放。"
                    : !hasMemory
                        ? $"可用内存不足，需要至少 {FormatBytes(minimumMemory)}。"
                        : $"可用磁盘空间不足，需要至少 {FormatBytes(minimumDisk)}。";
        return new WMImagingCapabilityStatus(
            feature,
            backendAvailable,
            featureEnabled,
            available,
            availableMemoryBytes,
            availableDiskBytes,
            reason);
    }

    private static long MinimumMemory(WMImagingFeature feature) => feature switch
    {
        WMImagingFeature.Raw => 384 * MiB,
        WMImagingFeature.StarTrail => 512 * MiB,
        WMImagingFeature.MultiFrame => 768 * MiB,
        WMImagingFeature.Png16 => 384 * MiB,
        WMImagingFeature.Tiff16 => 768 * MiB,
        _ => 512 * MiB
    };

    private static long MinimumDisk(WMImagingFeature feature) => feature switch
    {
        WMImagingFeature.Raw => 512 * MiB,
        WMImagingFeature.StarTrail => 1024 * MiB,
        WMImagingFeature.MultiFrame => 2 * 1024 * MiB,
        WMImagingFeature.Png16 => 512 * MiB,
        WMImagingFeature.Tiff16 => 2 * 1024 * MiB,
        _ => 1024 * MiB
    };

    private static string BackendReason(WMImagingFeature feature, string? nativeReason) =>
        feature == WMImagingFeature.Png16
            ? "当前设备不支持 16 位 PNG 编码。"
            : nativeReason ?? feature switch
            {
                WMImagingFeature.Raw => "当前 ABI 缺少可用的 RAW 解码符号。",
                WMImagingFeature.StarTrail => "当前 ABI 缺少星轨合成后端。",
                WMImagingFeature.MultiFrame => "当前 ABI 缺少星点配准或多帧堆栈后端。",
                WMImagingFeature.Tiff16 => "当前 ABI 缺少 16 位 TIFF 编码符号。",
                _ => "当前发布包缺少所需影像后端。"
            };

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * MiB
            ? $"{bytes / (1024d * MiB):0.#} GB"
            : $"{bytes / (double)MiB:0} MB";
}

/// <summary>
/// Desktop host adapter. Android owns the staged rollout policy; Mac Catalyst
/// and Windows expose the capabilities already supplied by their native host,
/// while still applying the same memory and disk safety checks.
/// </summary>
public sealed class WMHostImagingCapabilityProvider(IWMImagingCapabilities native)
    : IWMImagingCapabilityProvider
{
    public WMImagingCapabilities Current => native.Current;

    public WMImagingCapabilityStatus Probe(
        WMImagingFeature feature,
        long requiredDiskBytes = 0) =>
        Evaluate(feature, requiredDiskBytes, GetAvailableMemoryBytes(), GetAvailableDiskBytes());

    private WMImagingCapabilityStatus Evaluate(
        WMImagingFeature feature,
        long requiredDiskBytes,
        long availableMemoryBytes,
        long availableDiskBytes)
    {
        var current = Current;
        if (feature == WMImagingFeature.Png16)
        {
            // PNG16 is implemented by the managed high-precision pipeline and
            // does not require a native encoder symbol on desktop.
            current = current with { CanDecodeRaw = true };
        }
        return WMImagingCapabilityPolicy.Evaluate(
            feature,
            current,
            featureEnabled: true,
            availableMemoryBytes,
            availableDiskBytes,
            requiredDiskBytes);
    }

    private static long GetAvailableMemoryBytes()
    {
        try { return Math.Max(0, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes); }
        catch { return 0; }
    }

    private static long GetAvailableDiskBytes()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(Global.AppPath.BasePath));
            return string.IsNullOrWhiteSpace(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch { return 0; }
    }
}
