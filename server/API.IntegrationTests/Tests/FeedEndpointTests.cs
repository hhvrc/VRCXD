using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Common.Features.Feed;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The feed endpoints answer through Common/Features/Feed since they left the accounts controller.
// The routes, statuses and problem types are the contract and did not change with the move; these
// pin the parts no other test asks for.
public sealed class FeedEndpointTests
{
    [Test]
    public async Task An_unknown_account_answers_account_not_found()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        Task<HttpResponseMessage>[] requests =
        [
            app.Api.GetAsync("/1/accounts/usr_nobody/feed"),
            app.Api.PostAsJsonAsync("/1/accounts/usr_nobody/database/purge-avatar-feed", new { all = true }),
        ];
        foreach (var request in requests)
        {
            var response = await request;
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("Account.NotFound");
        }
    }

    [Test]
    public async Task The_type_filter_narrows_the_feed_and_ignores_names_that_are_not_types()
    {
        await using var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_feed_alice", "Alice", u => u.Bio("old bio"));
        await app.LoginAsync();
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsync(app.AccountPath($"friends/{alice.Id}/load"), null), "first load");
        alice.Bio("new bio");
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsync(app.AccountPath($"friends/{alice.Id}/load"), null), "second load");

        bool HasBio(JsonElement rows) => rows.EnumerateArray().Any(r => r.GetProperty("type").GetString() == "Bio" && r.GetProperty("userId").GetString() == alice.Id);
        await Assert.That(HasBio(await app.GetJsonAsync(app.AccountPath("feed")))).IsTrue();
        var bio = await app.GetJsonAsync(app.AccountPath("feed?types=Bio,NotAType"));
        await Assert.That(bio.EnumerateArray().Select(r => r.GetProperty("type").GetString()!).Distinct().ToArray()).IsEquivalentTo(new[] { "Bio" });
        await Assert.That(HasBio(await app.GetJsonAsync(app.AccountPath("feed?types=Status")))).IsFalse();
    }

    [Test]
    public async Task Feed_types_are_parsed_as_the_endpoint_takes_them()
    {
        await Assert.That(FeedService.ParseTypes(null)).IsEmpty();
        await Assert.That(FeedService.ParseTypes("GPS, Online,,nope,gps")).IsEquivalentTo(new[] { "GPS", "Online" });
    }
}
