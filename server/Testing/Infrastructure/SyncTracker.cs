using System.Net;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;

namespace VRCX.Testing.Infrastructure;

/// <summary>
/// Counts the friend and full syncs the server runs. A login starts a full sync in the
/// background (Login.cs, StartInitialSync) that would otherwise race whatever the test does next:
/// it can overwrite rows the test just arranged, or collide with the next login. The scenario's
/// HTTP clients wait for it (<see cref="SettleAfterLoginHandler"/>), so a login is complete,
/// initial sync included, when the call returns.
/// </summary>
public sealed class SyncTracker
{
    private readonly Lock _gate = new();
    private int _started;
    private int _running;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Started { get { lock (_gate) return _started; } }
    public int Running { get { lock (_gate) return _running; } }

    internal IDisposable Begin()
    {
        lock (_gate)
        {
            _started++;
            _running++;
        }
        Signal();
        return new End(this);
    }

    private sealed class End(SyncTracker tracker) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 1) return;
            lock (tracker._gate) tracker._running--;
            tracker.Signal();
        }
    }

    private void Signal()
    {
        TaskCompletionSource previous;
        lock (_gate)
        {
            previous = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        previous.TrySetResult();
    }

    /// <summary>Waits until more than <paramref name="startedBefore"/> syncs have started and none is running.</summary>
    public async Task WaitAsync(int startedBefore, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_started > startedBefore && _running == 0) return;
                changed = _changed.Task;
            }
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) throw new TimeoutException($"The server's syncs did not settle within {timeout.TotalSeconds}s ({Started} started, {Running} running).");
            await Task.WhenAny(changed, Task.Delay(left));
        }
    }

    /// <summary>Waits until no sync is running.</summary>
    public Task WaitIdleAsync(TimeSpan? timeout = null) => WaitAsync(-1, timeout ?? TimeSpan.FromSeconds(30));
}

/// <summary>The server's <see cref="IAccountSyncService"/> with its syncs counted by a <see cref="SyncTracker"/>.</summary>
internal sealed class TrackedAccountSyncService(IAccountSyncService inner, SyncTracker tracker) : IAccountSyncService
{
    public async Task SyncFriendsAsync(string accountId, CancellationToken ct)
    {
        using var _ = tracker.Begin();
        await inner.SyncFriendsAsync(accountId, ct);
    }

    public async Task RefreshCurrentUserAsync(string accountId, CancellationToken ct)
    {
        using var _ = tracker.Begin();
        await inner.RefreshCurrentUserAsync(accountId, ct);
    }

    public async Task SyncEverythingAsync(string accountId, CancellationToken ct)
    {
        using var _ = tracker.Begin();
        await inner.SyncEverythingAsync(accountId, ct);
    }

    public Task<User?> RefreshUserAsync(string accountId, string userId, CancellationToken ct) => inner.RefreshUserAsync(accountId, userId, ct);
    public Task RefreshProfileAsync(string accountId, string userId, CancellationToken ct) => inner.RefreshProfileAsync(accountId, userId, ct);
    public Task<World?> RefreshWorldAsync(string accountId, string worldId, CancellationToken ct) => inner.RefreshWorldAsync(accountId, worldId, ct);
    public Task<Avatar?> RefreshAvatarAsync(string accountId, string avatarId, CancellationToken ct) => inner.RefreshAvatarAsync(accountId, avatarId, ct);
    public Task<Group?> RefreshGroupAsync(string accountId, string groupId, CancellationToken ct) => inner.RefreshGroupAsync(accountId, groupId, ct);
    public Task<Instance?> RefreshInstanceAsync(string accountId, string location, CancellationToken ct) => inner.RefreshInstanceAsync(accountId, location, ct);
    public Task SyncFavoritesAsync(string accountId, CancellationToken ct) => inner.SyncFavoritesAsync(accountId, ct);
    public Task AddFavoriteAsync(string accountId, string type, string targetId, string group, CancellationToken ct) => inner.AddFavoriteAsync(accountId, type, targetId, group, ct);
    public Task RemoveFavoriteAsync(string accountId, string targetId, CancellationToken ct) => inner.RemoveFavoriteAsync(accountId, targetId, ct);
    public Task AddLocalFavoriteAsync(string accountId, FavoriteType type, string targetId, string group, CancellationToken ct) => inner.AddLocalFavoriteAsync(accountId, type, targetId, group, ct);
}

/// <summary>Makes a successful login through the API return only after the sync it started has finished.</summary>
internal sealed class SettleAfterLoginHandler(SyncTracker tracker) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var isLogin = request.Method == HttpMethod.Post && (path.EndsWith("/accounts/login", StringComparison.Ordinal) || path.EndsWith("/accounts/login/2fa", StringComparison.Ordinal));
        var before = tracker.Started;
        var response = await base.SendAsync(request, cancellationToken);
        if (isLogin && response.StatusCode == HttpStatusCode.OK)
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Contains("\"loggedIn\"", StringComparison.Ordinal)) await tracker.WaitAsync(before, TimeSpan.FromSeconds(30));
        }
        return response;
    }
}
