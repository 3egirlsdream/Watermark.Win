#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Keeps host chrome behind a focused workspace contract while legacy account
/// and settings pages continue to use <see cref="IClientInstance"/> directly.
/// </summary>
public sealed class WMClientSystemAppearance(IClientInstance client) : IWMSystemAppearance
{
    public void SetWorkspaceActive(bool active) =>
        client.SetColor(active ? "#10151E" : "#F4F6F8");
}
