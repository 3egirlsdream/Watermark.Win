#nullable enable

using Microsoft.JSInterop;
using Watermark.Razor.Workspace;

namespace Watermark.Web.Services;

public sealed class WMWebExternalActionService(IJSRuntime js) : IWMExternalActionService
{
    public Task OpenUrlAsync(string url) =>
        string.IsNullOrWhiteSpace(url)
            ? Task.CompletedTask
            : js.InvokeVoidAsync("open", url, "_blank", "noopener").AsTask();

    public Task CopyTextAsync(string text) =>
        js.InvokeVoidAsync("navigator.clipboard.writeText", text).AsTask();
}
