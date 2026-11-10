using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.IntegrationTests.Tests;

// Current presence in a real Redis, with the log in a real Postgres behind it.
public sealed class RedisPresenceTests
{
    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

    private async Task<VrcxContext> NewDbAsync() => await Get<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();

    private async Task MoveAsync(string userId, PresenceKind kind, string? location, DateTimeOffset at)
    {
        await using var db = await NewDbAsync();
        var store = Get<VrchatStore>();
        var user = await store.EnsureUserAsync(db, userId, userId, at, CancellationToken.None);
        await store.ApplyPresenceAsync(db, user, kind, location, null, "standalonewindows", at, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task A_move_is_logged_in_postgres_cached_in_redis_and_leaves_the_users_row_alone()
    {
        const string id = "usr_redis_move";
        const string first = "wrld_redis:1~region(eu)";
        const string second = "wrld_redis:2~region(eu)";
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-30);

        await MoveAsync(id, PresenceKind.Online, first, t0);
        DateTimeOffset updatedAt;
        await using (var db = await NewDbAsync()) updatedAt = await db.Users.Where(u => u.Id == id).Select(u => u.UpdatedAt).SingleAsync();

        await MoveAsync(id, PresenceKind.Location, second, t0.AddMinutes(5));

        var cached = await Get<IPresenceCache>().GetAsync(id, CancellationToken.None);
        await Assert.That((cached.State, cached.Location, cached.CurrentInstanceLocation)).IsEqualTo((UserState.Online, second, second));

        await using (var db = await NewDbAsync())
        {
            await Assert.That(await db.Users.Where(u => u.Id == id).Select(u => u.UpdatedAt).SingleAsync()).IsEqualTo(updatedAt);
            await Assert.That(await db.PresenceEvents.CountAsync(e => e.UserId == id)).IsEqualTo(2);

            // The pipeline visits now come out of the log.
            var visits = await db.InstanceVisits.AsNoTracking().Where(v => v.UserId == id).OrderBy(v => v.JoinedAt).ToListAsync();
            await Assert.That(visits.Select(v => (v.InstanceLocation, v.Source, v.LeftAt is null)))
                .IsEquivalentTo(new[] { (first, VisitSource.Pipeline, false), (second, VisitSource.Pipeline, true) });
        }

        var cache = Get<IPresenceCache>();
        await Assert.That((await cache.GetOccupantsAsync(first, CancellationToken.None)).Count).IsEqualTo(0);
        await Assert.That((await cache.GetOccupantsAsync(second, CancellationToken.None)).Single().UserId).IsEqualTo(id);

        using var client = Factory.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<JsonElement>($"/1/users/{id}");
        await Assert.That(body.GetProperty("location").GetString()).IsEqualTo(second);
        await Assert.That(body.GetProperty("platform").GetString()).IsEqualTo("standalonewindows");
    }

    [Test]
    public async Task A_wiped_redis_is_rebuilt_from_the_log_and_a_move_in_progress_survives_reconciling()
    {
        const string gone = "usr_redis_wiped";
        const string moving = "usr_redis_moving";
        const string location = "wrld_redis:3~region(eu)";
        // Whole seconds: Postgres keeps microseconds, so finer ticks would not come back equal.
        var now = DateTimeOffset.UtcNow;
        var t0 = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero).AddMinutes(-20);
        await MoveAsync(gone, PresenceKind.Online, location, t0);
        await MoveAsync(moving, PresenceKind.Online, location, t0);
        await MoveAsync(moving, PresenceKind.Location, "traveling", t0.AddMinutes(1));

        var redis = Get<IConnectionMultiplexer>().GetDatabase();
        await redis.HashDeleteAsync("vrcx:presence", gone);
        await redis.KeyDeleteAsync("vrcx:presence:instance:" + location);
        await Assert.That((await Get<IPresenceCache>().GetAsync(gone, CancellationToken.None)).Location).IsNull();

        await Get<RedisPresenceCache>().ReconcileAsync(CancellationToken.None);

        var restored = await Get<IPresenceCache>().GetAsync(gone, CancellationToken.None);
        await Assert.That((restored.State, restored.Location, restored.LocationSince)).IsEqualTo((UserState.Online, location, (DateTimeOffset?)t0));
        await Assert.That((await Get<IPresenceCache>().GetOccupantsAsync(location, CancellationToken.None)).Select(o => o.UserId)).Contains(gone);
        await Assert.That((await Get<IPresenceCache>().GetAsync(moving, CancellationToken.None)).Location).IsEqualTo("traveling");

        // Arriving still logs the move from where they were before traveling.
        await MoveAsync(moving, PresenceKind.Location, "wrld_redis:4~region(eu)", t0.AddMinutes(2));
        await using var db = await NewDbAsync();
        var arrival = await db.PresenceEvents.Where(e => e.UserId == moving).OrderByDescending(e => e.At).FirstAsync();
        await Assert.That(arrival.PreviousLocation).IsEqualTo(location);
    }

    [Test]
    public async Task Online_for_counts_from_coming_online_not_from_the_last_move_and_survives_a_rebuild()
    {
        const string id = "usr_redis_state_since";
        // Whole seconds, so the rebuilt time comes back equal.
        var now = DateTimeOffset.UtcNow;
        var t0 = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero).AddMinutes(-40);
        await MoveAsync(id, PresenceKind.Online, "wrld_redis:5~region(eu)", t0);
        await MoveAsync(id, PresenceKind.Location, "wrld_redis:6~region(eu)", t0.AddMinutes(10));

        var cached = await Get<IPresenceCache>().GetAsync(id, CancellationToken.None);
        await Assert.That((cached.StateSince, cached.PresenceChangedAt)).IsEqualTo(((DateTimeOffset?)t0, (DateTimeOffset?)t0.AddMinutes(10)));

        using var client = Factory.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<JsonElement>($"/1/users/{id}");
        await Assert.That(body.GetProperty("stateSince").GetDateTimeOffset()).IsEqualTo(t0);

        await Get<IConnectionMultiplexer>().GetDatabase().HashDeleteAsync("vrcx:presence", id);
        await Get<RedisPresenceCache>().ReconcileAsync(CancellationToken.None);
        await Assert.That((await Get<IPresenceCache>().GetAsync(id, CancellationToken.None)).StateSince).IsEqualTo((DateTimeOffset?)t0);

        // Going offline starts Offline For.
        await MoveAsync(id, PresenceKind.Offline, null, t0.AddMinutes(20));
        await Assert.That((await Get<IPresenceCache>().GetAsync(id, CancellationToken.None)).StateSince).IsEqualTo((DateTimeOffset?)t0.AddMinutes(20));
    }

    [Test]
    public async Task Pending_offline_changes_wait_in_redis_keep_their_first_start_and_can_be_cancelled()
    {
        var queue = Get<IPendingPresenceQueue>();
        var t0 = DateTimeOffset.UtcNow;
        await queue.ScheduleAsync("usr_pending_a", new PendingPresence(PresenceKind.Offline, "android", t0), CancellationToken.None);
        await queue.ScheduleAsync("usr_pending_a", new PendingPresence(PresenceKind.Active, null, t0.AddSeconds(30)), CancellationToken.None);
        await queue.ScheduleAsync("usr_pending_b", new PendingPresence(PresenceKind.Offline, null, t0), CancellationToken.None);
        await queue.CancelAsync("usr_pending_b", CancellationToken.None);

        await Assert.That((await queue.TakeDueAsync(t0.AddSeconds(-1), CancellationToken.None)).Count).IsEqualTo(0);
        var due = await queue.TakeDueAsync(t0.AddSeconds(1), CancellationToken.None);
        await Assert.That(due.Single().UserId).IsEqualTo("usr_pending_a");
        await Assert.That((due.Single().Pending.Kind, due.Single().Pending.Platform)).IsEqualTo((PresenceKind.Offline, "android"));
        await Assert.That((await queue.TakeDueAsync(t0.AddSeconds(1), CancellationToken.None)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_new_month_partition_takes_its_rows_out_of_the_default_one()
    {
        const string id = "usr_partition";
        // Far enough ahead that no partition exists for it yet.
        var future = new DateTimeOffset(2031, 3, 15, 0, 0, 0, TimeSpan.Zero);
        await MoveAsync(id, PresenceKind.Online, "wrld_redis:5~region(eu)", future);

        await PresencePartitionService.EnsurePartitionsAsync(Factory.Services, future, CancellationToken.None);

        await using var db = await NewDbAsync();
        await Assert.That(await db.PresenceEvents.CountAsync(e => e.UserId == id)).IsEqualTo(1);
        var partition = await db.Database.SqlQueryRaw<string>(
            "SELECT tableoid::regclass::text AS \"Value\" FROM presence_events WHERE user_id = {0}", id).SingleAsync();
        await Assert.That(partition).IsEqualTo("presence_events_y2031m03");
    }

    [Test]
    public async Task Author_names_follow_the_user_but_keep_the_stored_one_for_unknown_authors()
    {
        const string author = "usr_author_named";
        await using (var db = await NewDbAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.Add(new User { Id = author, DisplayName = "New Name", FirstSeenAt = now, UpdatedAt = now });
            db.Avatars.Add(new Avatar { Id = "avtr_named", Name = "A", AuthorId = author, AuthorName = "Old Name", FirstSeenAt = now });
            db.Avatars.Add(new Avatar { Id = "avtr_unnamed", Name = "B", AuthorName = "Only Name", FirstSeenAt = now });
            await db.SaveChangesAsync();
        }

        using var client = Factory.CreateAuthenticatedClient();
        var named = await client.GetFromJsonAsync<JsonElement>("/1/avatars/avtr_named");
        await Assert.That(named.GetProperty("authorName").GetString()).IsEqualTo("New Name");
        var unnamed = await client.GetFromJsonAsync<JsonElement>("/1/avatars/avtr_unnamed");
        await Assert.That(unnamed.GetProperty("authorName").GetString()).IsEqualTo("Only Name");

        await using var check = await NewDbAsync();
        await Assert.That(await check.Avatars.Where(a => a.Id == "avtr_named").Select(a => a.AuthorName).SingleAsync()).IsEqualTo("Old Name");
    }
}
