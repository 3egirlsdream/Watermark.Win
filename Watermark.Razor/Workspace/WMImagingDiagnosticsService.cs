#nullable enable

using System.Runtime.InteropServices;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed class WMImagingDiagnosticsService(
    IWMImagingCapabilities capabilities,
    IWMColorEngine? colorEngine = null)
    : IWMImagingDiagnosticsService
{
    public Task<WMImagingDiagnosticSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = capabilities.Current;
        var native = (capabilities as WMNativeImagingCapabilities)?.Diagnostics;
        var colorCapability = colorEngine?.Capability;
        var features = Enum.GetValues<WMImagingFeature>()
            .Select(feature => CreateStatus(feature, current))
            .ToArray();
        return Task.FromResult(new WMImagingDiagnosticSnapshot(
            Environment.OSVersion.Platform.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            native?.AbiVersion ?? 0,
            native?.IsLoaded ?? current.UnavailableReason is null,
            native?.BackendVersion ?? "Managed/host capability provider",
            native?.CapabilityBits ?? 0,
            0,
            0,
            features,
            DateTime.UtcNow,
            colorCapability?.Error ?? current.UnavailableReason));
    }

    private static WMImagingCapabilityStatus CreateStatus(
        WMImagingFeature feature,
        WMImagingCapabilities current)
    {
        var available = feature switch
        {
            WMImagingFeature.Raw => current.CanDecodeRaw,
            WMImagingFeature.StarTrail or WMImagingFeature.MultiFrame => current.CanMultiFrame,
            WMImagingFeature.Tiff16 => current.CanEncodeTiff16,
            WMImagingFeature.Png16 => current.CanDecodeRaw || current.CanMultiFrame,
            _ => false
        };
        return new WMImagingCapabilityStatus(
            feature,
            available,
            true,
            available,
            0,
            0,
            available ? null : current.UnavailableReason ?? "当前宿主未开放此能力。");
    }
}
