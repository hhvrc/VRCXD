using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Live;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// Videos as the original's game log stores them: named by a world's own "[VRCX]" line, not repeated
// when a world logs the same URL again, and skipped in worlds that name their videos themselves.
public sealed class VideoPlayTests
{
    private const string Here = "wrld_video:1~region(eu)";
    private const string DanceWorld = "wrld_f20326da-f1ac-45fc-a062-609723b097b1:1~region(eu)";

    [Test]
    public async Task Dance_and_cinema_lines_store_a_named_video_once()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-video");
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);

        await pc.SendLogAsync(
            GameLog.Location(Here, "Cinema").At(at),
            GameLog.Vrcx("""LSMedia 0,6298.292,Someone,The Outfit (2022), 1080p""").At(at.AddSeconds(1)),
            GameLog.Vrcx("""LSMedia 30,6298.292,Someone,The Outfit (2022), 1080p""").At(at.AddSeconds(31)),
            GameLog.VideoPlay("https://example.test/a.mp4").At(at.AddSeconds(40)),
            GameLog.VideoPlay("https://example.test/a.mp4").At(at.AddSeconds(41)));

        await using var db = app.CreateDbContext();
        var plays = await db.VideoPlays.Where(v => v.InstanceLocation == Here).OrderBy(v => v.At).ToListAsync();
        await Assert.That(plays.Select(p => (p.VideoUrl, p.VideoId, p.VideoName))).IsEquivalentTo(new[]
        {
            ("The Outfit (2022)", (string?)"LSMedia", (string?)"The Outfit (2022)"),
            ("https://example.test/a.mp4", (string?)null, (string?)null),
        });
        // The position when logged and the length, for Now Playing's progress: a plain URL starts at
        // the original's 8 second loading delay with no known length.
        await Assert.That(plays.Select(p => (p.VideoPos, p.VideoLength))).IsEquivalentTo(new[] { ((double?)0, (double?)6298.292), ((double?)8, (double?)null) });
        var rows = await app.GetJsonAsync($"/1/accounts/{app.AccountId}/game-log?types=VideoPlay&limit=5");
        var lsMedia = rows.EnumerateArray().First(r => r.TryGetProperty("videoId", out var id) && id.GetString() == "LSMedia");
        await Assert.That(lsMedia.GetProperty("videoLength").GetDouble()).IsEqualTo(6298.292);
    }

    [Test]
    public async Task A_plain_video_line_in_a_world_with_its_own_integration_is_skipped()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-dance");
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);

        await pc.SendLogAsync(
            GameLog.Location(DanceWorld, "Dance").At(at),
            GameLog.VideoPlay("https://example.test/raw.mp4").At(at.AddSeconds(1)),
            GameLog.Vrcx("""VideoPlay(VRDancing) "https://example.test/d.mp4",0,200,7,"Dancer","Song" """).At(at.AddSeconds(2)));

        await using var db = app.CreateDbContext();
        var plays = await db.VideoPlays.Where(v => v.InstanceLocation == DanceWorld).ToListAsync();
        await Assert.That(plays.Select(p => (p.VideoUrl, p.VideoName))).IsEquivalentTo(new[] { ("https://example.test/d.mp4", (string?)"Song") });
    }

    [Test]
    public async Task The_same_video_at_a_new_position_moves_now_playing()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-seek");
        var at = DateTimeOffset.UtcNow.AddMinutes(-1);

        await pc.SendLogAsync(
            GameLog.UserAuthenticated(app.AccountId!, "Me"),
            GameLog.Location(Here, "Cinema").At(at),
            GameLog.Vrcx("""LSMedia 0,6298.292,Someone,The Outfit (2022), 1080p""").At(at.AddSeconds(1)),
            GameLog.Vrcx("""LSMedia 1200,6298.292,Someone,The Outfit (2022), 1080p""").At(at.AddSeconds(20)));

        var moved = await live.WaitForAsync<NowPlayingPosition>(p => p.AccountId == app.AccountId);
        await Assert.That(moved.VideoUrl).IsEqualTo("The Outfit (2022)");
        await Assert.That(moved.VideoPos).IsEqualTo(1200);
        await Assert.That(moved.VideoLength).IsEqualTo(6298.292);
        await Assert.That(moved.LoggedAt.ToDateTimeOffset()).IsEqualTo(at.AddSeconds(20));
        // Still one stored play: a seek is not a new video.
        await using var db = app.CreateDbContext();
        await Assert.That(await db.VideoPlays.CountAsync(v => v.InstanceLocation == Here)).IsEqualTo(1);
    }

    [Test]
    public async Task The_progress_pie_follows_dance_world_videos_on_the_pc()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-pie");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));
        var settings = await app.GetJsonAsync(app.AccountPath("settings"));
        var body = System.Text.Json.Nodes.JsonNode.Parse(settings.GetRawText())!.AsObject();
        body["openVr"] = true;
        body["progressPie"] = true;
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync(app.AccountPath("settings"), body), "save settings");
        await pc.NextAgentSettingsAsync();
        var at = DateTimeOffset.UtcNow.AddSeconds(-5);

        // A plain world: the clock goes for the wrist's now playing, but the dance-world-only filter keeps the pie off.
        await pc.SendLogAsync(
            GameLog.Location(Here, "Cinema").At(at),
            GameLog.Vrcx("""LSMedia 0,6298.292,Someone,The Outfit (2022), 1080p""").At(at.AddSeconds(1)));
        var plain = (await pc.NextActionAsync()).UpdateNowPlayingPie;
        await Assert.That((plain.Active, plain.Pie, plain.Name)).IsEqualTo((true, false, "The Outfit (2022)"));
        await pc.SendLogAsync(
            GameLog.Location(DanceWorld, "Dance").At(at.AddSeconds(2)),
            GameLog.Vrcx("""VideoPlay(VRDancing) "https://example.test/d.mp4",0,200,7,"Dancer","Song" """).At(at.AddSeconds(3)));
        var action = await pc.NextActionAsync();
        await Assert.That(action.ActionCase).IsEqualTo(ClientAction.ActionOneofCase.UpdateNowPlayingPie);
        var pie = action.UpdateNowPlayingPie;
        await Assert.That((pie.Active, pie.Pie, pie.Position, pie.Length, pie.Speed, pie.Name)).IsEqualTo((true, true, 0d, 200d, 1d, "Song"));
        await Assert.That(pie.LoggedAt).IsEqualTo(at.AddSeconds(3).ToUnixTimeMilliseconds() / 1000.0);

        // Leaving stops it.
        await pc.SendLogAsync(GameLog.Location(Here, "Cinema").At(at.AddSeconds(4)));
        action = await pc.NextActionAsync();
        await Assert.That((action.UpdateNowPlayingPie.Active, action.UpdateNowPlayingPie.Pie)).IsEqualTo((false, false));
    }
}
