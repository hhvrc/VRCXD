using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services.Automation;
using VRCX.Testing;
using VRCX.Testing.Desktop;
using VrchatNotification = VRCX.Vrchat.Models.Notification;

namespace VRCX.API.IntegrationTests.Tests;

// The original's auto-decline: with the setting on, a friend request from someone never seen in an
// instance (no game log join by id or name) is hidden; someone met in game is left alone.
public sealed class AutoDeclineTests
{
    [Test]
    public async Task Friend_requests_from_strangers_are_declined_and_from_people_met_are_kept()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-decline");
        await pc.SendLogAsync(
            GameLog.UserAuthenticated(app.AccountId!, "Me"),
            GameLog.Location("wrld_decline:1~region(eu)", "Decline"),
            GameLog.PlayerJoined("Met Before", "usr_met"));
        app.Vrchat
            .Json("auth/user/notifications/not_stranger/hide", """{"id":"not_stranger"}""")
            .Json("auth/user/notifications/not_met/hide", """{"id":"not_met"}""");
        var automation = app.Services.GetRequiredService<AccountAutomation>();
        var stranger = new VrchatNotification { Id = "not_stranger", Type = "friendRequest", SenderUserId = "usr_stranger", SenderUsername = "Stranger" };
        var met = new VrchatNotification { Id = "not_met", Type = "friendRequest", SenderUserId = "usr_met", SenderUsername = "Met Before" };
        var metByName = new VrchatNotification { Id = "not_met", Type = "friendRequest", SenderUserId = "usr_other_id", SenderUsername = "Met Before" };

        // Someone met only by another account on this server is still a stranger to this one.
        await using (var db = app.CreateDbContext())
        {
            if (!db.Users.Any(u => u.Id == "usr_stranger")) db.Users.Add(new VRCX.Common.VrcxDb.User { Id = "usr_stranger", DisplayName = "Stranger" });
            db.Worlds.Add(new VRCX.Common.VrcxDb.World { Id = "wrld_elsewhere", Name = "Elsewhere" });
            db.Instances.Add(new VRCX.Common.VrcxDb.Instance { Location = "wrld_elsewhere:9", WorldId = "wrld_elsewhere", InstanceId = "9", InstanceName = "9", Type = VRCX.Common.VrcxDb.InstanceAccessType.Public });
            db.InstanceVisits.Add(new VRCX.Common.VrcxDb.InstanceVisit { UserId = "usr_stranger", InstanceLocation = "wrld_elsewhere:9", JoinedAt = DateTimeOffset.UtcNow, Source = VRCX.Common.VrcxDb.VisitSource.GameLog });
            await db.SaveChangesAsync();
        }

        // Off by default.
        await Assert.That(await automation.DeclineFriendRequestAsync(app.AccountId!, stranger, CancellationToken.None)).IsFalse();

        await app.Api.PutAsJsonAsync($"/1/accounts/{app.AccountId}/settings", new { autoDeclineFriendRequests = true });
        await Assert.That(await automation.DeclineFriendRequestAsync(app.AccountId!, met, CancellationToken.None)).IsFalse();
        await Assert.That(await automation.DeclineFriendRequestAsync(app.AccountId!, metByName, CancellationToken.None)).IsFalse();
        app.Vrchat.AssertNotCalled("PUT", "auth/user/notifications/not_met/hide");

        await Assert.That(await automation.DeclineFriendRequestAsync(app.AccountId!, stranger, CancellationToken.None)).IsTrue();
        app.Vrchat.AssertCalled("PUT", "auth/user/notifications/not_stranger/hide");
    }
}
