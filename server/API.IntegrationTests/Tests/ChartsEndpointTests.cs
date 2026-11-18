using System.Net;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The charts and Activity tab endpoints answer through Common/Features/Charts and
// Common/Features/UserActivity since they left the accounts controller. The routes, statuses and
// problem types are the contract and did not change with the move; these pin the parts no other
// test asks for.
public sealed class ChartsEndpointTests
{
    private static async Task<(HttpStatusCode Status, string Type)> ProblemAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (response.StatusCode, body.GetProperty("type").GetString()!);
    }

    [Test]
    public async Task An_unknown_account_answers_account_not_found()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        Task<HttpResponseMessage>[] requests =
        [
            app.Api.GetAsync("/1/accounts/usr_nobody/activity/dates"),
            app.Api.GetAsync("/1/accounts/usr_nobody/activity?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z"),
            app.Api.GetAsync("/1/accounts/usr_nobody/hot-worlds"),
            app.Api.GetAsync("/1/accounts/usr_nobody/hot-worlds/wrld_x/friends"),
            app.Api.GetAsync("/1/accounts/usr_nobody/mutuals"),
            app.Api.PostAsync("/1/accounts/usr_nobody/mutuals/fetch", null),
            app.Api.PostAsync("/1/accounts/usr_nobody/mutuals/usr_x/refresh", null),
        ];
        foreach (var request in requests)
            await Assert.That(await ProblemAsync(request)).IsEqualTo((HttpStatusCode.NotFound, "Account.NotFound"));
    }

    [Test]
    public async Task An_account_with_no_history_reads_empty_charts_and_activity()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        await Assert.That((await app.GetJsonAsync(app.AccountPath("activity/dates"))).GetArrayLength()).IsEqualTo(0);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("hot-worlds"))).GetArrayLength()).IsEqualTo(0);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("hot-worlds/wrld_x/friends"))).GetArrayLength()).IsEqualTo(0);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("mutuals/fetch"))).GetProperty("running").GetBoolean()).IsFalse();
        await Assert.That((await app.Api.DeleteAsync(app.AccountPath("mutuals/fetch"))).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await Assert.That((await app.GetJsonAsync(app.AccountPath("top-worlds"))).GetArrayLength()).IsEqualTo(0);
        var activity = await app.GetJsonAsync(app.AccountPath("users/usr_someone/activity?days=30"));
        await Assert.That(activity.GetProperty("events").GetArrayLength()).IsEqualTo(0);
        await Assert.That(activity.GetProperty("selfVisits").GetArrayLength()).IsEqualTo(0);
    }
}
