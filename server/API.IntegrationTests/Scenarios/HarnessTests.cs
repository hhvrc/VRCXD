using System.Net;
using System.Text.Json;
using VRCX.Common.Live;
using VRCX.Testing;
using VRCX.Testing.Desktop;
using VRCX.Common.Services.Ingestion;

namespace VRCX.API.IntegrationTests.Scenarios;

// The harness itself: each piece does what Server/Testing/README.md says. Scenarios are isolated,
// so these (and every test built on VrcxScenario) run in parallel.
public sealed class HarnessTests
{
    private const string World = "wrld_harness";
    private const string Instance = "wrld_harness:12345~region(eu)";

    [Test]
    public async Task A_login_and_full_sync_work_against_the_stateful_fake()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe("usr_me", "Me");
        app.Vrchat.Friend("usr_alice", "Alice", u => u.OnlineAt(Instance));
        app.Vrchat.Friend("usr_bob", "Bob");
        app.Vrchat.World(World, "Harness World");

        await app.LoginAsync();

        var friends = await app.GetJsonAsync(app.AccountPath("friends"));
        await Assert.That(friends.EnumerateArray().Select(f => f.GetProperty("displayName").GetString()!)).IsEquivalentTo(["Alice", "Bob"]);
        var alice = friends.EnumerateArray().Single(f => f.GetProperty("userId").GetString() == "usr_alice");
        await Assert.That(alice.GetProperty("state").GetString()).IsEqualTo("online");
        app.Vrchat.AssertCalled("GET", "auth/user/friends");
        app.Vrchat.AssertCalled("GET", "favorites");
    }

    [Test]
    public async Task Pipeline_frames_reach_live_clients()
    {
        await using var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_alice", "Alice");
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();

        await app.Pipeline.FriendOnlineAsync(alice, Instance);

        var presence = await live.WaitForAsync<PresenceChanged>(p => p.UserId == "usr_alice");
        await Assert.That(presence.State).IsEqualTo("online");
        await Assert.That(presence.InstanceLocation).IsEqualTo("wrld_harness:12345~region(eu)");
        var visit = await live.WaitForAsync<InstanceVisitChanged>(v => v.UserId == "usr_alice");
        await Assert.That((visit.Joined, visit.Source)).IsEqualTo((true, "pipeline"));
    }

    [Test]
    public async Task The_clock_drives_the_pending_offline_timer()
    {
        await using var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_alice", "Alice");
        await app.LoginAsync();
        await app.Pipeline.FriendOnlineAsync(alice, Instance);
        await using var live = await app.ConnectLiveAsync();

        await app.Pipeline.FriendOfflineAsync(alice);
        await Assert.That(await app.Jobs.SweepPendingPresenceAsync()).IsEqualTo(0);
        await live.AssertNoneAsync<PresenceChanged>();

        app.Time.Advance(PresenceDebouncer.Delay + TimeSpan.FromSeconds(1));
        await Assert.That(await app.Jobs.SweepPendingPresenceAsync()).IsEqualTo(1);
        var offline = await live.WaitForAsync<PresenceChanged>(p => p.UserId == "usr_alice");
        await Assert.That(offline.State).IsEqualTo("offline");
    }

    [Test]
    public async Task A_desktop_app_ships_game_log_lines()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-harness");

        var batch = await pc.SendLogAsync(
            GameLog.Location(Instance, "Harness World"),
            GameLog.PlayerJoined("Guest", "usr_guest"),
            GameLog.VideoPlay("https://example.test/video.mp4", "Guest"));

        await Assert.That(batch.Accepted).IsEqualTo(3);
        var joined = await live.WaitForAsync<InstanceVisitChanged>(v => v.UserId == "usr_guest");
        await Assert.That(joined.Source).IsEqualTo("game_log");
        var log = await app.GetJsonAsync(app.AccountPath("game-log"));
        await Assert.That(log.EnumerateArray().Select(r => r.GetProperty("type").GetString())).Contains("VideoPlay");
    }

    [Test]
    public async Task A_restart_keeps_the_database_and_resumes_the_session()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_alice", "Alice");
        await app.LoginAsync();

        await app.RestartAsync();

        var accounts = await app.GetJsonAsync("/1/accounts");
        var me = accounts.EnumerateArray().Single(a => a.GetProperty("userId").GetString() == "usr_me");
        await Assert.That(me.GetProperty("connected").GetBoolean()).IsTrue();
        await Assert.That((await app.GetJsonAsync(app.AccountPath("friends"))).GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task Scenarios_do_not_share_state()
    {
        await using var first = await VrcxScenario.StartAsync();
        await using var second = await VrcxScenario.StartAsync();
        first.Vrchat.Friend("usr_only_first", "OnlyFirst");
        await first.LoginAsync();
        await second.LoginAsync();

        await Assert.That((await first.GetJsonAsync(first.AccountPath("friends"))).GetArrayLength()).IsEqualTo(1);
        await Assert.That((await second.GetJsonAsync(second.AccountPath("friends"))).GetArrayLength()).IsEqualTo(0);
        var missing = await second.Api.GetAsync("/1/users/usr_only_first");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Overrides_win_over_the_model_and_requests_are_recorded()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Fail("POST", "user/usr_x/friendRequest", HttpStatusCode.BadRequest, "nope");

        var response = await app.Api.PostAsync(app.AccountPath("friend-requests/usr_x"), null);

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        app.Vrchat.AssertCalled("POST", "user/usr_x/friendRequest");
        app.Vrchat.AssertNotCalled("DELETE", "user/usr_x/friendRequest");
        await Assert.That(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetInt32()).IsGreaterThanOrEqualTo(400);
    }
}
