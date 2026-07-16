#nullable enable

using Watermark.Shared.Enums;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed class WMExecutionProfileProvider(IWMImagingCapabilities capabilities) : IWMExecutionProfileProvider
{
    public WMOperationExecutionOptions GetInteractiveProfile()
    {
        var mobile = Global.DeviceType is DeviceType.Andorid or DeviceType.IOS;
        var memoryBudget = WMProcessingScheduler.GetDefaultMemoryBudget();
        return new WMOperationExecutionOptions
        {
            MaxConcurrentImages = mobile ? 1 : 2,
            MaxPixelWorkers = mobile ? Math.Max(1, Environment.ProcessorCount / 2) : Math.Max(1, Environment.ProcessorCount - 2),
            PreviewMaxEdge = 1600,
            PreviewRenderMaxEdge = 1600,
            PreviewAnalysisMaxEdge = 1024,
            PreviewDecodeConcurrency = mobile ? 1 : 2,
            PreviewCacheBudgetBytes = mobile
                ? 512L * 1024 * 1024
                : 4L * 1024 * 1024 * 1024,
            MemoryBudgetBytes = mobile ? Math.Max(64L * 1024 * 1024, (long)(memoryBudget * .7)) : memoryBudget
        }.Normalize();
    }

    public WMImagingCapabilities GetImagingCapabilities() => capabilities.Current;
}

public static class WMImagingRolloutDefaults
{
    public static WMImagingRolloutOptions Create() => new(
#if WM_IMAGING_MASTER
        MasterEnabled: true,
#else
        MasterEnabled: false,
#endif
#if WM_IMAGING_RAW
        RawEnabled: true,
#else
        RawEnabled: false,
#endif
#if WM_IMAGING_STAR_TRAIL
        StarTrailEnabled: true,
#else
        StarTrailEnabled: false,
#endif
#if WM_IMAGING_MULTI_FRAME
        MultiFrameEnabled: true,
#else
        MultiFrameEnabled: false,
#endif
#if WM_IMAGING_PNG16
        Png16Enabled: true,
#else
        Png16Enabled: false,
#endif
#if WM_IMAGING_TIFF16
        Tiff16Enabled: true,
#else
        Tiff16Enabled: false,
#endif
#if DEBUG || WM_IMAGING_QA_OVERRIDE
        AllowLocalQaOverride: true);
#else
        AllowLocalQaOverride: false);
#endif
}

public sealed class WMWorkspaceFeatureFlags(WMImagingRolloutOptions rollout) : IWMWorkspaceFeatureFlags
{
    private readonly string flagsRoot = Path.Combine(Global.AppPath.BasePath, "Cache", "feature-flags");

    public bool IsHeavyImagingEnabled => ResolveFlag("android-heavy-imaging", rollout.MasterEnabled);

    public bool IsImagingFeatureEnabled(WMImagingFeature feature) =>
        IsHeavyImagingEnabled && ResolveFlag(feature switch
        {
            WMImagingFeature.Raw => "android-imaging-raw",
            WMImagingFeature.StarTrail => "android-imaging-star-trail",
            WMImagingFeature.MultiFrame => "android-imaging-multi-frame",
            WMImagingFeature.Png16 => "android-imaging-png16",
            WMImagingFeature.Tiff16 => "android-imaging-tiff16",
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null)
        }, FeatureDefault(feature));

    private bool FeatureDefault(WMImagingFeature feature) => feature switch
    {
        WMImagingFeature.Raw => rollout.RawEnabled,
        WMImagingFeature.StarTrail => rollout.StarTrailEnabled,
        WMImagingFeature.MultiFrame => rollout.MultiFrameEnabled,
        WMImagingFeature.Png16 => rollout.Png16Enabled,
        WMImagingFeature.Tiff16 => rollout.Tiff16Enabled,
        _ => false
    };

    private bool ResolveFlag(string name, bool defaultValue) =>
        rollout.AllowLocalQaOverride ? ReadFlag(name, defaultValue) : defaultValue;

    private bool ReadFlag(string name, bool defaultValue)
    {
        try
        {
            var path = Path.Combine(flagsRoot, name);
            if (!File.Exists(path)) return defaultValue;
            return bool.TryParse(File.ReadAllText(path).Trim(), out var enabled) ? enabled : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }
}
