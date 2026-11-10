using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The Activity tab of the user page: the rows its charts are built from, and most visited worlds.
public sealed class ActivityTests
{
    [Test]
    public async Task A_friends_activity_has_their_online_events_after_the_last_one_before_the_period_and_the_accounts_own_stays()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var now = DateTimeOffset.UtcNow;
        var friend = new User { Id = "usr_active", DisplayName = "Active", FirstSeenAt = now, UpdatedAt = now };
        await using (var db = app.CreateDbContext())
        {
            db.Users.Add(friend);
            db.PresenceEvents.AddRange(
                new PresenceEvent { UserId = friend.Id, At = now.AddDays(-40), Kind = PresenceKind.Online },
                new PresenceEvent { UserId = friend.Id, At = now.AddDays(-35), Kind = PresenceKind.Online },
                new PresenceEvent { UserId = friend.Id, At = now.AddDays(-2), Kind = PresenceKind.Offline },
                new PresenceEvent { UserId = friend.Id, At = now.AddDays(-1), Kind = PresenceKind.Location, Location = "wrld_x:1" },
                new PresenceEvent { UserId = friend.Id, At = now.AddHours(-3), Kind = PresenceKind.Online });
            await SeedVisitsAsync(db, app.AccountId!, now);
        }

        var activity = await app.GetJsonAsync(app.AccountPath($"users/{friend.Id}/activity?days=30"));

        var events = activity.GetProperty("events");
        await Assert.That(events.GetArrayLength()).IsEqualTo(3);
        await Assert.That(events[0].GetProperty("type").GetString()).IsEqualTo("Online");
        await Assert.That(events[1].GetProperty("type").GetString()).IsEqualTo("Offline");
        var visits = activity.GetProperty("selfVisits");
        await Assert.That(visits.GetArrayLength()).IsEqualTo(3);
        await Assert.That(visits[0].GetProperty("time").GetInt64()).IsEqualTo((long)TimeSpan.FromHours(2).TotalMilliseconds);
    }

    [Test]
    public async Task Most_visited_worlds_sort_by_time_or_count_and_can_leave_out_the_home_world()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.CreateDbContext()) await SeedVisitsAsync(db, app.AccountId!, now);

        var byTime = await app.GetJsonAsync(app.AccountPath("top-worlds?days=30&sortBy=time"));
        await Assert.That(byTime[0].GetProperty("worldId").GetString()).IsEqualTo("wrld_long");
        await Assert.That(byTime[0].GetProperty("worldName").GetString()).IsEqualTo("Long Stay");

        var byCount = await app.GetJsonAsync(app.AccountPath("top-worlds?days=30&sortBy=count"));
        await Assert.That(byCount[0].GetProperty("worldId").GetString()).IsEqualTo("wrld_often");
        await Assert.That(byCount[0].GetProperty("visitCount").GetInt32()).IsEqualTo(2);

        var withoutHome = await app.GetJsonAsync(app.AccountPath("top-worlds?days=30&excludeWorldId=wrld_long"));
        await Assert.That(withoutHome.GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task Own_stays_count_once_end_where_the_next_one_begins_and_fall_back_to_the_connection_without_a_game_log()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var me = app.AccountId!;
        var t = DateTimeOffset.UtcNow.AddDays(-2);
        await using (var db = app.CreateDbContext())
        {
            foreach (var world in new[] { "wrld_a", "wrld_b", "wrld_c" })
            {
                db.Worlds.Add(new World { Id = world, Name = world, FirstSeenAt = t, UpdatedAt = t });
                db.Instances.Add(new Instance { Location = $"{world}:1", WorldId = world, InstanceId = "1", InstanceName = "1", Type = InstanceAccessType.Public, FirstSeenAt = t });
            }
            db.InstanceVisits.AddRange(
                // A replayed log stored this stay twice; the copy closed first is the real one.
                new InstanceVisit { UserId = me, InstanceLocation = "wrld_a:1", JoinedAt = t, LeftAt = t.AddMinutes(20), Source = VisitSource.GameLog },
                new InstanceVisit { UserId = me, InstanceLocation = "wrld_a:1", JoinedAt = t, LeftAt = t.AddHours(6), Source = VisitSource.GameLog },
                // The connection's record of the same stay.
                new InstanceVisit { UserId = me, InstanceLocation = "wrld_a:1", JoinedAt = t.AddSeconds(5), LeftAt = t.AddMinutes(20), Source = VisitSource.Pipeline },
                // Never closed: the desktop app stopped sending. It ends when the next stay elsewhere begins.
                new InstanceVisit { UserId = me, InstanceLocation = "wrld_b:1", JoinedAt = t.AddHours(1), Source = VisitSource.GameLog },
                // Only the connection saw this one.
                new InstanceVisit { UserId = me, InstanceLocation = "wrld_c:1", JoinedAt = t.AddHours(1.5), LeftAt = t.AddHours(2), Source = VisitSource.Pipeline });
            await db.SaveChangesAsync();
        }

        var visits = (await app.GetJsonAsync(app.AccountPath($"users/{me}/activity?days=30"))).GetProperty("selfVisits").EnumerateArray()
            .Select(v => v.GetProperty("time").GetInt64()).ToList();
        await Assert.That(visits).IsEquivalentTo(new[] { (long)TimeSpan.FromMinutes(20).TotalMilliseconds, (long)TimeSpan.FromMinutes(30).TotalMilliseconds, (long)TimeSpan.FromMinutes(30).TotalMilliseconds });

        var byCount = await app.GetJsonAsync(app.AccountPath("top-worlds?days=30&sortBy=count"));
        await Assert.That(byCount.EnumerateArray().All(w => w.GetProperty("visitCount").GetInt32() == 1)).IsTrue();
    }

    private static async Task SeedVisitsAsync(VrcxContext db, string account, DateTimeOffset now)
    {
        foreach (var (world, name) in new[] { ("wrld_long", "Long Stay"), ("wrld_often", "Often") })
        {
            db.Worlds.Add(new World { Id = world, Name = name, FirstSeenAt = now, UpdatedAt = now });
            db.Instances.Add(new Instance { Location = $"{world}:1", WorldId = world, InstanceId = "1", InstanceName = "1", Type = InstanceAccessType.Public, FirstSeenAt = now });
        }
        var stay = now.AddDays(-3);
        db.InstanceVisits.AddRange(
            new InstanceVisit { UserId = account, InstanceLocation = "wrld_long:1", JoinedAt = stay, LeftAt = stay.AddHours(2), Source = VisitSource.GameLog },
            new InstanceVisit { UserId = account, InstanceLocation = "wrld_often:1", JoinedAt = stay.AddHours(3), LeftAt = stay.AddHours(3.25), Source = VisitSource.GameLog },
            new InstanceVisit { UserId = account, InstanceLocation = "wrld_often:1", JoinedAt = stay.AddHours(3), LeftAt = stay.AddHours(3.25), Source = VisitSource.GameLog },
            new InstanceVisit { UserId = account, InstanceLocation = "wrld_often:1", JoinedAt = stay.AddHours(4), LeftAt = stay.AddHours(4.25), Source = VisitSource.GameLog },
            new InstanceVisit { UserId = account, InstanceLocation = "wrld_long:1", JoinedAt = now.AddDays(-60), LeftAt = now.AddDays(-60).AddHours(1), Source = VisitSource.GameLog });
        await db.SaveChangesAsync();
    }
}
