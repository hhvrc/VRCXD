using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Regressions;

[Category("Regression")]
public sealed class NotificationRegressions
{
    private static async Task<JsonElement[]> NotificationsAsync(VrcxScenario app, bool includeHidden = false) =>
        (await app.GetJsonAsync(app.AccountPath($"notifications?pageSize=500&includeHidden={(includeHidden ? "true" : "false")}"))).GetProperty("items").EnumerateArray().ToArray();

    // Accepting a friend request that was already answered elsewhere: VRChat says 404, and the
    // original then hid it instead of leaving a request that can never be acted on.
    [Test, Pr(1025), Issue(1024)]
    public async Task Pr_1025_accepting_a_friend_request_vrchat_already_dropped_hides_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        var bob = app.Vrchat.User("usr_bob", "Bob");
        var request = app.Vrchat.Notification("friendRequest", bob);
        await app.LoginAsync();
        await Assert.That((await NotificationsAsync(app)).Select(n => n.GetProperty("id").GetString())).Contains(request.Id);

        app.Vrchat.Fail("PUT", $"auth/user/notifications/{request.Id}/accept", HttpStatusCode.NotFound, "that friend request could not be found");
        var accept = await app.Api.PostAsync(app.AccountPath($"notifications/{request.Id}/accept"), null);

        await Assert.That(accept.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        app.Vrchat.AssertCalled("PUT", $"auth/user/notifications/{request.Id}/accept");
        await Assert.That((await NotificationsAsync(app)).Select(n => n.GetProperty("id").GetString())).DoesNotContain(request.Id);
    }

    // Upstream 8397c5f8: a refresh VRChat rate-limits stops refreshes for two minutes, so
    // retrying does not keep the 429 going.
    [Test]
    public async Task A_rate_limited_notification_refresh_backs_off()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Fail("GET", "auth/user/notifications", HttpStatusCode.TooManyRequests, "slow down");
        app.Vrchat.ClearRequests();

        var first = await app.Api.PostAsync(app.AccountPath("notifications/refresh"), null);
        await Assert.That(first.StatusCode).IsNotEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.RequestsTo("GET", "auth/user/notifications")).IsNotEmpty();

        app.Vrchat.ClearRequests();
        var second = await app.Api.PostAsync(app.AccountPath("notifications/refresh"), null);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.RequestsTo("GET", "auth/user/notifications")).IsEmpty();
    }

    // The issue itself: a notification answered on another platform could not be deleted from the
    // log. Taking a row off the page is local and must not depend on VRChat at all.
    [Test, Issue(1024)]
    public async Task Issue_1024_removing_a_notification_from_the_log_never_asks_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        var bob = app.Vrchat.User("usr_bob", "Bob");
        var request = app.Vrchat.Notification("friendRequest", bob);
        await app.LoginAsync();
        app.Vrchat.RemoveNotification(request.Id);
        app.Vrchat.ClearRequests();

        var removed = await app.Api.DeleteAsync(app.AccountPath($"notifications/{request.Id}"));

        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.Requests.Where(r => r.Path.Contains(request.Id, StringComparison.Ordinal))).IsEmpty();
        await Assert.That((await NotificationsAsync(app, includeHidden: true)).Select(n => n.GetProperty("id").GetString())).DoesNotContain(request.Id);
    }

    // Invites and invite requests vanished when VRCX restarted: VRChat's own list does not keep
    // them, so a reload that trusts it loses them. The server keeps what the pipeline delivered.
    [Test, Issue(931)]
    public async Task Issue_931_invites_and_invite_requests_survive_a_restart_and_a_refresh()
    {
        await using var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_alice", "Alice");
        await app.LoginAsync();
        var invite = app.Vrchat.Notification("invite", alice, n => n.Details(new JsonObject { ["worldId"] = "wrld_x:1", ["worldName"] = "X" }));
        var request = app.Vrchat.Notification("requestInvite", alice);
        await app.Pipeline.NotificationAsync(invite);
        await app.Pipeline.NotificationAsync(request);
        // VRChat no longer lists either of them.
        app.Vrchat.RemoveNotification(invite.Id);
        app.Vrchat.RemoveNotification(request.Id);

        await app.RestartAsync();
        var refresh = await app.Api.PostAsync(app.AccountPath("notifications/refresh"), null);
        await Assert.That(refresh.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var ids = (await NotificationsAsync(app)).Select(n => n.GetProperty("id").GetString()).ToList();
        await Assert.That(ids).Contains(invite.Id);
        await Assert.That(ids).Contains(request.Id);
    }

    private static async Task EnableAutoAcceptAsync(VrcxScenario app, string mode)
    {
        var settings = JsonNode.Parse((await app.GetJsonAsync(app.AccountPath("settings"))).GetRawText())!.AsObject();
        settings["autoAcceptInviteRequests"] = mode;
        var put = await app.Api.PutAsJsonAsync(app.AccountPath("settings"), settings);
        await VrcxScenario.EnsureSuccessAsync(put, "PUT settings");
    }

    // "Auto Accept Invite Requests" for VRCX favorites stopped answering (and PR 999 also hides
    // the request once answered). Here: a favorite asks while the game is in a public instance.
    [Test, Issue(889), Pr(999)]
    public async Task Issue_889_an_invite_request_from_a_favorite_is_answered_and_hidden()
    {
        await using var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_alice", "Alice", u => u.Active());
        var carol = app.Vrchat.Friend("usr_carol", "Carol", u => u.Active());
        app.Vrchat.Favorite("friend", alice.Id, "group_0");
        await app.LoginAsync();
        await EnableAutoAcceptAsync(app, "All Favorites");
        await using var pc = await app.ConnectDesktopAsync("pc-auto");
        await pc.SendLogAsync(GameLog.Location("wrld_auto:4242~region(eu)", "Auto World"));

        var fromAlice = app.Vrchat.Notification("requestInvite", alice);
        await app.Pipeline.NotificationAsync(fromAlice);
        var fromCarol = app.Vrchat.Notification("requestInvite", carol);
        await app.Pipeline.NotificationAsync(fromCarol);

        var invite = app.Vrchat.AssertCalled("POST", $"invite/{alice.Id}");
        await Assert.That(invite.Json!["instanceId"]!.GetValue<string>()).IsEqualTo("wrld_auto:4242~region(eu)");
        app.Vrchat.AssertCalled("PUT", $"auth/user/notifications/{fromAlice.Id}/hide");
        // Carol is a friend but not a favorite.
        app.Vrchat.AssertNotCalled("POST", $"invite/{carol.Id}");
        var rows = await NotificationsAsync(app, includeHidden: true);
        await Assert.That(rows.Single(n => n.GetProperty("id").GetString() == fromAlice.Id).GetProperty("hidden").GetBoolean()).IsTrue();
        await Assert.That(rows.Single(n => n.GetProperty("id").GetString() == fromCarol.Id).GetProperty("hidden").GetBoolean()).IsFalse();
    }

    // The original noted group changes (rename, new owner, own roles, leaving) in the notification
    // log; the port never made them.
    [Test, Issue(762), Issue(919)]
    public async Task Issue_762_a_renamed_or_left_group_logs_a_group_change()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        var club = app.Vrchat.Group("grp_club", "Club");
        var guild = app.Vrchat.Group("grp_guild", "Guild");
        app.Vrchat.Member(me.Id, club);
        app.Vrchat.Member(me.Id, guild);
        await app.LoginAsync();
        await Assert.That((await NotificationsAsync(app)).Count(n => n.GetProperty("type").GetString() == "groupChange")).IsEqualTo(0);

        club.Set("name", "Club Renamed");
        app.Vrchat.LeaveGroup(me.Id, guild.Id);
        await app.SyncAsync();

        var messages = (await NotificationsAsync(app)).Where(n => n.GetProperty("type").GetString() == "groupChange").Select(n => n.GetProperty("message").GetString()).ToList();
        await Assert.That(messages).Contains("Name changed from Club to Club Renamed");
        await Assert.That(messages).Contains("Left group");
        await Assert.That(messages.Count).IsEqualTo(2);
    }
}
