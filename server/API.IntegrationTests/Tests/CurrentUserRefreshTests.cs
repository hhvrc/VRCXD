using Microsoft.EntityFrameworkCore;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream PR 881: VRChat asked clients to poll less. As the original, the 5-minute job refreshes the
// current user and leaves the friend list to the hourly refresh, unless the stored one is over an hour old.
public sealed class CurrentUserRefreshTests
{
    private static int Count(VrcxScenario app, string prefix)
    {
        lock (app.Vrchat.Calls) return app.Vrchat.Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Test]
    public async Task The_five_minute_job_refreshes_the_current_user_and_not_a_fresh_friend_list()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_pal", "Pal");
        await app.LoginAsync();
        var friendCalls = Count(app, "GET auth/user/friends");
        var userCalls = Count(app, "GET auth/user") - friendCalls;

        await app.Jobs.FriendSyncAsync();

        await Assert.That(Count(app, "GET auth/user/friends")).IsEqualTo(friendCalls);
        await Assert.That(Count(app, "GET auth/user") - Count(app, "GET auth/user/friends")).IsGreaterThan(userCalls);
    }

    [Test]
    public async Task A_friend_list_over_an_hour_old_is_fetched_again()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_pal", "Pal");
        await app.LoginAsync();
        await using (var db = app.CreateDbContext())
            await db.Accounts.Where(a => a.UserId == app.AccountId).ExecuteUpdateAsync(a => a.SetProperty(x => x.LastFriendSyncAt, DateTimeOffset.UtcNow.AddHours(-2)));
        var friendCalls = Count(app, "GET auth/user/friends");

        await app.Jobs.FriendSyncAsync();

        await Assert.That(Count(app, "GET auth/user/friends")).IsGreaterThan(friendCalls);
    }
}
