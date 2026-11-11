using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Common.Live;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Regressions;

[Category("Regression")]
public sealed class GameLogRegressions
{
    private const string Here = "wrld_club:777~friends(usr_host)~region(eu)";

    // Join/leave rows for players who are not friends were stored without their user id.
    [Test, Issue(694)]
    public async Task Issue_694_players_who_are_not_friends_keep_their_user_id()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-694");

        await pc.SendLogAsync(
            GameLog.Location(Here, "Club"),
            GameLog.PlayerJoined("Stranger", "usr_stranger"),
            GameLog.PlayerLeft("Stranger", "usr_stranger"));

        var rows = (await app.GetJsonAsync(app.AccountPath("game-log?types=OnPlayerJoined,OnPlayerLeft"))).EnumerateArray().ToList();
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows.All(r => r.GetProperty("userId").GetString() == "usr_stranger")).IsTrue();
    }

    // After quitting VRChat the app kept showing the old location. The quit line ends the stay:
    // everyone's visit closes and the player list is empty.
    [Test, Issue(357)]
    public async Task Issue_357_quitting_the_game_ends_the_stay()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-357");
        await pc.SendLogAsync(GameLog.Location(Here, "Club"), GameLog.PlayerJoined("Guest", "usr_guest"));
        await Assert.That((await app.GetJsonAsync(app.AccountPath("player-list"))).GetProperty("players").GetArrayLength()).IsEqualTo(2);

        await pc.SendLogAsync(GameLog.Quit());

        var left = await live.WaitForAsync<InstanceVisitChanged>(v => !v.Joined && v.UserId == "usr_guest");
        await Assert.That(left.Source).IsEqualTo("game_log");
        await live.WaitForAsync<InstanceVisitChanged>(v => !v.Joined && v.UserId == app.AccountId);
        var list = await app.GetJsonAsync(app.AccountPath("player-list"));
        await Assert.That(list.GetProperty("players").GetArrayLength()).IsEqualTo(0);
        var instance = await app.GetJsonAsync("/1/instances/" + Uri.EscapeDataString(Here));
        await Assert.That(instance.GetProperty("present").GetArrayLength()).IsEqualTo(0);
    }

    // The Player List timer of someone reset to zero when the account befriended them mid-stay.
    [Test, Issue(837)]
    public async Task Issue_837_befriending_someone_in_the_instance_keeps_their_join_time()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-837");
        var joinedAt = app.Time.GetUtcNow();
        await pc.SendLogAsync(GameLog.Location(Here, "Club").At(joinedAt), GameLog.PlayerJoined("Grace", "usr_grace").At(joinedAt));

        app.Time.Advance(TimeSpan.FromMinutes(25));
        var grace = app.Vrchat.User("usr_grace", "Grace", u => u.OnlineAt(Here));
        await app.Pipeline.FriendAddAsync(grace);
        await app.Pipeline.FriendLocationAsync(grace, Here);

        var player = (await app.GetJsonAsync(app.AccountPath("player-list"))).GetProperty("players").EnumerateArray()
            .Single(p => p.GetProperty("userId").GetString() == "usr_grace");
        await Assert.That(player.GetProperty("isFriend").GetBoolean()).IsTrue();
        await Assert.That(player.GetProperty("joinedAt").GetDateTimeOffset()).IsEqualTo(joinedAt);
    }

    // Avatar changes from the game log were dropped by the server. As the original: the first avatar
    // seen per player and repeats are not changes; a switch is, and it is stored with the player.
    [Test, Issue(682)]
    public async Task Issue_682_avatar_changes_are_stored_after_the_first_sighting()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-682");

        await pc.SendLogAsync(
            GameLog.UserAuthenticated(app.AccountId!, "Me"),
            GameLog.Location(Here, "Club"),
            GameLog.PlayerJoined("Dancer", "usr_dancer"),
            GameLog.AvatarChange("Dancer", "Robot"),
            GameLog.AvatarChange("Dancer", "Robot"),
            GameLog.AvatarChange("Dancer", "Fox"),
            GameLog.PlayerLeft("Dancer", "usr_dancer"),
            GameLog.PlayerJoined("Dancer", "usr_dancer"),
            GameLog.AvatarChange("Dancer", "Cat"));

        await using var db = app.CreateDbContext();
        var changes = db.GameLogEvents.Where(e => e.Kind == "AvatarChange").OrderBy(e => e.Id).ToList();
        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Data).IsEqualTo("Fox");
        await Assert.That(changes[0].UserId).IsEqualTo("usr_dancer");
        await Assert.That(changes[0].DisplayName).IsEqualTo("Dancer");
    }

    // "Destination set" was dropped, so stays in the instance being left ran on until the next
    // instance loaded. As the original, the destination line ends every stay there.
    [Test, Issue(29)]
    public async Task Issue_29_a_destination_line_ends_the_stays_in_the_instance_being_left()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-29");
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        const string Next = "wrld_next:1~region(eu)";

        await pc.SendLogAsync(
            GameLog.UserAuthenticated(app.AccountId!, "Me").At(t0),
            GameLog.Location(Here, "Club").At(t0),
            GameLog.PlayerJoined("Guest", "usr_guest").At(t0.AddSeconds(5)),
            GameLog.LocationDestination(Next).At(t0.AddMinutes(2)),
            GameLog.Location(Next, "Next").At(t0.AddMinutes(3)));

        await using var db = app.CreateDbContext();
        var guest = db.InstanceVisits.Single(v => v.UserId == "usr_guest");
        await Assert.That(guest.LeftAt?.ToUnixTimeMilliseconds()).IsEqualTo(t0.AddMinutes(2).ToUnixTimeMilliseconds());
        var mine = db.InstanceVisits.Where(v => v.UserId == app.AccountId).OrderBy(v => v.JoinedAt).ToList();
        await Assert.That(mine.Count).IsEqualTo(2);
        await Assert.That(mine[0].LeftAt?.ToUnixTimeMilliseconds()).IsEqualTo(t0.AddMinutes(2).ToUnixTimeMilliseconds());
        await Assert.That(mine[1].InstanceLocation).IsEqualTo(Next);
        await Assert.That(mine[1].LeftAt).IsNull();
    }

    // The previous-instance player list had no friend or favourite markers.
    [Test, Pr(777)]
    public async Task Pr_777_previous_instance_players_mark_friends_and_favorites()
    {
        await using var app = await VrcxScenario.StartAsync();
        var pal = app.Vrchat.Friend("usr_pal", "Pal");
        app.Vrchat.Friend("usr_fave", "Fave");
        await app.LoginAsync();
        await using (var db = app.CreateDbContext())
        {
            db.Favorites.Add(new VRCX.Common.VrcxDb.Favorite { Id = "fvrt_777", OwnerUserId = app.AccountId!, Type = VRCX.Common.VrcxDb.FavoriteType.Friend, TargetId = "usr_fave", GroupName = "group_0", FirstSeenAt = DateTimeOffset.UtcNow, FetchedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var pc = await app.ConnectDesktopAsync("pc-777");
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        await pc.SendLogAsync(
            GameLog.UserAuthenticated(app.AccountId!, "Me").At(t0),
            GameLog.Location(Here, "Club").At(t0),
            GameLog.PlayerJoined("Pal", pal.Id).At(t0.AddSeconds(1)),
            GameLog.PlayerJoined("Fave", "usr_fave").At(t0.AddSeconds(2)),
            GameLog.PlayerJoined("Stranger", "usr_stranger").At(t0.AddSeconds(3)));

        var players = (await app.GetJsonAsync(app.AccountPath($"previous-instances/players?location={Uri.EscapeDataString(Here)}"))).GetProperty("players").EnumerateArray()
            .ToDictionary(p => p.GetProperty("userId").GetString()!, p => (p.GetProperty("isFriend").GetBoolean(), p.GetProperty("isFavorite").GetBoolean()));
        await Assert.That(players["usr_pal"]).IsEqualTo((true, false));
        await Assert.That(players["usr_fave"]).IsEqualTo((true, true));
        await Assert.That(players["usr_stranger"]).IsEqualTo((false, false));
    }

    // A friend whose location VRChat hides but who is in the game's instance belongs under Same
    // Instance; the sidebar learns who is there from the game log.
    [Test, Pr(1138)]
    public async Task Pr_1138_the_game_instance_lists_who_the_game_log_has_there()
    {
        await using var app = await VrcxScenario.StartAsync();
        var hidden = app.Vrchat.Friend("usr_hidden", "Hidden", u => u.OnlineAt("private"));
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-1138");
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        await pc.SendLogAsync(
            GameLog.UserAuthenticated(app.AccountId!, "Me").At(t0),
            GameLog.Location(Here, "Club").At(t0),
            GameLog.PlayerJoined("Hidden", hidden.Id).At(t0.AddSeconds(1)),
            GameLog.PlayerJoined("Gone", "usr_gone").At(t0.AddSeconds(2)),
            GameLog.PlayerLeft("Gone", "usr_gone").At(t0.AddSeconds(3)));

        var game = await app.GetJsonAsync(app.AccountPath("game/instance"));
        await Assert.That(game.GetProperty("location").GetString()).IsEqualTo(Here);
        await Assert.That(game.GetProperty("playerIds").EnumerateArray().Select(p => p.GetString()!)).IsEquivalentTo(new[] { hidden.Id });
        // When the game joined, for the wrist overlay's timer.
        await Assert.That(Math.Abs((game.GetProperty("joinedAt").GetDateTimeOffset() - t0).TotalMilliseconds)).IsLessThan(1);
    }
}
