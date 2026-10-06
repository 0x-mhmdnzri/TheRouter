using System.Collections.Concurrent;

namespace TheRouter.Core.Graph;

/// <summary>
/// Keeps a versioned history of immutable RouterGraph snapshots.
/// Current is always lock-free; history is bounded.
/// </summary>
public sealed class GraphVersionStore
{
    private readonly ConcurrentDictionary<long, RouterGraph> _versions = new();
    private long _currentVersion;
    private RouterGraph _current;
    private readonly int _maxHistory;

    public GraphVersionStore(RouterGraph initial, int maxHistory = 8)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
        _currentVersion = 0;
        _versions[0] = _current;
        _maxHistory = Math.Max(1, maxHistory);
    }

    public long CurrentVersion => Volatile.Read(ref _currentVersion);
    public RouterGraph Current => Volatile.Read(ref _current!);

    public RouterGraph? GetVersion(long version)
        => _versions.TryGetValue(version, out var g) ? g : null;

    /// <summary>
    /// Publishes a new immutable graph and returns the new version number.
    /// Old versions beyond <see cref="_maxHistory"/> are dropped.
    /// </summary>
    public long Publish(RouterGraph next)
    {
        ArgumentNullException.ThrowIfNull(next);
        long newVer = Interlocked.Increment(ref _currentVersion);
        _versions[newVer] = next;
        Volatile.Write(ref _current, next);

        // prune
        long minKeep = newVer - _maxHistory;
        foreach (var key in _versions.Keys)
        {
            if (key < minKeep)
                _versions.TryRemove(key, out _);
        }

        return newVer;
    }

    public long Update(Action<RouterGraphBuilder> build)
    {
        var builder = new RouterGraphBuilder();
        build(builder);
        return Publish(builder.Build());
    }
}
