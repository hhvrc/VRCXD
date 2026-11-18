using System.Net;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The favorites endpoints answer through Common/Features/Favorites since they left the accounts
// controller. The routes, statuses and problem types are the contract and did not change with the
// move; these pin the parts no other test asks for.
public sealed class FavoriteEndpointTests
{
    private static async Task<(HttpStatusCode Status, string Type)> ProblemAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (response.StatusCode, body.GetProperty("type").GetString()!);
    }

    [Test]
    public async Task The_list_of_an_unknown_account_answers_account_not_found()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        await Assert.That(await ProblemAsync(app.Api.GetAsync("/1/accounts/usr_nobody/favorites?type=friend"))).IsEqualTo((HttpStatusCode.NotFound, "Account.NotFound"));
    }

    [Test]
    public async Task What_goes_to_vrchat_answers_no_live_session_without_one()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        Task<HttpResponseMessage>[] requests =
        [
            app.Api.PostAsync("/1/accounts/usr_nobody/favorites/refresh", null),
            app.Api.DeleteAsync("/1/accounts/usr_nobody/favorites/usr_x"),
            app.Api.GetAsync("/1/accounts/usr_nobody/favorites/picker?type=friend"),
            app.Api.GetAsync("/1/accounts/usr_nobody/favorites/world-occupants"),
            app.Api.PostAsync("/1/accounts/usr_nobody/favorites/local/refresh?type=world&targetId=wrld_x", null),
            app.Api.DeleteAsync("/1/accounts/usr_nobody/favorites/groups/friend/group_0"),
        ];
        foreach (var request in requests)
            await Assert.That(await ProblemAsync(request)).IsEqualTo((HttpStatusCode.Conflict, "Account.NoLiveSession"));
    }

    [Test]
    public async Task An_account_with_no_favorites_reads_empty()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        await Assert.That((await app.GetJsonAsync(app.AccountPath("favorites/status?targetId=usr_x"))).GetBoolean()).IsFalse();
        await Assert.That((await app.GetJsonAsync(app.AccountPath("favorites/local?type=friend"))).GetArrayLength()).IsEqualTo(0);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("favorites/local/groups?type=friend"))).GetArrayLength()).IsEqualTo(0);
        var favorites = await app.GetJsonAsync(app.AccountPath("favorites?type=friend"));
        await Assert.That(favorites.GetProperty("favorites").GetArrayLength()).IsEqualTo(0);
    }
}
