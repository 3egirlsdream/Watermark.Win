#nullable enable

using Microsoft.JSInterop;
using Watermark.Shared.Enums;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed record WMEditorInteractionProfile(
    string PointerType,
    bool HasHover,
    bool FinePointer,
    bool HasHardwareKeyboard,
    bool SupportsImageBitmap,
    bool SupportsBlob,
    bool SupportsPointerCapture,
    double SafeAreaTop,
    double SafeAreaRight,
    double SafeAreaBottom,
    double SafeAreaLeft,
    double DisplayDensity,
    bool MobileMemoryPolicy,
    long SceneCacheBudgetBytes);

public interface IWMEditorInteractionProfileProvider
{
    WMEditorInteractionProfile Current { get; }
    ValueTask<WMEditorInteractionProfile> GetAsync(
        CancellationToken cancellationToken = default);
    void ObservePointer(string pointerType, bool hasHover, bool finePointer);
}

public sealed class WMEditorInteractionProfileProvider(
    IJSRuntime jsRuntime,
    IWMExecutionProfileProvider executionProfiles) : IWMEditorInteractionProfileProvider, IAsyncDisposable
{
    private WMEditorInteractionProfile? current;
    private IJSObjectReference? module;

    public WMEditorInteractionProfile Current =>
        current ?? CreateFallback();

    public async ValueTask<WMEditorInteractionProfile> GetAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            module ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                cancellationToken,
                "./_content/Watermark.Razor/js/mac-template-canvas.js");
            var detected = await module.InvokeAsync<DetectedProfile>(
                "detectInteractionProfile",
                cancellationToken);
            current = Create(
                detected.PointerType,
                detected.HasHover,
                detected.FinePointer,
                detected.HasHardwareKeyboard,
                detected.SupportsImageBitmap,
                detected.SupportsBlob,
                detected.SupportsPointerCapture,
                detected.SafeAreaTop,
                detected.SafeAreaRight,
                detected.SafeAreaBottom,
                detected.SafeAreaLeft,
                detected.DisplayDensity);
        }
        catch (JSException)
        {
            current = CreateFallback();
        }
        catch (JSDisconnectedException)
        {
            current = CreateFallback();
        }
        return current;
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try { await module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        module = null;
    }

    public void ObservePointer(string pointerType, bool hasHover, bool finePointer)
    {
        var basis = Current;
        current = basis with
        {
            PointerType = NormalizePointer(pointerType),
            HasHover = hasHover,
            FinePointer = finePointer
        };
    }

    private WMEditorInteractionProfile Create(
        string? pointerType,
        bool hasHover,
        bool finePointer,
        bool hasHardwareKeyboard,
        bool supportsImageBitmap,
        bool supportsBlob,
        bool supportsPointerCapture,
        double safeAreaTop,
        double safeAreaRight,
        double safeAreaBottom,
        double safeAreaLeft,
        double displayDensity)
    {
        var mobile = Global.DeviceType is DeviceType.Andorid or DeviceType.IOS;
        var execution = executionProfiles.GetInteractiveProfile();
        var cacheBudget = Math.Max(
            16L * 1024 * 1024,
            Math.Min(
                execution.PreviewCacheBudgetBytes,
                execution.MemoryBudgetBytes / 4));
        return new WMEditorInteractionProfile(
            NormalizePointer(pointerType),
            hasHover,
            finePointer,
            hasHardwareKeyboard,
            supportsImageBitmap,
            supportsBlob,
            supportsPointerCapture,
            Math.Max(0, safeAreaTop),
            Math.Max(0, safeAreaRight),
            Math.Max(0, safeAreaBottom),
            Math.Max(0, safeAreaLeft),
            Math.Max(1, displayDensity),
            mobile,
            cacheBudget);
    }

    private WMEditorInteractionProfile CreateFallback()
    {
        var mobile = Global.DeviceType is DeviceType.Andorid or DeviceType.IOS;
        return Create(
            mobile ? "touch" : "mouse",
            !mobile,
            !mobile,
            !mobile,
            false,
            true,
            true,
            0,
            0,
            0,
            0,
            1);
    }

    private static string NormalizePointer(string? pointerType) =>
        pointerType is "touch" or "pen" ? pointerType : "mouse";

    private sealed record DetectedProfile(
        string PointerType,
        bool HasHover,
        bool FinePointer,
        bool HasHardwareKeyboard,
        bool SupportsImageBitmap,
        bool SupportsBlob,
        bool SupportsPointerCapture,
        double SafeAreaTop,
        double SafeAreaRight,
        double SafeAreaBottom,
        double SafeAreaLeft,
        double DisplayDensity);
}
