using Microsoft.EntityFrameworkCore;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;
using VRCX.Common.Websocket;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// The desktop app sends a batch again when the acknowledgement was lost, and a batch the server could
// not store was sent again forever. Neither may change what is stored beyond the first copy.
public sealed class GameLogResendTests
{
    private const string Here = "wrld_resend:1~region(eu)";

    private static LogEntry[] Batch(string accountId, DateTimeOffset at) =>
    [
        GameLog.UserAuthenticated(accountId, "Me").At(at),
        GameLog.Location(Here, "Resend").At(at.AddSeconds(1)),
        GameLog.PortalSpawn().At(at.AddSeconds(2)),
        GameLog.PortalSpawn().At(at.AddSeconds(2)),
        GameLog.Event("Something happened").At(at.AddSeconds(3)),
        GameLog.StickerSpawn("usr_resend_sticker", "Sticky", "inv_00000000-0000-0000-0000-000000000001").At(at.AddSeconds(4)),
    ];

    private static async Task<(int Portals, int Events, int Stickers)> CountsAsync(VrcxScenario app)
    {
        await using var db = app.CreateDbContext();
        return (await db.PortalSpawns.CountAsync(p => p.InstanceLocation == Here),
            await db.GameLogEvents.CountAsync(e => e.InstanceLocation == Here),
            await db.StickerSpawns.CountAsync(s => s.InstanceLocation == Here));
    }

    [Test]
    public async Task A_resent_batch_stores_no_event_or_spawn_twice()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-resend");
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);

        await pc.SendLogAsync(Batch(app.AccountId!, at));
        await pc.SendLogAsync(Batch(app.AccountId!, at));

        // Two portals in one second are two portals, once.
        await Assert.That(await CountsAsync(app)).IsEqualTo((2, 1, 1));
    }

    [Test]
    public async Task A_resent_batch_with_a_video_stores_no_event_or_spawn_twice()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-resend-video");
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);
        LogEntry[] batch = [.. Batch(app.AccountId!, at), GameLog.VideoPlay("https://example.test/resend").At(at.AddSeconds(5))];

        await pc.SendLogAsync(batch);
        // The video's unique key fails the whole save, so this copy is stored line by line.
        await pc.SendLogAsync(batch);

        await Assert.That(await CountsAsync(app)).IsEqualTo((2, 1, 1));
    }

    [Test]
    public async Task A_line_that_cannot_be_stored_is_skipped_and_the_batch_acknowledged()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-poison");
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);
        // A line the database refuses, whatever the reason.
        await using (var setup = app.CreateDbContext())
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION refuse_poison() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.data = 'poison' THEN RAISE EXCEPTION 'refused' USING ERRCODE = 'check_violation'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER refuse_poison BEFORE INSERT ON game_log_events FOR EACH ROW EXECUTE FUNCTION refuse_poison();
                """);

        var result = await pc.SendLogAsync(
            GameLog.UserAuthenticated(app.AccountId!, "Me").At(at),
            GameLog.Location(Here, "Resend").At(at.AddSeconds(1)),
            GameLog.Event("poison").At(at.AddSeconds(2)),
            GameLog.PlayerJoined("Guest", "usr_poison_guest").At(at.AddSeconds(3)));

        await Assert.That(result.Accepted).IsEqualTo(3);
        await using var db = app.CreateDbContext();
        await Assert.That(await db.InstanceVisits.AnyAsync(v => v.UserId == "usr_poison_guest" && v.InstanceLocation == Here)).IsTrue();
        await Assert.That((await db.GameLogClients.SingleAsync(c => c.ClientId == "pc-poison")).LastEntryAt!.Value).IsGreaterThanOrEqualTo(at.AddSeconds(3));
    }

    [Test]
    public async Task A_batch_hitting_a_brief_database_outage_is_not_acknowledged_and_is_stored_when_sent_again()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);
        // The database answering "out of resources" for one row, as it might while restarting.
        await using (var setup = app.CreateDbContext())
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION refuse_briefly() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.data = 'during outage' THEN RAISE EXCEPTION 'busy' USING ERRCODE = 'insufficient_resources'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER refuse_briefly BEFORE INSERT ON game_log_events FOR EACH ROW EXECUTE FUNCTION refuse_briefly();
                """);
        LogEntry[] batch =
        [
            GameLog.UserAuthenticated(app.AccountId!, "Me").At(at),
            GameLog.Location(Here, "Resend").At(at.AddSeconds(1)),
            GameLog.Event("during outage").At(at.AddSeconds(2)),
            GameLog.PlayerJoined("Guest", "usr_outage_guest").At(at.AddSeconds(3)),
        ];

        await using (var pc = await app.ConnectDesktopAsync("pc-outage"))
        {
            var acked = true;
            try { await pc.SendLogAsync(batch); }
            catch (Exception) { acked = false; }
            await Assert.That(acked).IsFalse();
        }
        await using (var db = app.CreateDbContext())
        {
            await Assert.That(await db.InstanceVisits.AnyAsync(v => v.UserId == "usr_outage_guest")).IsFalse();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_briefly ON game_log_events");
        }

        // The database is back; the app reconnects and sends the same batch.
        await using (var again = await app.ConnectDesktopAsync("pc-outage"))
        {
            var result = await again.SendLogAsync(batch);
            await Assert.That(result.Accepted).IsEqualTo(4);
        }
        await using (var db = app.CreateDbContext())
        {
            await Assert.That(await db.GameLogEvents.CountAsync(e => e.Data == "during outage")).IsEqualTo(1);
            await Assert.That(await db.InstanceVisits.AnyAsync(v => v.UserId == "usr_outage_guest" && v.InstanceLocation == Here)).IsTrue();
        }
    }

    [Test]
    public async Task VRChat_stopping_while_a_backlog_is_read_keeps_the_instance_for_the_rest_of_it()
    {
        LiveSocketHandler.QuietCloseDelay = TimeSpan.FromSeconds(1);
        await using var app = await VrcxScenario.StartAsync();
        // Connected before signing in, so the test desktop sends no sign-in line stamped now.
        await using var pc = await app.ConnectDesktopAsync("pc-backlog");
        await app.LoginAsync();
        await pc.GameRunningAsync(true);
        var at = DateTimeOffset.UtcNow.AddHours(-4);
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me").At(at), GameLog.Location(Here, "Resend").At(at.AddSeconds(1)));

        var stopped = DateTimeOffset.UtcNow;
        await pc.GameRunningAsync(false);
        await pc.SendLogAsync(GameLog.PlayerJoined("Later", "usr_backlog_later").At(at.AddHours(1)));

        await using (var db = app.CreateDbContext())
            await Assert.That(await db.InstanceVisits.AnyAsync(v => v.UserId == "usr_backlog_later" && v.InstanceLocation == Here)).IsTrue();
        for (var i = 0; i < 50; i++)
        {
            await using var db = app.CreateDbContext();
            if (!await db.InstanceVisits.AnyAsync(v => v.InstanceLocation == Here && v.LeftAt == null)) break;
            await Task.Delay(100);
        }
        // Once the backlog is read, the stay ends when VRChat stopped.
        await using (var db = app.CreateDbContext())
        {
            var mine = await db.InstanceVisits.SingleAsync(v => v.UserId == app.AccountId && v.InstanceLocation == Here);
            await Assert.That(mine.LeftAt).IsNotNull();
            await Assert.That(mine.LeftAt!.Value).IsGreaterThanOrEqualTo(stopped.AddSeconds(-1)).And.IsLessThanOrEqualTo(stopped.AddSeconds(5));
        }
    }
}
