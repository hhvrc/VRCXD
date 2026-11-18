using System.Text.RegularExpressions;

namespace VRCX.Vrchat;

/// <summary>
/// A sliding window over real timestamps, as the original's <c>createRateLimiter</c> in
/// <c>src/shared/utils/throttle.js</c>: at most so many requests in any span of the interval.
/// Which requests wait on one is <see cref="VrchatRatePolicy"/>'s to say.
/// </summary>
public sealed class VrchatRateLimiter
{
    /// <summary>At most <paramref name="Limit"/> requests in any <paramref name="Interval"/>-long span.</summary>
    public sealed record Window(int Limit, TimeSpan Interval)
    {
        public int Limit { get; } = Limit > 0 ? Limit : throw new ArgumentOutOfRangeException(nameof(Limit));
        public TimeSpan Interval { get; } = Interval > TimeSpan.Zero ? Interval : throw new ArgumentOutOfRangeException(nameof(Interval));
    }

    private readonly Window[] _windows;
    private readonly TimeSpan _longest;
    private readonly TimeProvider _time;
    // Waiters queue up here in arrival order; whoever holds it is the only one consulting the stamps.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _stamps = new();

    public VrchatRateLimiter(IReadOnlyList<Window> windows, TimeProvider? time = null)
    {
        _windows = windows.ToArray();
        _longest = _windows.Length == 0 ? TimeSpan.Zero : _windows.Max(w => w.Interval);
        _time = time ?? TimeProvider.System;
    }

    public IReadOnlyList<Window> Windows => _windows;

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

    // Caller holds _gate.
    private TimeSpan NextSlotDelay(DateTimeOffset now)
    {
        while (_stamps.Count > 0 && now - _stamps.Peek() >= _longest) _stamps.Dequeue();

        var wait = TimeSpan.Zero;
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

/// <summary>
/// One of the original's paced requests: which paths it covers, the pace, and how a 429 on one is
/// retried (the original's <c>executeWithBackoff</c>: the base delay, doubled on every retry).
/// </summary>
public sealed record VrchatRateRule(string Name, Regex Path, VrchatRateLimiter Limiter, int MaxRetries, TimeSpan RetryBaseDelay)
{
    public bool Matches(Uri? uri) => uri is not null && Path.IsMatch(uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?')[0]);
}

/// <summary>
/// Which requests to VRChat are paced. The server holds itself to exactly what the original VRCX
/// does, no more and no less: the original paces two bulk reads and sends everything else as it
/// comes, answering a 429 as the error it is.
///
/// - The friend list's pages: 60 a minute, a 429 retried five times from one second
///   (<c>bulkRefreshFriends</c> in <c>src/stores/friend.js</c>).
/// - A user's mutual friends' pages: 5 a second, a 429 retried four times from half a second
///   (<c>fetchMutualGraph</c> and <c>fetchMutualFriendsForUser</c> in <c>src/stores/charts.js</c>).
///
/// The original builds a limiter per run on one PC with one account. Here each pace is one for the
/// whole process, since VRChat counts per source IP and every account on this server shares one.
///
/// One thing goes beyond the original: when VRChat answers 429 with a Retry-After, nothing is sent
/// from this server until that time has passed, and the refused request is then sent again. VRChat
/// refuses by IP, so every account's requests would be refused until then anyway.
/// </summary>
public sealed class VrchatRatePolicy(IReadOnlyList<VrchatRateRule> rules, TimeProvider? time = null)
{
    /// <summary>How often a request VRChat refused with a Retry-After is sent again once it has passed.</summary>
    public const int RetryAfterRetries = 3;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _blockedLock = new();
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;

    /// <summary>VRChat asked for a pause: nothing goes out for <paramref name="retryAfter"/> (longer if an earlier pause outlasts it).</summary>
    public void Pause(TimeSpan retryAfter)
    {
        if (retryAfter <= TimeSpan.Zero) return;
        var until = _time.GetUtcNow() + retryAfter;
        lock (_blockedLock)
        {
            if (until > _blockedUntil) _blockedUntil = until;
        }
    }

    /// <summary>Returns once no pause VRChat asked for is running.</summary>
    public async Task WaitForPauseAsync(CancellationToken ct)
    {
        while (true)
        {
            TimeSpan wait;
            lock (_blockedLock) wait = _blockedUntil - _time.GetUtcNow();
            if (wait <= TimeSpan.Zero) return;
            await Task.Delay(wait, _time, ct);
        }
    }

    /// <summary>The original's pacing; what every production client uses.</summary>
    public static VrchatRatePolicy Upstream { get; } = new(
    [
        new VrchatRateRule("friends", new Regex("/auth/user/friends$", RegexOptions.CultureInvariant),
            new VrchatRateLimiter([new(60, TimeSpan.FromMinutes(1))]), MaxRetries: 5, RetryBaseDelay: TimeSpan.FromSeconds(1)),
        new VrchatRateRule("mutual friends", new Regex("/users/[^/]+/mutuals/friends$", RegexOptions.CultureInvariant),
            new VrchatRateLimiter([new(5, TimeSpan.FromSeconds(1))]), MaxRetries: 4, RetryBaseDelay: TimeSpan.FromMilliseconds(500)),
    ]);

    /// <summary>No pacing and no retries. For tests against a fake VRChat.</summary>
    public static VrchatRatePolicy None { get; } = new([]);

    public IReadOnlyList<VrchatRateRule> Rules => rules;

    /// <summary>The rule a request falls under, or null for one that goes out as it comes.</summary>
    public VrchatRateRule? For(Uri? uri)
    {
        foreach (var rule in rules)
            if (rule.Matches(uri)) return rule;
        return null;
    }
}
