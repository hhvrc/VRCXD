using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common;
using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's Settings, Advanced, Database panel: one call counts the history tables.
public sealed class TableSizesTests
{
    [Test]
    public async Task The_database_panel_counts_history_rows_by_kind()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var before = await app.GetJsonAsync("/1/database/table-sizes");

        await using (var db = await app.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync())
        {
            var userId = await db.Accounts.Select(a => a.UserId).FirstAsync();
            var now = DateTimeOffset.UtcNow;
            db.PresenceEvents.Add(new PresenceEvent { UserId = userId, At = now, Kind = PresenceKind.Location, Location = "wrld_1:1" });
            db.PresenceEvents.Add(new PresenceEvent { UserId = userId, At = now, Kind = PresenceKind.Online });
            db.PresenceEvents.Add(new PresenceEvent { UserId = userId, At = now, Kind = PresenceKind.Offline });
            db.GameLogEvents.Add(new GameLogEvent { At = now, Kind = "Event", Data = "Kicked" });
            db.GameLogEvents.Add(new GameLogEvent { At = now, Kind = "External", Data = "Hello" });
            await db.SaveChangesAsync();
        }

        var after = await app.GetJsonAsync("/1/database/table-sizes");
        long Delta(string name) => after.GetProperty(name).GetInt64() - before.GetProperty(name).GetInt64();
        await Assert.That(Delta("gps")).IsEqualTo(1);
        await Assert.That(Delta("onlineOffline")).IsEqualTo(2);
        await Assert.That(Delta("event")).IsEqualTo(1);
        await Assert.That(Delta("external")).IsEqualTo(1);
        await Assert.That(Delta("videoPlay")).IsEqualTo(0);
    }
}
