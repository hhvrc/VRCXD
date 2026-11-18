using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Filters;

namespace VRCX.API.Services;

/// <summary>
/// Short-lived answers to the VRChat reads a dialog makes each time it opens (a user's mutual
/// counts, profile and groups, the account's group instances, an instance's live state), and a
/// minimum gap between ?refresh=true fetches of one user, world, group or instance. Opening the
/// same dialog twice, or two panels asking at once, then costs VRChat one request instead of one
/// each. Keys are per account and target; concurrent askers of one key share the one fetch. A
/// failed fetch is not kept. Any write through the API (<see cref="ClearOnWriteFilter"/>) empties
/// it, so what the account just changed is read again.
/// </summary>
public sealed class VrchatReadCache(TimeProvider time)
{
    /// <summary>The user dialog's tabs and the sidebar's group instances: they change rarely.</summary>
    public static readonly TimeSpan DialogTtl = TimeSpan.FromMinutes(5);

    /// <summary>An instance's player counts and queue, which the instance bar shows live.</summary>
    public static readonly TimeSpan LiveInstanceTtl = TimeSpan.FromMinutes(1);

    /// <summary>How soon after one ?refresh=true another for the same thing reaches VRChat again.</summary>
    public static readonly TimeSpan RefreshMinAge = TimeSpan.FromSeconds(30);

    private sealed record Entry(DateTimeOffset At, Task<object?> Value);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public static string Key(string accountId, string what, string target) => $"{accountId}\n{what}\n{target}";

    /// <summary>
    /// The kept answer for <paramref name="key"/> while it is younger than <paramref name="ttl"/>, else
    /// <paramref name="fetch"/>'s. With <paramref name="fresh"/> (a refresh button) it fetches anyway
    /// and keeps that answer for the next askers.
    /// </summary>
    public async Task<T> GetOrFetchAsync<T>(string key, TimeSpan ttl, Func<Task<T>> fetch, bool fresh = false)
    {
        while (true)
        {
            var now = time.GetUtcNow();
            var found = _entries.TryGetValue(key, out var entry);
            if (found && !fresh && now - entry!.At < ttl)
            {
                try
                {
                    return (T)(await entry.Value)!;
                }
                catch (Exception) when (!entry.Value.IsCompletedSuccessfully)
                {
                    // The fetch this waited for failed and is gone: fetch on our own.
                    _entries.TryRemove(KeyValuePair.Create(key, entry));
                    continue;
                }
            }

            var source = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var mine = new Entry(now, source.Task);
            if (found ? !_entries.TryUpdate(key, mine, entry!) : !_entries.TryAdd(key, mine)) continue;
            Prune(now);
            try
            {
                var value = await fetch();
                source.SetResult(value);
                return value;
            }
            catch (Exception ex)
            {
                _entries.TryRemove(KeyValuePair.Create(key, mine));
                source.SetException(ex);
                _ = source.Task.Exception;
                throw;
            }
        }
    }

    /// <summary>Runs <paramref name="refresh"/> unless it ran for <paramref name="key"/> less than <paramref name="minAge"/> ago. True when it ran.</summary>
    public async Task<bool> ThrottleAsync(string key, TimeSpan minAge, Func<Task> refresh)
    {
        var ran = false;
        await GetOrFetchAsync<bool>(key, minAge, async () =>
        {
            ran = true;
            await refresh();
            return true;
        });
        return ran;
    }

    /// <summary>Forget everything kept, so the next read asks VRChat.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>Forget these keys: a refresh of the thing they are about.</summary>
    public void Forget(params string[] keys)
    {
        foreach (var key in keys) _entries.TryRemove(key, out _);
    }

    private void Prune(DateTimeOffset now)
    {
        if (_entries.Count < 2000) return;
        foreach (var (key, entry) in _entries)
            if (now - entry.At > DialogTtl && entry.Value.IsCompleted) _entries.TryRemove(KeyValuePair.Create(key, entry));
    }

    /// <summary>
    /// A write through the API (an edit, a join, a friend request) may change what is kept, so it all
    /// goes; so does the account's stored auth/user, unless the write stored a new one itself (a status
    /// change answers with the account; selecting a fallback avatar or a home world does not).
    /// </summary>
    public sealed class ClearOnWriteFilter(VrchatReadCache cache, Common.Services.Sessions.CurrentUserCache currentUsers, TimeProvider time) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var method = context.HttpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            {
                await next();
                return;
            }
            var started = time.GetUtcNow();
            await next();
            cache.Clear();
            if (context.RouteData.Values.TryGetValue("accountId", out var account) && account is string accountId)
                currentUsers.InvalidateUnlessSetSince(accountId, started);
        }
    }
}
