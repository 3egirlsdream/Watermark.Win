#nullable enable

using System.Runtime.InteropServices;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed class WMImagingDiagnosticsService(IWMImagingCapabilities capabilities)
    : IWMImagingDiagnosticsService
{
    public Task<WMImagingDiagnosticSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = capabilities.Current;
        var features = Enum.GetValues<WMImagingFeature>()
            .Select(feature => CreateStatus(feature, current))
            .ToArray();
        return Task.FromResult(new WMImagingDiagnosticSnapshot(
            Environment.OSVersion.Platform.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            0,
            current.UnavailableReason is null,
            "Managed/host capability provider",
            0,
            0,
            0,
            features,
            DateTime.UtcNow,
            current.UnavailableReason));
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
