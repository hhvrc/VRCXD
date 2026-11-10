namespace VRCX.Vrchat;

/// <summary>
/// One process-wide budget for everything this server sends to VRChat, shared by every
/// account's <see cref="VrchatApiClient"/>. VRChat rate-limits by source IP, not by session,
/// so five logged-in accounts must not each get their own allowance.
///
/// The limits are the ones upstream VRCX pins itself to through <c>createRateLimiter</c> in
/// <c>src/shared/utils/throttle.js</c>: 60 requests per minute (friend list refresh) and a
/// 5 per second burst cap (mutual graph fetch), both as sliding windows over real timestamps.
/// A 429 from VRChat parks the whole budget for the Retry-After it asked for, so a flood on
/// one account does not get the IP's other sessions rejected too.
/// </summary>
public sealed class VrchatRateLimiter
{
    /// <summary>At most <paramref name="Limit"/> requests in any <paramref name="Interval"/>-long span.</summary>
    public sealed record Window(int Limit, TimeSpan Interval)
    {
        public int Limit { get; } = Limit > 0 ? Limit : throw new ArgumentOutOfRangeException(nameof(Limit));
        public TimeSpan Interval { get; } = Interval > TimeSpan.Zero ? Interval : throw new ArgumentOutOfRangeException(nameof(Interval));
    }

    /// <summary>What the upstream VRCX client holds itself to: 5/s burst, 60/min sustained.</summary>
    public static readonly IReadOnlyList<Window> UpstreamPolicy =
    [
        new(5, TimeSpan.FromSeconds(1)),
        new(60, TimeSpan.FromMinutes(1)),
    ];

    /// <summary>The one limiter production clients share. Static so nothing can accidentally build a second budget.</summary>
    public static VrchatRateLimiter Shared { get; } = new(UpstreamPolicy);

    /// <summary>No limiting at all. For tests against a fake VRChat.</summary>
    public static VrchatRateLimiter Unlimited { get; } = new([]);

    private static readonly string[] LimitedHosts = ["vrchat.cloud", "vrchat.com"];

    private readonly Window[] _windows;
    private readonly TimeSpan _longest;
    private readonly TimeProvider _time;
    // Waiters queue up here in arrival order; whoever holds it is the only one consulting the stamps.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _stamps = new();
    private readonly object _penaltyLock = new();
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;

    public VrchatRateLimiter(IReadOnlyList<Window> windows, TimeProvider? time = null)
    {
        _windows = windows.ToArray();
        _longest = _windows.Length == 0 ? TimeSpan.Zero : _windows.Max(w => w.Interval);
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<Window> Windows => _windows;

    /// <summary>True for api.vrchat.cloud, pipeline.vrchat.cloud, *.vrchat.com - anything VRChat's edge counts.</summary>
    public static bool AppliesTo(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri) return false;
        var host = uri.Host;
        foreach (var limited in LimitedHosts)
        {
            if (host.Equals(limited, StringComparison.OrdinalIgnoreCase)) return true;
            if (host.Length > limited.Length
                && host.EndsWith(limited, StringComparison.OrdinalIgnoreCase)
                && host[^(limited.Length + 1)] == '.')
                return true;
        }
        return false;
    }

    /// <summary>Returns once a request may go out, having taken one slot from every window.</summary>
    public async Task AcquireAsync(CancellationToken ct = default)
    {
        if (_windows.Length == 0) return;

        await _gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = _time.GetUtcNow();
                var wait = NextSlotDelay(now);
                if (wait <= TimeSpan.Zero)
                {
                    _stamps.Enqueue(now);
                    return;
                }
                await Task.Delay(wait, _time, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>VRChat said 429: nobody sends anything for <paramref name="delay"/> (longer if an earlier penalty outlasts it).</summary>
    public void Penalize(TimeSpan delay)
    {
        if (_windows.Length == 0 || delay <= TimeSpan.Zero) return;
        var until = _time.GetUtcNow() + delay;
        lock (_penaltyLock)
        {
            if (until > _blockedUntil) _blockedUntil = until;
        }
    }

    // Caller holds _gate.
    private TimeSpan NextSlotDelay(DateTimeOffset now)
    {
        while (_stamps.Count > 0 && now - _stamps.Peek() >= _longest) _stamps.Dequeue();

        TimeSpan wait;
        lock (_penaltyLock) wait = _blockedUntil - now;

        foreach (var window in _windows)
        {
            // Stamps are in arrival order, so the ones inside this window are the tail of the queue.
            var inWindow = 0;
            DateTimeOffset oldestInWindow = default;
            foreach (var stamp in _stamps)
            {
                if (now - stamp >= window.Interval) continue;
                if (inWindow == 0) oldestInWindow = stamp;
                inWindow++;
            }
            if (inWindow < window.Limit) continue;
            var untilFree = oldestInWindow + window.Interval - now;
            if (untilFree > wait) wait = untilFree;
        }
        return wait;
    }
}
