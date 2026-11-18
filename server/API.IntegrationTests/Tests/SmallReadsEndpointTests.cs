using System.Net;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Quick search, the avatar history and the visits list answer through Common/Features
// (QuickSearch, Avatars, GameLog) since they left the accounts controller. The routes, statuses
// and problem types are the contract and did not change with the move; these pin the parts no
// other test asks for.
public sealed class SmallReadsEndpointTests
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
            app.Api.GetAsync("/1/accounts/usr_nobody/quick-search"),
            app.Api.GetAsync("/1/accounts/usr_nobody/avatar-history"),
            app.Api.DeleteAsync("/1/accounts/usr_nobody/avatar-history"),
            app.Api.GetAsync("/1/accounts/usr_nobody/visits"),
        ];
        foreach (var request in requests)
            await Assert.That(await ProblemAsync(request)).IsEqualTo((HttpStatusCode.NotFound, "Account.NotFound"));
        await Assert.That(await ProblemAsync(app.Api.GetAsync("/1/accounts/usr_nobody/avatars/avtr_x/exists"))).IsEqualTo((HttpStatusCode.Conflict, "Account.NoLiveSession"));
    }

    [Test]
    public async Task The_quick_search_index_lists_the_accounts_friends()
    {
        await using var app = await VrcxScenario.StartAsync();
        var bob = app.Vrchat.Friend("usr_qs_bob", "Bob");
        await app.LoginAsync();

        var index = await app.GetJsonAsync(app.AccountPath("quick-search"));

        await Assert.That(index.GetProperty("friends").EnumerateArray().Any(f => f.GetProperty("id").GetString() == bob.Id && f.GetProperty("name").GetString() == "Bob")).IsTrue();
        await Assert.That(index.GetProperty("groups").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task Clearing_the_avatar_history_empties_it_and_the_visits_list_reads()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        // What the account wore before now is history until it is cleared.
        await Assert.That((await app.Api.GetAsync(app.AccountPath("avatar-history"))).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await app.Api.DeleteAsync(app.AccountPath("avatar-history"))).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("avatar-history"))).GetArrayLength()).IsEqualTo(0);
        await Assert.That((await app.Api.GetAsync(app.AccountPath("visits"))).StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
