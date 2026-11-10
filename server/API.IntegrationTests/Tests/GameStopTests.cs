using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;
using VRCX.Testing;
using VRCX.Common.Websocket;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// Stays the game log opened must end when the game does, even without a quit line, and a resent
// log must not store a stay twice. Both left stays open for good, which inflated playtime.
public sealed class GameStopTests
{
    private const string Here = "wrld_stop:1~region(eu)";

    [Test]
    public async Task VRChat_stopping_without_a_quit_line_ends_everyones_stay()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-stop");
        await pc.GameRunningAsync(true);
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"), GameLog.Location(Here, "Stop"), GameLog.PlayerJoined("Guest", "usr_stop_guest"));

        await pc.GameRunningAsync(false);

        await using var db = app.CreateDbContext();
        var open = await db.InstanceVisits.CountAsync(v => v.InstanceLocation == Here && v.Source == VisitSource.GameLog && v.LeftAt == null);
        await Assert.That(open).IsEqualTo(0);
    }

    [Test]
    public async Task Catching_up_on_old_log_while_VRChat_is_closed_keeps_the_instance_until_reading_goes_quiet()
    {
        LiveSocketHandler.QuietCloseDelay = TimeSpan.FromSeconds(1);
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-catchup");
        await pc.GameRunningAsync(false);
        var at = DateTimeOffset.UtcNow.AddHours(-3);
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me").At(at), GameLog.Location(Here, "Stop").At(at.AddSeconds(1)), GameLog.PlayerJoined("First", "usr_catch_first").At(at.AddSeconds(2)));

        // The next batch of the same catch-up still has its instance.
        await pc.SendLogAsync(GameLog.PlayerJoined("Second", "usr_catch_second").At(at.AddMinutes(5)));

        await using (var db = app.CreateDbContext())
            await Assert.That(await db.InstanceVisits.AnyAsync(v => v.UserId == "usr_catch_second" && v.InstanceLocation == Here)).IsTrue();
        // Once reading is quiet, everything open ends with the last line read.
        for (var i = 0; i < 50; i++)
        {
            await using var db = app.CreateDbContext();
            if (!await db.InstanceVisits.AnyAsync(v => v.InstanceLocation == Here && v.LeftAt == null)) break;
            await Task.Delay(100);
        }
        await using (var db = app.CreateDbContext())
        {
            // It ends with the last line read; the test desktop's own sign-in line counts too, so not later than now.
            var mine = await db.InstanceVisits.SingleAsync(v => v.UserId == app.AccountId && v.InstanceLocation == Here);
            await Assert.That(mine.LeftAt).IsNotNull();
            await Assert.That(mine.LeftAt!.Value).IsGreaterThanOrEqualTo(at.AddMinutes(5)).And.IsLessThanOrEqualTo(DateTimeOffset.UtcNow);
        }
    }

    [Test]
    public async Task One_line_already_stored_does_not_lose_the_rest_of_its_batch()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-dupe");
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);

        // The same video in the same second is one row; the stay around it must still be stored.
        var result = await pc.SendLogAsync(
            GameLog.UserAuthenticated(app.AccountId!, "Me").At(at),
            GameLog.Location(Here, "Stop").At(at.AddSeconds(1)),
            GameLog.VideoPlay("https://example.test/v").At(at.AddSeconds(2)),
            GameLog.VideoPlay("https://example.test/v").At(at.AddSeconds(2)),
            GameLog.PlayerJoined("Guest", "usr_dupe_guest").At(at.AddSeconds(3)));

        await Assert.That(result.Accepted).IsEqualTo(5);
        await using var db = app.CreateDbContext();
        await Assert.That(await db.InstanceVisits.AnyAsync(v => v.UserId == app.AccountId && v.InstanceLocation == Here)).IsTrue();
        await Assert.That(await db.InstanceVisits.AnyAsync(v => v.UserId == "usr_dupe_guest")).IsTrue();
    }

    [Test]
    public async Task A_udon_exception_read_late_does_not_move_the_resume_point()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var at = DateTimeOffset.UtcNow.AddHours(-2);
        var late = DateTimeOffset.UtcNow.AddHours(1);
        await using (var pc = await app.ConnectDesktopAsync("pc-udon"))
            await pc.SendLogAsync(GameLog.Location(Here, "Stop").At(at), GameLog.UdonException("continued").At(late));

        await using var again = await app.ConnectDesktopAsync("pc-udon");

        // Older desktop apps stamp a continuation line with the moment it was read, here an hour ahead.
        await Assert.That(again.Resume!.LastEntryAt.ToDateTimeOffset()).IsLessThan(late);
    }

    [Test]
    public async Task A_resent_log_does_not_store_the_same_stay_again()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-resend");
        var at = DateTimeOffset.UtcNow.AddMinutes(-30);
        var lines = new[] { GameLog.UserAuthenticated(app.AccountId!, "Me").At(at), GameLog.Location(Here, "Stop").At(at.AddSeconds(1)), GameLog.Quit().At(at.AddMinutes(10)) };
        await pc.SendLogAsync(lines);

        await pc.SendLogAsync(lines);

        await using var db = app.CreateDbContext();
        var stays = await db.InstanceVisits.Where(v => v.UserId == app.AccountId && v.InstanceLocation == Here).ToListAsync();
        await Assert.That(stays.Count).IsEqualTo(1);
        await Assert.That(stays[0].LeftAt).IsNotNull();
    }
}
