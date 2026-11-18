using System.Net;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The friends endpoints answer through Common/Features/Friends (FriendQueries and FriendActions)
// since they left the accounts controller. These pin the problems they map: the routes, statuses
// and problem types are the contract and did not change with the move.
public sealed class FriendsEndpointTests
{
    private static async Task<(HttpStatusCode Status, string Type)> ProblemAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (response.StatusCode, body.GetProperty("type").GetString()!);
    }

    [Test]
    public async Task Reads_of_an_unknown_account_answer_account_not_found()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        foreach (var path in new[] { "friends", "friends/details", "friend-log" })
            await Assert.That(await ProblemAsync(app.Api.GetAsync($"/1/accounts/usr_nobody/{path}"))).IsEqualTo((HttpStatusCode.NotFound, "Account.NotFound"));
        await Assert.That(await ProblemAsync(app.Api.DeleteAsync("/1/accounts/usr_nobody/friend-log/Friend:1"))).IsEqualTo((HttpStatusCode.NotFound, "Account.NotFound"));
    }

    [Test]
    public async Task Actions_without_a_live_session_answer_no_live_session_and_do_not_call_vrchat()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        Task<HttpResponseMessage>[] actions =
        [
            app.Api.PostAsync("/1/accounts/usr_nobody/friend-requests/usr_x", null),
            app.Api.DeleteAsync("/1/accounts/usr_nobody/friend-requests/usr_x"),
            app.Api.DeleteAsync("/1/accounts/usr_nobody/friends/usr_x"),
            app.Api.PostAsync("/1/accounts/usr_nobody/friends/usr_x/load", null),
            app.Api.PostAsync("/1/accounts/usr_nobody/friends/refresh", null),
        ];
        foreach (var action in actions)
            await Assert.That(await ProblemAsync(action)).IsEqualTo((HttpStatusCode.Conflict, "Account.NoLiveSession"));
        app.Vrchat.AssertNotCalled("POST", "user/usr_x/friendRequest");
        app.Vrchat.AssertNotCalled("DELETE", "user/usr_x/friendRequest");
        app.Vrchat.AssertNotCalled("DELETE", "auth/user/friends/usr_x");
        app.Vrchat.AssertNotCalled("GET", "users/usr_x");
    }

    [Test]
    public async Task A_refusal_from_vrchat_answers_an_upstream_problem_and_logs_no_friend_request()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Fail("POST", "user/usr_x/friendRequest", HttpStatusCode.BadRequest, "nope");

        await Assert.That(await ProblemAsync(app.Api.PostAsync(app.AccountPath("friend-requests/usr_x"), null))).IsEqualTo((HttpStatusCode.BadGateway, "Vrchat.Upstream"));

        await Assert.That((await app.GetJsonAsync(app.AccountPath("friend-log"))).GetArrayLength()).IsEqualTo(0);
    }
}
