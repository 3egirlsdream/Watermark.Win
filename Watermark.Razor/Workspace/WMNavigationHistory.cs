#nullable enable

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Watermark.Razor.Workspace;

public interface IWMNavigationHistory
{
    string CurrentPathAndQuery { get; }
    int BackDepth { get; }
    void NavigateTo(string path, bool replace = false);
    void NavigateRoot(string path);
    void GoBack(string fallbackPath);
    void GoBackTo(string path);
}

/// <summary>
/// Restores the in-app push/pop behavior formerly supplied by PPageStack while
/// keeping each page addressable through the shared router.
/// </summary>
public sealed class WMNavigationHistory : IWMNavigationHistory, IDisposable
{
    private readonly NavigationManager navigation;
    private readonly List<string> backStack = [];
    private string? pendingTarget;
    private bool disposed;

    public WMNavigationHistory(NavigationManager navigation)
    {
        this.navigation = navigation;
        CurrentPathAndQuery = GetLocalPath(navigation.Uri);
        navigation.LocationChanged += OnLocationChanged;
    }

    public string CurrentPathAndQuery { get; private set; }
    public int BackDepth => backStack.Count;

    public void NavigateTo(string path, bool replace = false)
    {
        var target = WMReturnUrl.Normalize(path, "/create");
        if (SameLocation(CurrentPathAndQuery, target)) return;

        if (!replace)
            Push(CurrentPathAndQuery);
        else if (backStack.Count > 0 && SameLocation(backStack[^1], target))
            backStack.RemoveAt(backStack.Count - 1);
        NavigateCore(target, replace);
    }

    public void NavigateRoot(string path)
    {
        backStack.Clear();
        var target = WMReturnUrl.Normalize(path, "/create");
        if (SameLocation(CurrentPathAndQuery, target)) return;
        NavigateCore(target, replace: true);
    }

    public void GoBack(string fallbackPath)
    {
        string? target = null;
        while (backStack.Count > 0 && target is null)
        {
            var candidate = Pop();
            if (!SameLocation(CurrentPathAndQuery, candidate)) target = candidate;
        }
        target ??= WMReturnUrl.Normalize(fallbackPath, "/profile");

        if (SameLocation(CurrentPathAndQuery, target)) return;
        NavigateCore(target, replace: true);
    }

    public void GoBackTo(string path)
    {
        var target = WMReturnUrl.Normalize(path, "/profile");
        var index = backStack.FindLastIndex(item => SameLocation(item, target));
        if (index >= 0)
            backStack.RemoveRange(index, backStack.Count - index);
        else
            backStack.Clear();

        if (SameLocation(CurrentPathAndQuery, target)) return;
        NavigateCore(target, replace: true);
    }

    private void NavigateCore(string target, bool replace)
    {
        pendingTarget = target;
        CurrentPathAndQuery = target;
        navigation.NavigateTo(target, replace: replace);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        var target = GetLocalPath(args.Location);
        if (pendingTarget is not null && SameLocation(pendingTarget, target))
        {
            pendingTarget = null;
            CurrentPathAndQuery = target;
            return;
        }

        pendingTarget = null;
        if (backStack.Count > 0 && SameLocation(backStack[^1], target))
            backStack.RemoveAt(backStack.Count - 1);
        else if (!SameLocation(CurrentPathAndQuery, target))
            Push(CurrentPathAndQuery);

        CurrentPathAndQuery = target;
    }

    private void Push(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (backStack.Count == 0 || !SameLocation(backStack[^1], path)) backStack.Add(path);
    }

    private string Pop()
    {
        var index = backStack.Count - 1;
        var target = backStack[index];
        backStack.RemoveAt(index);
        return target;
    }

    private string GetLocalPath(string absoluteOrRelative)
    {
        if (Uri.TryCreate(absoluteOrRelative, UriKind.Absolute, out var absolute))
            return string.IsNullOrEmpty(absolute.PathAndQuery) ? "/" : absolute.PathAndQuery;
        return WMReturnUrl.Normalize(absoluteOrRelative, "/create");
    }

    private static bool SameLocation(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        navigation.LocationChanged -= OnLocationChanged;
    }
}
