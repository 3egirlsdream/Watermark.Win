#nullable enable

namespace Watermark.Razor.Workspace;

/// <summary>
/// Host-to-Razor back dispatcher. Handlers form a stack so the most recently
/// displayed overlay or page receives Android system back first.
/// </summary>
public sealed class WMSystemBackDispatcher : IWMSystemBackDispatcher
{
    private readonly object gate = new();
    private readonly List<Registration> registrations = [];

    public IDisposable Register(Func<bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var registration = new Registration(this, handler);
        lock (gate) registrations.Add(registration);
        return registration;
    }

    public bool TryDispatch()
    {
        Registration[] snapshot;
        lock (gate) snapshot = registrations.ToArray();
        for (var index = snapshot.Length - 1; index >= 0; index--)
        {
            try
            {
                if (snapshot[index].Handler()) return true;
            }
            catch
            {
                // A stale Razor handler must not prevent the host from falling
                // back to its normal navigation behavior.
            }
        }
        return false;
    }

    private void Remove(Registration registration)
    {
        lock (gate) registrations.Remove(registration);
    }

    private sealed class Registration(WMSystemBackDispatcher owner, Func<bool> handler) : IDisposable
    {
        private int disposed;
        public Func<bool> Handler { get; } = handler;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Remove(this);
        }
    }
}
