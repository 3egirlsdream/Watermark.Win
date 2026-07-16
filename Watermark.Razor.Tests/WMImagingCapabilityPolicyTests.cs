using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMImagingCapabilityPolicyTests
{
    [Fact]
    public void UnknownResources_FailClosed()
    {
        var status = WMImagingCapabilityPolicy.Evaluate(
            WMImagingFeature.StarTrail,
            new WMImagingCapabilities(
                false, true, false, 100, 45_000_000, null),
            featureEnabled: true,
            availableMemoryBytes: 0,
            availableDiskBytes: 0);

        Assert.False(status.IsAvailable);
        Assert.Contains("内存不足", status.UnavailableReason);
    }

    private const long MiB = 1024L * 1024;

    [Fact]
    public void MissingNativeBackend_ReturnsExplicitUnavailableResult()
    {
        var result = WMImagingCapabilityPolicy.Evaluate(
            WMImagingFeature.Raw,
            WMImagingCapabilities.MobileDisabled,
            featureEnabled: true,
            availableMemoryBytes: 2 * 1024 * MiB,
            availableDiskBytes: 4 * 1024 * MiB);

        Assert.False(result.BackendAvailable);
        Assert.False(result.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
    }

    [Fact]
    public void MultiFrame_WithLowMemory_IsRejectedBeforeProcessing()
    {
        var result = WMImagingCapabilityPolicy.Evaluate(
            WMImagingFeature.MultiFrame,
            NativeCapabilities(),
            featureEnabled: true,
            availableMemoryBytes: 512 * MiB,
            availableDiskBytes: 4 * 1024 * MiB);

        Assert.True(result.BackendAvailable);
        Assert.False(result.IsAvailable);
        Assert.Contains("内存不足", result.UnavailableReason);
    }

    [Fact]
    public void Tiff16_WithLowDiskOrDisabledFlag_ReturnsSpecificReason()
    {
        var lowDisk = WMImagingCapabilityPolicy.Evaluate(
            WMImagingFeature.Tiff16,
            NativeCapabilities(),
            featureEnabled: true,
            availableMemoryBytes: 2 * 1024 * MiB,
            availableDiskBytes: 512 * MiB);
        var disabled = WMImagingCapabilityPolicy.Evaluate(
            WMImagingFeature.Tiff16,
            NativeCapabilities(),
            featureEnabled: false,
            availableMemoryBytes: 2 * 1024 * MiB,
            availableDiskBytes: 4 * 1024 * MiB);

        Assert.False(lowDisk.IsAvailable);
        Assert.Contains("磁盘空间不足", lowDisk.UnavailableReason);
        Assert.False(disabled.IsAvailable);
        Assert.Contains("QA", disabled.UnavailableReason);
    }

    [Fact]
    public void Png16_UsesManagedBackendButStillHonorsResourcesAndFlag()
    {
        var result = WMImagingCapabilityPolicy.Evaluate(
            WMImagingFeature.Png16,
            WMImagingCapabilities.Unsupported,
            featureEnabled: true,
            availableMemoryBytes: 1024 * MiB,
            availableDiskBytes: 1024 * MiB);

        Assert.True(result.BackendAvailable);
        Assert.True(result.IsAvailable);
        Assert.Null(result.UnavailableReason);
    }

    private static WMImagingCapabilities NativeCapabilities() => new(
        CanDecodeRaw: true,
        CanMultiFrame: true,
        CanEncodeTiff16: true,
        MaxFrames: 100,
        MaxPixelsPerFrame: 45_000_000,
        UnavailableReason: null,
        CanAlignScene: true,
        CanAlignStars: true);
}
