using System.Collections.Concurrent;

namespace VRCX.API.Services;

/// <summary>
/// Upstream's refreshNotifications guard (8397c5f8), per account: a refresh asked for while one
/// runs waits for that one instead of fetching again, and after VRChat answers 429 refreshes do
/// nothing for <see cref="Backoff"/>.
/// </summary>
public sealed class NotificationRefreshGate(TimeProvider time)
{
    public static readonly TimeSpan Backoff = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _blockedUntil = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>Runs <paramref name="refresh"/> unless one is running or the account is backing off; then <paramref name="skipped"/>.</summary>
    public async Task<T> RunAsync<T>(string accountId, Func<Task<T>> refresh, Func<T> skipped, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            await gate.WaitAsync(cancellationToken);
            gate.Release();
            return skipped();
        }
        try
        {
            if (_blockedUntil.TryGetValue(accountId, out var until) && time.GetUtcNow() < until) return skipped();
            return await refresh();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>VRChat rate-limited this account's refresh.</summary>
    public void RateLimited(string accountId) => _blockedUntil[accountId] = time.GetUtcNow() + Backoff;
}
