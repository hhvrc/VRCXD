using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Ingestion;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Regressions;

[Category("Regression")]
public sealed class FriendRegressions
{
    private static async Task<JsonElement[]> FriendLogAsync(VrcxScenario app) =>
        (await app.GetJsonAsync(app.AccountPath("friend-log"))).EnumerateArray().ToArray();

    // A friend whose name changed (a Steam "Name 1a2b" account getting a real VRChat name) showed
    // up as unfriended. It must be a name change, through both the pipeline and the next friend sync.
    [Test, Issue(1228)]
    public async Task Issue_1228_a_friend_changing_their_name_is_a_name_change_not_an_unfriend()
    {
        await using var app = await VrcxScenario.StartAsync();
        var newcomer = app.Vrchat.Friend("usr_newcomer", "Newcomer 89d9", u => u.OnlineAt("wrld_a:1~region(eu)"));
        await app.LoginAsync();

        newcomer.Name("Newcomer");
        await app.Pipeline.FriendUpdateAsync(newcomer);
        await app.Jobs.FriendSyncAsync();

        var log = await FriendLogAsync(app);
        await Assert.That(log.Select(e => e.GetProperty("type").GetString())).DoesNotContain("Unfriend");
        var rename = log.Single(e => e.GetProperty("type").GetString() == "DisplayName");
        await Assert.That((rename.GetProperty("previousDisplayName").GetString(), rename.GetProperty("displayName").GetString())).IsEqualTo(("Newcomer 89d9", "Newcomer"));
        var friends = await app.GetJsonAsync(app.AccountPath("friends"));
        await Assert.That(friends.EnumerateArray().Single().GetProperty("displayName").GetString()).IsEqualTo("Newcomer");
    }

    // Rows deleted from the friend log came back after closing and reopening the app.
    [Test, Issue(1262)]
    public async Task Issue_1262_rows_removed_from_the_friend_log_stay_removed_after_a_restart()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_old1", "Old One");
        app.Vrchat.Friend("usr_old2", "Old Two");
        await app.LoginAsync();
        var dave = app.Vrchat.User("usr_dave", "Dave");
        await app.Pipeline.FriendAddAsync(dave);
        await app.Pipeline.FriendDeleteAsync(dave);
        var keys = (await FriendLogAsync(app)).Where(e => e.GetProperty("userId").GetString() == "usr_dave").Select(e => e.GetProperty("key").GetString()!).ToList();
        await Assert.That(keys.Count).IsEqualTo(2);

        foreach (var key in keys)
            await Assert.That((await app.Api.DeleteAsync(app.AccountPath($"friend-log/{Uri.EscapeDataString(key)}"))).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await app.RestartAsync();
        await app.Jobs.FriendSyncAsync();

        await Assert.That((await FriendLogAsync(app)).Select(e => e.GetProperty("userId").GetString())).DoesNotContain("usr_dave");
    }

    // Friend requests did not appear in the Friend Log. The original logs the ones the account
    // sends (and withdraws) through VRCX; so does the server.
    [Test, Issue(1046)]
    public async Task Issue_1046_friend_requests_sent_and_withdrawn_through_vrcx_are_in_the_friend_log()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.User("usr_erin", "Erin");
        await app.LoginAsync();

        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsync(app.AccountPath("friend-requests/usr_erin"), null), "send request");
        await VrcxScenario.EnsureSuccessAsync(await app.Api.DeleteAsync(app.AccountPath("friend-requests/usr_erin")), "withdraw request");

        app.Vrchat.AssertCalled("POST", "user/usr_erin/friendRequest");
        var types = (await FriendLogAsync(app)).Where(e => e.GetProperty("userId").GetString() == "usr_erin").Select(e => e.GetProperty("type").GetString()!).ToList();
        await Assert.That(types).IsEquivalentTo(["FriendRequest", "CancelFriendRequest"]);
    }

    // Unfavoriting a friend while they were pending offline left them stuck as online. The pending
    // transition must land after its delay whatever happens to favorites in between.
    [Test, Issue(733)]
    public async Task Issue_733_a_friend_pending_offline_goes_offline_after_the_delay_even_when_unfavorited_meanwhile()
    {
        await using var app = await VrcxScenario.StartAsync();
        var frank = app.Vrchat.Friend("usr_frank", "Frank", u => u.OnlineAt("wrld_f:1~region(eu)"));
        app.Vrchat.Favorite("friend", frank.Id, "group_0");
        await app.LoginAsync();
        await app.Pipeline.FriendLocationAsync(frank, "wrld_f:2~region(eu)");

        await app.Pipeline.FriendOfflineAsync(frank);
        app.Vrchat.Unfavorite(frank.Id);
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsync(app.AccountPath("favorites/refresh"), null), "favorites refresh");
        async Task<string?> StateAsync() =>
            (await app.GetJsonAsync(app.AccountPath("friends"))).EnumerateArray().Single(f => f.GetProperty("userId").GetString() == frank.Id).GetProperty("state").GetString();
        await Assert.That(await StateAsync()).IsEqualTo("online");

        app.Time.Advance(PresenceDebouncer.Delay + TimeSpan.FromSeconds(1));
        await Assert.That(await app.Jobs.SweepPendingPresenceAsync()).IsEqualTo(1);

        await Assert.That(await StateAsync()).IsEqualTo("offline");
        await using var db = app.CreateDbContext();
        await Assert.That(await db.Favorites.AnyAsync(f => f.TargetId == frank.Id)).IsFalse();
    }

    // "Left group" notifications for groups never left, every few minutes: a resync with the same
    // group list must not end memberships, and a real leave is recorded once.
    [Test, Issue(790)]
    public async Task Issue_790_an_unchanged_group_list_never_ends_a_membership()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        var club = app.Vrchat.Group("grp_club", "Club");
        var guild = app.Vrchat.Group("grp_guild", "Guild");
        app.Vrchat.Member(me.Id, club, representing: true);
        app.Vrchat.Member(me.Id, guild);
        await app.LoginAsync();

        await app.Jobs.FullSyncAsync();
        await app.Jobs.FullSyncAsync();
        await using (var db = app.CreateDbContext())
        {
            var memberships = await db.GroupMembers.Where(m => m.UserId == me.Id).ToListAsync();
            await Assert.That(memberships.Count).IsEqualTo(2);
            await Assert.That(memberships.All(m => m.LeftAt == null)).IsTrue();
        }

        app.Vrchat.LeaveGroup(me.Id, guild.Id);
        await app.Jobs.FullSyncAsync();
        await app.Jobs.FullSyncAsync();
        await using (var db = app.CreateDbContext())
        {
            var memberships = await db.GroupMembers.Where(m => m.UserId == me.Id).ToListAsync();
            await Assert.That(memberships.Single(m => m.GroupId == club.Id).LeftAt).IsNull();
            await Assert.That(memberships.Count(m => m.GroupId == guild.Id)).IsEqualTo(1);
            await Assert.That(memberships.Single(m => m.GroupId == guild.Id).LeftAt).IsNotNull();
        }
    }

    // Removing a favorite VRChat had already dropped (404) kept a phantom favorite.
    [Test, Issue(269)]
    public async Task Issue_269_removing_a_favorite_vrchat_already_dropped_clears_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.CreateDbContext())
        {
            if (!db.Users.Any(u => u.Id == "usr_gone")) db.Users.Add(new VRCX.Common.VrcxDb.User { Id = "usr_gone", DisplayName = "Gone" });
            db.Favorites.Add(new VRCX.Common.VrcxDb.Favorite { Id = "fvrt_gone", OwnerUserId = app.AccountId!, Type = VRCX.Common.VrcxDb.FavoriteType.Friend, TargetId = "usr_gone", GroupName = "group_0", FirstSeenAt = now, FetchedAt = now });
            await db.SaveChangesAsync();
        }

        // The fake answers 404 for a favorite it does not have, as VRChat does.
        var removed = await app.Api.DeleteAsync(app.AccountPath("favorites/usr_gone"));
        await Assert.That((int)removed.StatusCode).IsEqualTo(204);
        app.Vrchat.AssertCalled("DELETE", "favorites/usr_gone");
        await using var check = app.CreateDbContext();
        await Assert.That(check.Favorites.Any(f => f.TargetId == "usr_gone")).IsFalse();
    }

    // VRChat moved the user icon from userIcon to iconUrl; a friend with only iconUrl must keep it.
    [Test, Issue(1886)]
    public async Task Issue_1886_the_icon_vrchat_sends_as_iconUrl_is_kept_and_preferred()
    {
        await using var app = await VrcxScenario.StartAsync();
        const string Icon = "https://api.vrchat.cloud/api/1/file/file_icon-1886/1/file";
        app.Vrchat.Friend("usr_icon", "Icon Friend", u => u.Set("iconUrl", Icon).Set("userIcon", ""));
        await app.LoginAsync();
        await app.Jobs.FriendSyncAsync();

        var user = await app.GetJsonAsync($"/1/users/usr_icon?account={app.AccountId}");
        await Assert.That(user.GetProperty("iconUrl").GetString()).IsEqualTo(Icon);
        var friend = (await app.GetJsonAsync(app.AccountPath("friends"))).EnumerateArray().Single();
        await Assert.That(friend.GetProperty("thumbnailImageUrl").GetString()).IsEqualTo(Icon);
    }

    // VRChat's default robot image replaced a friend's real avatar picture.
    [Test, Issue(275)]
    public async Task Issue_275_the_default_robot_image_does_not_replace_the_last_avatar()
    {
        await using var app = await VrcxScenario.StartAsync();
        const string Real = "https://api.vrchat.cloud/api/1/file/file_real-275/1/file";
        const string Robot = "https://api.vrchat.cloud/api/1/file/file_0e8c4e32-7444-44ea-ade4-313c010d4bae/1/file";
        var friend = app.Vrchat.Friend("usr_robot", "Robot Friend", u => u.Set("currentAvatarImageUrl", Real).Set("currentAvatarThumbnailImageUrl", Real));
        await app.LoginAsync();
        await app.Jobs.FriendSyncAsync();

        friend.Set("currentAvatarImageUrl", Robot).Set("currentAvatarThumbnailImageUrl", Robot);
        await app.Pipeline.FriendUpdateAsync(friend);

        var user = await app.GetJsonAsync($"/1/users/usr_robot?account={app.AccountId}");
        await Assert.That(user.GetProperty("currentAvatarImageUrl").GetString()).IsEqualTo(Real);
    }
}
