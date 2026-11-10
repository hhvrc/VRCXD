namespace VRCX.Testing.Infrastructure;

/// <summary>
/// The scenario's clock: real time plus an offset the test controls. Much of the server still
/// stamps rows with DateTimeOffset.UtcNow, so a frozen fake clock would put the two sources out of
/// order (a name change "before" the friendship it belongs to). This one runs with the wall clock
/// and only jumps when the test says so. Readings are strictly increasing, at least 1 ms apart, so
/// rows written in one step still order. Timers are the system's; the harness runs no hosted
/// services that would use them.
/// </summary>
public sealed class ScenarioClock : TimeProvider
{
    private readonly Lock _gate = new();
    private TimeSpan _offset;
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public ScenarioClock(DateTimeOffset? start = null)
    {
        if (start is { } at) _offset = at - System.GetUtcNow();
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            var now = Truncate(System.GetUtcNow() + _offset);
            if (now <= _last) now = _last + TimeSpan.FromMilliseconds(1);
            _last = now;
            return now;
        }
    }

    /// <summary>Moves the clock forward, for example past the pending-offline delay.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        lock (_gate) _offset += by;
    }

    /// <summary>The offset from the wall clock so far.</summary>
    public TimeSpan Offset { get { lock (_gate) return _offset; } }

    // Postgres keeps microseconds; whole milliseconds keep round-tripped values equal.
    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Offset);
}
