using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// The original's status bar game segment: the running session's start, and the last session's length
// and end (VRCX_lastGameSessionMs, VRCX_lastGameOfflineAt), kept per PC.
public sealed class GameSessionTimesTests
{
    [Test]
    public async Task A_started_and_stopped_game_leaves_its_last_session()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-session");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await pc.GameRunningAsync(true);
        var running = await StatusAsync(app, s => s.TryGetProperty("gameStartedAt", out _));
        var started = running.GetProperty("gameStartedAt").GetDateTimeOffset();
        await Assert.That(started).IsGreaterThan(before);

        await pc.GameRunningAsync(false);
        var stopped = await StatusAsync(app, s => s.TryGetProperty("lastGameStoppedAt", out _));
        await Assert.That(stopped.TryGetProperty("gameStartedAt", out _)).IsFalse();
        await Assert.That(stopped.GetProperty("lastGameStartedAt").GetDateTimeOffset()).IsEqualTo(started);
        await Assert.That(stopped.GetProperty("lastGameStoppedAt").GetDateTimeOffset()).IsGreaterThanOrEqualTo(started);
    }

    private static async Task<JsonElement> StatusAsync(VrcxScenario app, Func<JsonElement, bool> ready)
    {
        for (var i = 0; ; i++)
        {
            var status = await app.Api.GetFromJsonAsync<JsonElement>($"/1/accounts/{app.AccountId}/desktop");
            if (ready(status) || i == 50) return status;
            await Task.Delay(100);
        }
    }
}
