using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The notification endpoints answer through Common/Features/Notifications since they left the
// accounts controller. The routes, statuses and problem types are the contract and did not change
// with the move; these pin the problems no other test asks for.
public sealed class NotificationEndpointTests
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

        await Assert.That(await ProblemAsync(app.Api.GetAsync("/1/accounts/usr_nobody/notifications"))).IsEqualTo((HttpStatusCode.NotFound, "Account.NotFound"));
    }

    [Test]
    public async Task A_notification_that_is_not_stored_answers_notification_not_found()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        Task<HttpResponseMessage>[] requests =
        [
            app.Api.PostAsync(app.AccountPath("notifications/not_a_notification/see"), null),
            app.Api.PostAsync(app.AccountPath("notifications/not_a_notification/hide"), null),
            app.Api.DeleteAsync(app.AccountPath("notifications/not_a_notification")),
        ];
        foreach (var request in requests)
            await Assert.That(await ProblemAsync(request)).IsEqualTo((HttpStatusCode.NotFound, "Notification.NotFound"));
    }

    [Test]
    public async Task Actions_without_a_live_session_answer_no_live_session()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        Task<HttpResponseMessage>[] requests =
        [
            app.Api.PostAsync("/1/accounts/usr_nobody/notifications/refresh", null),
            app.Api.PostAsync("/1/accounts/usr_nobody/notifications/see-all", null),
            app.Api.PostAsync("/1/accounts/usr_nobody/notifications/not_1/accept", null),
            app.Api.PostAsJsonAsync("/1/accounts/usr_nobody/notifications/not_1/respond", new { responseType = "accept", responseData = "" }),
            app.Api.PostAsJsonAsync("/1/accounts/usr_nobody/notifications/not_1/invite-response", new { responseSlot = 1 }),
        ];
        foreach (var request in requests)
            await Assert.That(await ProblemAsync(request)).IsEqualTo((HttpStatusCode.Conflict, "Account.NoLiveSession"));
    }
}
