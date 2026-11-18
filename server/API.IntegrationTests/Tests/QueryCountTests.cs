using Microsoft.EntityFrameworkCore;
using VRCX.Common.Features.Friends;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;
using VRCX.Testing;
using VRCX.Testing.Desktop;
using VRCX.Testing.FakeVrchat;

namespace VRCX.API.IntegrationTests.Tests;

// The syncs and the game log handle lists whose length the account decides (friends, groups,
// players in an instance). A query per item made a 300-friend sync ~900 statements; these bound the
// round trips with constants, so a loop that queries per item again fails here.
public sealed class QueryCountTests
{
    private static async Task<List<FakeUser>> SeedFriendsAsync(VrcxScenario app, int count)
    {
        var worlds = Enumerable.Range(0, 5).Select(i => app.Vrchat.World($"wrld_q{i}", $"World {i}")).ToList();
        var friends = new List<FakeUser>();
        for (var i = 0; i < count; i++)
        {
            var k = i;
            friends.Add(app.Vrchat.Friend($"usr_q{i:D4}", $"Friend {i}", u =>
            {
                if (k % 5 == 0) u.OnlineAt($"{worlds[k % worlds.Count].Id}:{100 + k % 7}~region(eu)");
                else if (k % 5 == 1) u.Active();
                else u.Offline();
            }));
        }
        await Task.CompletedTask;
        return friends;
    }

    private static async Task ExpireFriendListAsync(VrcxScenario app)
    {
        await using var db = app.CreateDbContext();
        await db.Accounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.LastFriendSyncAt, DateTimeOffset.UtcNow.AddHours(-2)));
    }

    [Test]
    public async Task A_friend_sync_of_300_friends_takes_a_bounded_number_of_queries()
    {
        await using var app = await VrcxScenario.StartAsync();
        await SeedFriendsAsync(app, 300);
        await app.LoginAsync();
        await ExpireFriendListAsync(app);

        var trips = await app.Sql.CountAsync(() => app.Jobs.FriendSyncAsync());

        await Assert.That(trips).IsLessThanOrEqualTo(MaxFriendSyncTrips).Because(app.Sql.Describe());
    }

    private const int MaxFriendSyncTrips = 16;

    [Test]
    public async Task A_friends_topic_snapshot_of_300_friends_takes_a_bounded_number_of_queries()
    {
        await using var app = await VrcxScenario.StartAsync();
        await SeedFriendsAsync(app, 300);
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();

        var trips = await app.Sql.CountAsync(() => live.SubscribeSnapshotAsync(1, FriendsTopic.TopicName, app.AccountId));

        await Assert.That(trips).IsLessThanOrEqualTo(MaxFriendsSnapshotTrips).Because(app.Sql.Describe());
    }

    private const int MaxFriendsSnapshotTrips = 2;

    [Test]
    public async Task A_notifications_topic_snapshot_of_300_notifications_takes_a_bounded_number_of_queries()
    {
        await using var app = await VrcxScenario.StartAsync();
        var sender = app.Vrchat.User("usr_q_sender", "Sender");
        for (var i = 0; i < 300; i++) app.Vrchat.Notification("invite", sender, n => n.Message($"invite {i}"));
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();

        var trips = await app.Sql.CountAsync(() => live.SubscribeSnapshotAsync(1, VRCX.Common.Features.Notifications.NotificationsTopic.TopicName, app.AccountId));

        await Assert.That(trips).IsLessThanOrEqualTo(MaxNotificationsSnapshotTrips).Because(app.Sql.Describe());
    }

    /// <summary>The account check and the one page of rows.</summary>
    private const int MaxNotificationsSnapshotTrips = 2;

    [Test]
    public async Task A_full_sync_takes_a_bounded_number_of_queries()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        var friends = await SeedFriendsAsync(app, 100);
        for (var i = 0; i < 20; i++) app.Vrchat.Member(me.Id, app.Vrchat.Group($"grp_q{i:D2}", $"Group {i}"), representing: i == 0);
        app.Vrchat.FavoriteGroup("world", "worlds1");
        app.Vrchat.FavoriteGroup("avatar", "avatars1");
        for (var i = 0; i < 20; i++)
        {
            app.Vrchat.Favorite("world", app.Vrchat.World($"wrld_fav{i:D2}", $"Fav {i}").Id, "worlds1");
            app.Vrchat.Favorite("avatar", app.Vrchat.Avatar($"avtr_fav{i:D2}", $"Avatar {i}").Id, "avatars1");
        }
        for (var i = 0; i < 30; i++) app.Vrchat.Notification("friendRequest", app.Vrchat.User($"usr_qn{i:D2}", $"Requester {i}"));
        await app.LoginAsync();
        await ExpireFriendListAsync(app);
        // Every group's full record is due again, so that path is counted too.
        await using (var db = app.CreateDbContext())
            await db.Groups.ExecuteUpdateAsync(s => s.SetProperty(g => g.FetchedAt, DateTimeOffset.UtcNow.AddDays(-2)));

        var trips = await app.Sql.CountAsync(() => app.Jobs.FullSyncAsync());

        app.Vrchat.AssertCalled("GET", "groups/grp_q19");

        await Assert.That(trips).IsLessThanOrEqualTo(MaxFullSyncTrips).Because(app.Sql.Describe());
    }

    private const int MaxFullSyncTrips = 45;

    [Test]
    public async Task A_game_log_batch_of_20_joins_takes_a_bounded_number_of_queries()
    {
        await using var app = await VrcxScenario.StartAsync();
        var friends = await SeedFriendsAsync(app, 10);
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-queries");
        await pc.SendLogAsync(GameLog.Location("wrld_q0:900~region(eu)", "World 0"));
        var lines = new List<LogEntry>();
        foreach (var f in friends) lines.Add(GameLog.PlayerJoined(f.DisplayName, f.Id));
        for (var i = 0; i < 10; i++) lines.Add(GameLog.PlayerJoined($"Stranger {i}", $"usr_qs{i:D2}"));

        var trips = await app.Sql.CountAsync(() => pc.SendLogAsync(lines.ToArray()));

        await Assert.That(trips).IsLessThanOrEqualTo(MaxGameLogBatchTrips).Because(app.Sql.Describe());
        await using var db = app.CreateDbContext();
        await Assert.That(await db.InstanceVisits.CountAsync(v => v.InstanceLocation == "wrld_q0:900~region(eu)" && v.Source == VisitSource.GameLog && v.LeftAt == null)).IsEqualTo(21);
    }

    private const int MaxGameLogBatchTrips = 12;
}
