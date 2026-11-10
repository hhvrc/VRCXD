using System.Text.Json.Nodes;
using VRCX.Common.Live;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's VRChat Credits row on the own profile: getVRChatCredits, kept current by the
// pipeline's economy-update (setCurrentUserCredits).
public sealed class CreditsTests
{
    [Test]
    public async Task The_balance_comes_from_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Json("GET", $"user/{app.AccountId}/economy/balance", """{"balance":1234}""");

        var credits = await app.GetJsonAsync(app.AccountPath("economy/balance"));
        await Assert.That(credits.GetProperty("balance").GetInt32()).IsEqualTo(1234);
    }

    [Test]
    public async Task Economy_update_reaches_the_live_socket()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();

        await app.Pipeline.SendAsync("economy-update", new JsonObject { ["walletBalance"] = 567 });

        var changed = await live.WaitForAsync<CreditsChanged>(c => c.AccountId == app.AccountId);
        await Assert.That(changed.Balance).IsEqualTo(567);
    }
}

// content-refresh tells an open gallery which tab to reload.
public sealed class ContentRefreshTests
{
    [Test]
    public async Task Content_refresh_reaches_the_live_socket()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();

        await app.Pipeline.SendAsync("content-refresh", new JsonObject { ["contentType"] = "emoji", ["actionType"] = "created" });

        var refreshed = await live.WaitForAsync<AccountContentRefreshed>(c => c.AccountId == app.AccountId);
        await Assert.That(refreshed.ContentType).IsEqualTo("emoji");
        await Assert.That(refreshed.ActionType).IsEqualTo("created");
    }
}
