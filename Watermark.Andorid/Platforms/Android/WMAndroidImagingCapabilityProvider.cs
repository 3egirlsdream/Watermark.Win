#if ANDROID
#nullable enable

using Android.App;
using Android.Content;
using Android.OS;
using Watermark.Razor.Workspace;
using Watermark.Shared.Models;

namespace Watermark.Andorid;

/// <summary>
/// Android host capability gate. Native services may be registered for hidden
/// diagnostics, but product features become available only when the backend,
/// per-feature QA flag, memory and disk checks all pass.
/// </summary>
public sealed class WMAndroidImagingCapabilityProvider(
    WMNativeImagingCapabilities nativeCapabilities,
    IWMWorkspaceFeatureFlags featureFlags) : IWMImagingCapabilityProvider
{
    private const long MiB = 1024L * 1024;

    public WMImagingCapabilities Current
    {
        get
        {
            var raw = Probe(WMImagingFeature.Raw);
            var starTrail = Probe(WMImagingFeature.StarTrail);
            var multiFrame = Probe(WMImagingFeature.MultiFrame);
            var tiff = Probe(WMImagingFeature.Tiff16);
            var native = nativeCapabilities.Current;
            var availableMemory = Math.Max(0, multiFrame.AvailableMemoryBytes);
            var maxFrames = availableMemory == 0
                ? 20
                : (int)Math.Clamp(availableMemory / (128 * MiB), 2, 100);
            var maxPixels = availableMemory == 0
                ? 12_000_000
                : Math.Clamp(availableMemory / 32, 4_000_000, 45_000_000);
            var canMultiFrame = starTrail.IsAvailable || multiFrame.IsAvailable;
            return new WMImagingCapabilities(
                raw.IsAvailable,
                canMultiFrame,
                tiff.IsAvailable,
                maxFrames,
                maxPixels,
                canMultiFrame ? null : multiFrame.UnavailableReason ?? starTrail.UnavailableReason,
                native.CanAlignScene && canMultiFrame,
                native.CanAlignStars && multiFrame.IsAvailable);
        }
    }

    public WMImagingCapabilityStatus Probe(
        WMImagingFeature feature,
        long requiredDiskBytes = 0)
    {
        return WMImagingCapabilityPolicy.Evaluate(
            feature,
            nativeCapabilities.Current,
            featureFlags.IsImagingFeatureEnabled(feature),
            GetAvailableMemoryBytes(),
            GetAvailableDiskBytes(),
            requiredDiskBytes);
    }

    internal static long GetAvailableMemoryBytes()
    {
        try
        {
            var context = Android.App.Application.Context;
            using var manager = context.GetSystemService(Context.ActivityService) as ActivityManager;
            if (manager is null) return 0;
            var info = new ActivityManager.MemoryInfo();
            manager.GetMemoryInfo(info);
            return Math.Max(0, info.AvailMem);
        }
        catch
        {
            return 0;
        }
    }

    internal static long GetAvailableDiskBytes()
    {
        try
        {
            using var statistics = new StatFs(FileSystem.CacheDirectory);
            return Math.Max(0, statistics.AvailableBytes);
        }
        catch
        {
            return 0;
        }
    }

}

public sealed class WMAndroidImagingDiagnosticsService(
    WMNativeImagingCapabilities nativeCapabilities,
    IWMImagingCapabilityProvider capabilities) : IWMImagingDiagnosticsService
{
    public Task<WMImagingDiagnosticSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var native = nativeCapabilities.Diagnostics;
        var features = Enum.GetValues<WMImagingFeature>()
            .Select(feature => capabilities.Probe(feature))
            .ToArray();
        return Task.FromResult(new WMImagingDiagnosticSnapshot(
            Android.OS.Build.Model ?? "Android",
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            native.AbiVersion,
            native.IsLoaded,
            native.BackendVersion,
            native.CapabilityBits,
            WMAndroidImagingCapabilityProvider.GetAvailableMemoryBytes(),
            WMAndroidImagingCapabilityProvider.GetAvailableDiskBytes(),
            features,
            DateTime.UtcNow));
    }
}

/// <summary>
/// Keeps RAW out of the normal picker/import path until its independent gate
/// passes, while leaving the concrete composite decoder available to hidden
/// diagnostics.
/// </summary>
public sealed class WMAndroidCapabilityGatedPhotoDecoder(
    WMCompositePhotoDecoder inner,
    IWMImagingCapabilityProvider capabilities) : IWMPhotoDecoder
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".sr2", ".raf",
        ".orf", ".rw2", ".rwl", ".pef", ".3fr", ".iiq", ".srw"
    };

    public bool CanDecode(string path) =>
        !IsRaw(path)
            ? inner.CanDecode(path)
            : capabilities.Probe(WMImagingFeature.Raw).IsAvailable && inner.CanDecode(path);

    public Task<WMPhotoDecodeResult> DecodeAsync(
        string sourcePath,
        string outputPath,
        WMPhotoDecodeOptions decodeOptions,
        CancellationToken cancellationToken = default)
    {
        if (!IsRaw(sourcePath))
            return inner.DecodeAsync(sourcePath, outputPath, decodeOptions, cancellationToken);
        var capability = capabilities.Probe(WMImagingFeature.Raw);
        return capability.IsAvailable
            ? inner.DecodeAsync(sourcePath, outputPath, decodeOptions, cancellationToken)
            : Task.FromResult(new WMPhotoDecodeResult(
                WMImagingResultStatus.Unsupported,
                null,
                0,
                0,
                "Android capability gate",
                "1",
                capability.UnavailableReason));
    }

    private static bool IsRaw(string path) =>
        RawExtensions.Contains(Path.GetExtension(path));
}
#endif
