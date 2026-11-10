using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's Login Settings "custom development endpoint": the server sends the account's
// session to that API, so only https/wss URLs on VRChat's domains (or configured hosts) pass.
public sealed class CustomEndpointTests
{
    [Test]
    public async Task A_custom_endpoint_on_a_vrchat_host_is_used_and_remembered()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe();

        var response = await app.Api.PostAsJsonAsync("/1/accounts/login",
            new { username = "me", password = "test-password", endpoint = "https://dev-api.vrchat.cloud/api/1", websocket = "wss://dev-pipeline.vrchat.cloud" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var used = app.Vrchat.CreatedFor.Last(e => e is not null)!;
        await Assert.That(used.Api.ToString()).IsEqualTo("https://dev-api.vrchat.cloud/api/1/");
        await using var db = app.CreateDbContext();
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == me.Id);
        await Assert.That(account.ApiEndpoint).IsEqualTo("https://dev-api.vrchat.cloud/api/1/");
        await Assert.That(account.PipelineEndpoint).IsEqualTo("wss://dev-pipeline.vrchat.cloud/");
    }

    [Test]
    [Arguments("https://10.0.0.1/api/1", "")]
    [Arguments("http://api.vrchat.cloud/api/1", "")]
    [Arguments("", "ws://pipeline.vrchat.cloud")]
    [Arguments("https://vrchat.cloud.evil.example/api/1", "")]
    public async Task Other_hosts_and_plain_schemes_are_refused(string endpoint, string websocket)
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe();

        var response = await app.Api.PostAsJsonAsync("/1/accounts/login", new { username = "me", password = "test-password", endpoint, websocket });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(app.Vrchat.CreatedFor.Any(e => e is not null)).IsFalse();
    }

    [Test]
    public async Task No_endpoint_logs_in_against_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync(sync: false);
        await using var db = app.CreateDbContext();
        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.UserId == app.AccountId);
        await Assert.That(account.ApiEndpoint).IsNull();
    }
}
