#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Transitional host adapter. V2 pages depend only on the focused haptic
/// contract while existing platform implementations remain in IClientInstance.
/// </summary>
public sealed class WMClientHapticFeedback(IClientInstance client) : IWMHapticFeedback
{
    public void Perform()
    {
        try { client.Haptic(); }
        catch (NotImplementedException) { }
        catch (PlatformNotSupportedException) { }
    }
}
