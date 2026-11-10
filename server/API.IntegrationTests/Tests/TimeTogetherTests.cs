using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The user page's Join Count and Time Together: the game log's stays with the user, each once.
public sealed class TimeTogetherTests
{
    [Test]
    public async Task Game_log_stays_count_once_and_open_ones_add_no_time()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var t = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using (var db = app.CreateDbContext())
        {
            db.Users.Add(new User { Id = "usr_pal", DisplayName = "Pal", FirstSeenAt = t, UpdatedAt = t });
            db.Worlds.Add(new World { Id = "wrld_t", Name = "T", FirstSeenAt = t, UpdatedAt = t });
            db.Instances.Add(new Instance { Location = "wrld_t:1", WorldId = "wrld_t", InstanceId = "1", InstanceName = "1", Type = InstanceAccessType.Public, FirstSeenAt = t });
            db.InstanceVisits.AddRange(
                new InstanceVisit { UserId = "usr_pal", InstanceLocation = "wrld_t:1", JoinedAt = t, LeftAt = t.AddMinutes(30), Source = VisitSource.GameLog },
                // The same stay stored twice.
                new InstanceVisit { UserId = "usr_pal", InstanceLocation = "wrld_t:1", JoinedAt = t, LeftAt = t.AddMinutes(30), Source = VisitSource.GameLog },
                new InstanceVisit { UserId = "usr_pal", InstanceLocation = "wrld_t:1", JoinedAt = t.AddHours(2), LeftAt = t.AddHours(3), Source = VisitSource.GameLog },
                // Still there: counted, no time yet.
                new InstanceVisit { UserId = "usr_pal", InstanceLocation = "wrld_t:1", JoinedAt = t.AddHours(5), Source = VisitSource.GameLog },
                // Seen by the connection only, not together with the account.
                new InstanceVisit { UserId = "usr_pal", InstanceLocation = "wrld_t:1", JoinedAt = t.AddHours(4), LeftAt = t.AddHours(4.5), Source = VisitSource.Pipeline });
            await db.SaveChangesAsync();
        }

        var together = await app.GetJsonAsync(app.AccountPath("users/usr_pal/together"));

        await Assert.That(together.GetProperty("joinCount").GetInt32()).IsEqualTo(3);
        await Assert.That(together.GetProperty("timeTogetherMs").GetInt64()).IsEqualTo((long)TimeSpan.FromMinutes(90).TotalMilliseconds);
        // Still there: Last Seen is the stay before, as the original's getUserStats.
        await Assert.That(together.GetProperty("lastSeen").GetDateTimeOffset()).IsEqualTo(t.AddHours(3));
        await Assert.That(together.GetProperty("friendLog").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task Last_seen_is_the_newest_leave_and_the_friend_log_dates_come_newest_first()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var t = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await using (var db = app.CreateDbContext())
        {
            db.Users.Add(new User { Id = "usr_pal", DisplayName = "Pal", FirstSeenAt = t, UpdatedAt = t });
            db.Worlds.Add(new World { Id = "wrld_t", Name = "T", FirstSeenAt = t, UpdatedAt = t });
            db.Instances.Add(new Instance { Location = "wrld_t:1", WorldId = "wrld_t", InstanceId = "1", InstanceName = "1", Type = InstanceAccessType.Public, FirstSeenAt = t });
            db.InstanceVisits.AddRange(
                new InstanceVisit { UserId = "usr_pal", InstanceLocation = "wrld_t:1", JoinedAt = t, LeftAt = t.AddMinutes(30), Source = VisitSource.GameLog },
                new InstanceVisit { UserId = "usr_pal", InstanceLocation = "wrld_t:1", JoinedAt = t.AddDays(1), LeftAt = t.AddDays(1).AddHours(1), Source = VisitSource.GameLog });
            db.Friendships.AddRange(
                new Friendship { OwnerUserId = app.AccountId!, FriendUserId = "usr_pal", FriendNumber = 1, StartedAt = t, EndedAt = t.AddDays(2), LastConfirmedAt = t },
                new Friendship { OwnerUserId = app.AccountId!, FriendUserId = "usr_pal", FriendNumber = 1, StartedAt = t.AddDays(3), LastConfirmedAt = t.AddDays(3) });
            await db.SaveChangesAsync();
        }

        var together = await app.GetJsonAsync(app.AccountPath("users/usr_pal/together"));

        await Assert.That(together.GetProperty("lastSeen").GetDateTimeOffset()).IsEqualTo(t.AddDays(1).AddHours(1));
        var log = together.GetProperty("friendLog");
        await Assert.That(log.GetArrayLength()).IsEqualTo(3);
        await Assert.That(log[0].GetProperty("type").GetString()).IsEqualTo("Friend");
        await Assert.That(log[0].GetProperty("createdAt").GetDateTimeOffset()).IsEqualTo(t.AddDays(3));
        await Assert.That(log[1].GetProperty("type").GetString()).IsEqualTo("Unfriend");
        await Assert.That(log[2].GetProperty("type").GetString()).IsEqualTo("Friend");
    }
}
