using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VRCX.API.Services;
using VRCX.Common.Services;
using VRCX.Common.Services.Sessions;
using VRCX.Common.Services.Sync;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// VRChat limits requests per IP, and the server shares one budget across every account. These pin
// the reads that used to go to VRChat every time: the hourly sync's GET groups/{id} for every group,
// GET /me on every sidebar reload, and the user dialog's live tabs on every open.
public sealed class VrchatRequestBudgetTests
{
    private static async Task<JsonElement[]> GroupChangesAsync(VrcxScenario app) =>
        (await app.GetJsonAsync(app.AccountPath("notifications?pageSize=500"))).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("type").GetString() == "groupChange").ToArray();

    private static async Task BackdateGroupsAsync(VrcxScenario app, TimeSpan by)
    {
        await using var db = app.CreateDbContext();
        await db.Groups.ExecuteUpdateAsync(s => s.SetProperty(g => g.FetchedAt, g => g.FetchedAt - by));
    }

    // ---- the hourly full sync's groups -------------------------------------

    [Test]
    public async Task The_full_sync_reads_a_group_again_only_when_its_list_entry_changed()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        var club = app.Vrchat.Group("grp_budget_club", "Club");
        var guild = app.Vrchat.Group("grp_budget_guild", "Guild");
        app.Vrchat.Member(me.Id, club);
        app.Vrchat.Member(me.Id, guild);
        await app.LoginAsync();
        app.Vrchat.AssertCalled("GET", "groups/grp_budget_club");

        app.Vrchat.ClearRequests();
        await app.Jobs.FullSyncAsync();
        app.Vrchat.AssertCalled("GET", $"users/{me.Id}/groups");
        app.Vrchat.AssertNotCalled("GET", "groups/grp_budget_club");
        app.Vrchat.AssertNotCalled("GET", "groups/grp_budget_guild");

        club.Set("memberCount", 2);
        app.Vrchat.ClearRequests();
        await app.Jobs.FullSyncAsync();
        app.Vrchat.AssertCalled("GET", "groups/grp_budget_club");
        app.Vrchat.AssertNotCalled("GET", "groups/grp_budget_guild");
        await using var db = app.CreateDbContext();
        await Assert.That((await db.Groups.SingleAsync(g => g.Id == "grp_budget_club")).MemberCount).IsEqualTo(2);
    }

    [Test]
    public async Task A_group_not_read_for_a_day_is_read_again()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        app.Vrchat.Member(me.Id, app.Vrchat.Group("grp_budget_old", "Old"));
        await app.LoginAsync();

        await BackdateGroupsAsync(app, AccountSyncService.GroupMaxAge + TimeSpan.FromMinutes(1));
        app.Vrchat.ClearRequests();
        await app.Jobs.FullSyncAsync();

        app.Vrchat.AssertCalled("GET", "groups/grp_budget_old");
    }

    [Test]
    public async Task A_new_owner_in_the_group_list_still_logs_a_group_change()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        var club = app.Vrchat.Group("grp_budget_owner", "Club");
        app.Vrchat.Member(me.Id, club);
        await app.LoginAsync();
        await Assert.That(await GroupChangesAsync(app)).IsEmpty();

        club.Set("ownerId", "usr_budget_new_owner");
        await app.Jobs.FullSyncAsync();

        app.Vrchat.AssertCalled("GET", "groups/grp_budget_owner");
        var messages = (await GroupChangesAsync(app)).Select(n => n.GetProperty("message").GetString()).ToList();
        await Assert.That(messages.Count(m => m!.StartsWith("Owner changed from usr_group_owner to", StringComparison.Ordinal))).IsEqualTo(1);
    }

    // The list does not carry the account's roles: a role change the pipeline missed shows once the
    // group's full record is read again, within a day.
    [Test]
    public async Task A_role_change_logs_a_group_change_when_the_full_record_is_read_again()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();
        var club = app.Vrchat.Group("grp_budget_roles", "Club");
        club.Set("roles", new JsonArray(new JsonObject { ["id"] = "grol_budget_officer", ["groupId"] = club.Json["id"]!.GetValue<string>(), ["name"] = "Officer" }));
        club.Set("myMember", new JsonObject { ["userId"] = me.Id, ["roleIds"] = new JsonArray() });
        app.Vrchat.Member(me.Id, club);
        await app.LoginAsync();

        club.Set("myMember", new JsonObject { ["userId"] = me.Id, ["roleIds"] = new JsonArray("grol_budget_officer") });
        await app.Jobs.FullSyncAsync();
        await Assert.That(await GroupChangesAsync(app)).IsEmpty();

        await BackdateGroupsAsync(app, AccountSyncService.GroupMaxAge + TimeSpan.FromMinutes(1));
        await app.Jobs.FullSyncAsync();

        var messages = (await GroupChangesAsync(app)).Select(n => n.GetProperty("message").GetString()!).ToList();
        await Assert.That(messages).IsEquivalentTo(["Role Officer added"]);
    }

    // ---- GET /me -------------------------------------------------------------

    [Test]
    public async Task The_own_row_is_answered_from_the_last_auth_user_for_a_minute()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe(configure: u => u.Status("join me", "around"));
        await app.LoginAsync();
        app.Vrchat.ClearRequests();

        var first = await app.GetJsonAsync(app.AccountPath("me"));
        var second = await app.GetJsonAsync(app.AccountPath("me"));

        app.Vrchat.AssertNotCalled("GET", "auth/user");
        await Assert.That(first.GetProperty("status").GetString()).IsEqualTo("join me");
        await Assert.That(second.GetProperty("statusDescription").GetString()).IsEqualTo("around");

        // Older than a minute, or asked for, it is read again.
        app.Time.Advance(CurrentUserCache.MaxAge + TimeSpan.FromSeconds(1));
        await app.GetJsonAsync(app.AccountPath("me"));
        await Assert.That(app.Vrchat.CountOf("GET", "auth/user")).IsEqualTo(1);
        await app.GetJsonAsync(app.AccountPath("me?refresh=true"));
        await Assert.That(app.Vrchat.CountOf("GET", "auth/user")).IsEqualTo(2);
    }

    [Test]
    public async Task A_pipeline_user_update_shows_in_the_own_row_without_asking_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe(configure: u => u.Status("active", "before"));
        await app.LoginAsync();
        await app.GetJsonAsync(app.AccountPath("me"));
        app.Vrchat.ClearRequests();

        me.Status("busy", "after");
        await app.Pipeline.UserUpdateAsync();
        var row = await app.GetJsonAsync(app.AccountPath("me"));

        app.Vrchat.AssertNotCalled("GET", "auth/user");
        await Assert.That(row.GetProperty("status").GetString()).IsEqualTo("busy");
        await Assert.That(row.GetProperty("statusDescription").GetString()).IsEqualTo("after");
    }

    [Test]
    public async Task The_sidebar_refresh_reads_auth_user_once_and_the_own_row_follows_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe(configure: u => u.Status("active", "before"));
        await app.LoginAsync();
        await app.GetJsonAsync(app.AccountPath("me"));
        me.Status("ask me", "changed elsewhere");
        app.Vrchat.ClearRequests();

        // The refresh button: POST friends/refresh, then GET /me.
        var refresh = await app.Api.PostAsync(app.AccountPath("friends/refresh"), null);
        await Assert.That(refresh.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var row = await app.GetJsonAsync(app.AccountPath("me"));

        await Assert.That(app.Vrchat.CountOf("GET", "auth/user")).IsEqualTo(1);
        await Assert.That(row.GetProperty("status").GetString()).IsEqualTo("ask me");
    }

    [Test]
    public async Task A_change_vrchat_does_not_answer_with_the_account_reads_it_again()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe();
        app.Vrchat.World("wrld_budget_home", "Home");
        await app.LoginAsync();
        await app.GetJsonAsync(app.AccountPath("me"));
        app.Vrchat.ClearRequests();

        var home = await app.Api.PutAsync(app.AccountPath("home/wrld_budget_home"), null);
        await Assert.That(home.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await app.GetJsonAsync(app.AccountPath("me"));

        app.Vrchat.AssertCalled("GET", "auth/user");
    }

    // ---- the user dialog's live tabs -------------------------------------------

    [Test]
    public async Task A_users_dialog_tabs_are_read_from_vrchat_once_in_five_minutes()
    {
        await using var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_budget_alice", "Alice");
        await app.LoginAsync();
        app.Vrchat.ClearRequests();

        foreach (var tab in new[] { "mutual-counts", "public-profile", "groups" })
            for (var i = 0; i < 3; i++)
                await app.GetJsonAsync(app.AccountPath($"users/{alice.Id}/{tab}"));

        await Assert.That(app.Vrchat.CountOf("GET", $"users/{alice.Id}/mutuals")).IsEqualTo(1);
        await Assert.That(app.Vrchat.CountOf("GET", $"profile/{alice.Id}")).IsEqualTo(1);
        await Assert.That(app.Vrchat.CountOf("GET", $"users/{alice.Id}/groups")).IsEqualTo(1);
        await Assert.That(app.Vrchat.CountOf("GET", $"users/{alice.Id}/groups/represented")).IsEqualTo(1);

        app.Time.Advance(VrchatReadCache.DialogTtl + TimeSpan.FromSeconds(1));
        await app.GetJsonAsync(app.AccountPath($"users/{alice.Id}/mutual-counts"));
        await Assert.That(app.Vrchat.CountOf("GET", $"users/{alice.Id}/mutuals")).IsEqualTo(2);
    }

    [Test]
    public async Task A_write_through_the_api_drops_what_was_kept()
    {
        await using var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_budget_write", "Alice");
        await app.LoginAsync();
        app.Vrchat.ClearRequests();

        await app.GetJsonAsync(app.AccountPath($"users/{alice.Id}/mutual-counts"));
        var memo = await app.Api.PutAsJsonAsync($"/1/users/{alice.Id}/memo", new { memo = "hi" });
        await Assert.That(memo.IsSuccessStatusCode).IsTrue();
        await app.GetJsonAsync(app.AccountPath($"users/{alice.Id}/mutual-counts"));

        await Assert.That(app.Vrchat.CountOf("GET", $"users/{alice.Id}/mutuals")).IsEqualTo(2);
    }

    [Test]
    public async Task Group_instances_and_an_instances_live_state_are_kept_briefly()
    {
        await using var app = await VrcxScenario.StartAsync();
        const string location = "wrld_budget_live:1~region(eu)";
        app.Vrchat.World("wrld_budget_live", "Live");
        app.Vrchat.Instance(location);
        await app.LoginAsync();
        app.Vrchat.ClearRequests();

        for (var i = 0; i < 3; i++)
        {
            await app.GetJsonAsync(app.AccountPath("group-instances"));
            await app.GetJsonAsync(app.AccountPath($"instances/live?location={Uri.EscapeDataString(location)}"));
        }

        await Assert.That(app.Vrchat.CountOf("GET", $"users/{app.AccountId}/instances/groups")).IsEqualTo(1);
        await Assert.That(app.Vrchat.Requests.Count(r => r.Path.StartsWith("instances/", StringComparison.Ordinal))).IsEqualTo(1);

        app.Time.Advance(VrchatReadCache.LiveInstanceTtl + TimeSpan.FromSeconds(1));
        await app.GetJsonAsync(app.AccountPath($"instances/live?location={Uri.EscapeDataString(location)}"));
        await app.GetJsonAsync(app.AccountPath("group-instances"));
        await Assert.That(app.Vrchat.Requests.Count(r => r.Path.StartsWith("instances/", StringComparison.Ordinal))).IsEqualTo(2);
        await Assert.That(app.Vrchat.CountOf("GET", $"users/{app.AccountId}/instances/groups")).IsEqualTo(1);
    }

    [Test]
    public async Task A_refresh_asked_for_again_within_30_seconds_does_not_reach_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        const string location = "wrld_budget_refresh:7~region(eu)";
        var bob = app.Vrchat.User("usr_budget_bob", "Bob");
        app.Vrchat.World("wrld_budget_refresh", "Refresh");
        app.Vrchat.Group("grp_budget_refresh", "Refresh");
        app.Vrchat.Instance(location);
        await app.LoginAsync();
        var account = app.AccountId;
        string[] paths =
        [
            $"/1/users/{bob.Id}?refresh=true&account={account}",
            $"/1/worlds/wrld_budget_refresh?refresh=true&account={account}",
            $"/1/groups/grp_budget_refresh?refresh=true&account={account}",
            $"/1/instances/{Uri.EscapeDataString(location)}?refresh=true&account={account}",
        ];
        app.Vrchat.ClearRequests();

        foreach (var path in paths)
        {
            await app.GetJsonAsync(path);
            await app.GetJsonAsync(path);
        }

        await Assert.That(app.Vrchat.CountOf("GET", $"users/{bob.Id}")).IsEqualTo(1);
        await Assert.That(app.Vrchat.CountOf("GET", "worlds/wrld_budget_refresh")).IsEqualTo(1);
        await Assert.That(app.Vrchat.CountOf("GET", "groups/grp_budget_refresh")).IsEqualTo(1);
        await Assert.That(app.Vrchat.Requests.Count(r => r.Path.StartsWith("instances/", StringComparison.Ordinal))).IsEqualTo(1);

        app.Time.Advance(VrchatReadCache.RefreshMinAge + TimeSpan.FromSeconds(1));
        await app.GetJsonAsync(paths[0]);
        await Assert.That(app.Vrchat.CountOf("GET", $"users/{bob.Id}")).IsEqualTo(2);
    }
}
