using Watermark.Razor.Workspace;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMSystemBackDispatcherTests
{
    [Fact]
    public void Dispatch_UsesNewestHandlerFirst()
    {
        var dispatcher = new WMSystemBackDispatcher();
        var calls = new List<string>();
        using var first = dispatcher.Register(() =>
        {
            calls.Add("first");
            return true;
        });
        using var second = dispatcher.Register(() =>
        {
            calls.Add("second");
            return true;
        });

        Assert.True(dispatcher.TryDispatch());
        Assert.Equal(["second"], calls);
    }

    [Fact]
    public void DisposedHandler_IsNotCalled()
    {
        var dispatcher = new WMSystemBackDispatcher();
        var calls = 0;
        var registration = dispatcher.Register(() =>
        {
            calls++;
            return true;
        });
        registration.Dispose();

        Assert.False(dispatcher.TryDispatch());
        Assert.Equal(0, calls);
    }
}
