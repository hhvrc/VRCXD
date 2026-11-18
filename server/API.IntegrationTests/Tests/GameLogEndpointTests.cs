using System.Net;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The game log endpoints answer through Common/Features/GameLog since they left the accounts
// controller. The routes, statuses and problem types are the contract and did not change with the
// move; these pin the parts no other test asks for.
public sealed class GameLogEndpointTests
{
    [Test]
    public async Task Reads_of_an_unknown_account_answer_account_not_found()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        Task<HttpResponseMessage>[] requests =
        [
            app.Api.GetAsync("/1/accounts/usr_nobody/game-log"),
            app.Api.GetAsync("/1/accounts/usr_nobody/game-log/sessions"),
            app.Api.GetAsync("/1/accounts/usr_nobody/player-list"),
            app.Api.DeleteAsync("/1/accounts/usr_nobody/game-log/Location:1"),
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
    public async Task An_account_with_no_game_log_reads_empty_lists_and_hiding_a_row_twice_is_fine()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        await Assert.That((await app.GetJsonAsync(app.AccountPath("game-log"))).GetArrayLength()).IsEqualTo(0);
        var sessions = await app.GetJsonAsync(app.AccountPath("game-log/sessions"));
        await Assert.That(sessions.GetProperty("locations").GetArrayLength()).IsEqualTo(0);
        await Assert.That(sessions.GetProperty("hasMore").GetBoolean()).IsFalse();
        await Assert.That((await app.GetJsonAsync(app.AccountPath("player-list"))).GetProperty("players").GetArrayLength()).IsEqualTo(0);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("previous-instances"))).GetArrayLength()).IsEqualTo(0);
        var players = await app.GetJsonAsync(app.AccountPath("previous-instances/players?location=wrld_none:1"));
        await Assert.That(players.GetProperty("players").GetArrayLength()).IsEqualTo(0);

        for (var i = 0; i < 2; i++)
            await Assert.That((await app.Api.DeleteAsync(app.AccountPath("game-log/Location:1"))).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }
}
