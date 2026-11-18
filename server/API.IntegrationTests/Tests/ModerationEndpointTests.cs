using System.Net;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The moderation endpoints answer through Common/Features/Moderation since they left the accounts
// controller. The routes, statuses and problem types are the contract and did not change with the
// move; these pin the problems no other test asks for.
public sealed class ModerationEndpointTests
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

        await Assert.That(await ProblemAsync(app.Api.GetAsync("/1/accounts/usr_nobody/moderations"))).IsEqualTo((HttpStatusCode.NotFound, "Account.NotFound"));
        await Assert.That((await app.GetJsonAsync(app.AccountPath("moderations"))).GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task Actions_without_a_live_session_answer_no_live_session_and_do_not_call_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        Task<HttpResponseMessage>[] requests =
        [
            app.Api.PostAsync("/1/accounts/usr_nobody/moderations/usr_x/block", null),
            app.Api.DeleteAsync("/1/accounts/usr_nobody/moderations/usr_x/block"),
            app.Api.DeleteAsync("/1/accounts/usr_nobody/moderations/mute"),
            app.Api.PostAsync("/1/accounts/usr_nobody/moderations/refresh", null),
        ];
        foreach (var request in requests)
            await Assert.That(await ProblemAsync(request)).IsEqualTo((HttpStatusCode.Conflict, "Account.NoLiveSession"));
        app.Vrchat.AssertNotCalled("POST", "auth/user/playermoderations");
    }
}
