using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// The original's "gameLog support for Quest": with no PC game running, the account's own location
// from VRChat is what the game log records.
public sealed class QuestGameLogTests
{
    private const string First = "wrld_quest:1~region(eu)";
    private const string Second = "wrld_quest:2~region(eu)";

    private static async Task<List<InstanceVisit>> MineAsync(VrcxScenario app)
    {
        await using var db = app.CreateDbContext();
        return await db.InstanceVisits.AsNoTracking().Where(v => v.UserId == app.AccountId && v.Source == VisitSource.GameLog).OrderBy(v => v.JoinedAt).ToListAsync();
    }

    [Test]
    public async Task Without_a_pc_game_the_accounts_own_location_is_logged()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        await app.Pipeline.UserLocationAsync(First);
        await app.Pipeline.UserLocationAsync(First);
        await app.Pipeline.UserLocationAsync(Second);

        var mine = await MineAsync(app);
        await Assert.That(mine.Select(v => v.InstanceLocation)).IsEquivalentTo(new[] { First, Second });
        await Assert.That(mine[0].LeftAt).IsNotNull();
        await Assert.That(mine[1].LeftAt).IsNull();

        await app.Pipeline.UserLocationAsync("offline");
        await Assert.That((await MineAsync(app)).All(v => v.LeftAt is not null)).IsTrue();
    }

    [Test]
    public async Task With_the_game_running_on_a_pc_its_game_log_is_left_alone()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-quest");
        await pc.GameRunningAsync(true);
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"), GameLog.Location(First, "Quest"));

        await app.Pipeline.UserLocationAsync(Second);

        var mine = await MineAsync(app);
        await Assert.That(mine.Select(v => v.InstanceLocation)).IsEquivalentTo(new[] { First });
        await Assert.That(mine[0].LeftAt).IsNull();

        // The original's runUpdateCurrentUserLocationFlow: the game log is where the account is.
        var me = await app.GetJsonAsync(app.AccountPath("me"));
        await Assert.That(me.GetProperty("currentLocation").GetString()).IsEqualTo(First);
    }

    // A visit opened from VRChat's location while no PC reported the game left the next game log
    // Location line hitting the one-open-visit index, and every join after it was then skipped.
    [Test]
    public async Task A_pc_game_log_takes_over_from_a_visit_logged_while_the_game_was_not_running()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-late");

        await app.Pipeline.UserLocationAsync(First);
        await Assert.That((await MineAsync(app)).Single().InstanceLocation).IsEqualTo(First);

        await pc.GameRunningAsync(true);
        var result = await pc.SendLogAsync(
            GameLog.UserAuthenticated(app.AccountId!, "Me"),
            GameLog.Location(Second, "Quest"),
            GameLog.PlayerJoined("Alice", "usr_alice"));
        await Assert.That(result.Accepted).IsEqualTo(3);

        var mine = await MineAsync(app);
        await Assert.That(mine.Select(v => v.InstanceLocation)).IsEquivalentTo(new[] { First, Second });
        await Assert.That(mine.Single(v => v.InstanceLocation == First).LeftAt).IsNotNull();
        await Assert.That(mine.Single(v => v.InstanceLocation == Second).LeftAt).IsNull();

        await using var db = app.CreateDbContext();
        var alice = await db.InstanceVisits.AsNoTracking().SingleAsync(v => v.UserId == "usr_alice" && v.Source == VisitSource.GameLog);
        await Assert.That(alice.InstanceLocation).IsEqualTo(Second);
        await Assert.That(app.Logs.Errors).IsEmpty();
    }
}
