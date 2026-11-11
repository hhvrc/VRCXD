using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Live;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// The original's nowPlayingSpec (https://protv.dev/guides/nowplaying): ProTV producers say what plays,
// with their own clocks, and the followed one is stored and shown.
public sealed class NowPlayingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_named_media_is_stored_once_and_its_clock_follows_play_pause_and_seek()
    {
        var spec = new NowPlayingProducers();
        var change = spec.Apply("""{"id":"Main-1","type":"mediaChange","mediaUrl":"https://youtu.be/dQw4w9WgXcQ","mediaName":"Song","mediaOwnerName":"Ann","mediaLength":200}""", T0);
        await Assert.That(change.OfType<NowPlayingSet>().Single()).IsEqualTo(new NowPlayingSet("https://youtu.be/dQw4w9WgXcQ", "Main", "Song", 200, "Ann", false));
        var timing = change.OfType<NowPlayingTiming>().Single();
        await Assert.That((timing.Name, timing.ThumbnailUrl, timing.Position, timing.Paused)).IsEqualTo(("Song (Ann)", "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg", 0.0, false));

        var pause = spec.Apply("""{"id":"Main-1","type":"mediaPause"}""", T0.AddSeconds(10)).OfType<NowPlayingTiming>().Single();
        await Assert.That((pause.Position, pause.Paused)).IsEqualTo((10.0, true));
        var seek = spec.Apply("""{"id":"Main-1","type":"mediaSeek","mediaTime":50}""", T0.AddSeconds(30)).OfType<NowPlayingTiming>().Single();
        await Assert.That((seek.Position, seek.Paused)).IsEqualTo((50.0, true));
        var play = spec.Apply("""{"id":"Main-1","type":"mediaPlay"}""", T0.AddSeconds(40)).OfType<NowPlayingTiming>().Single();
        await Assert.That((play.Position, play.Paused)).IsEqualTo((50.0, false));
        // Speed changes keep the position reached so far.
        var info = spec.Apply("""{"id":"Main-1","type":"mediaInfo","mediaSpeed":2}""", T0.AddSeconds(50)).OfType<NowPlayingTiming>().Single();
        await Assert.That((info.Position, info.Speed)).IsEqualTo((60.0, 2.0));

        await Assert.That(spec.Apply("""{"id":"Main-1","type":"mediaStop"}""", T0.AddSeconds(60)).OfType<NowPlayingClear>().Count()).IsEqualTo(1);
        await Assert.That(spec.Apply("not json", T0).Count).IsEqualTo(0);
        await Assert.That(spec.Apply("""{"type":"mediaPlay"}""", T0).Count).IsEqualTo(0);
    }

    [Test]
    public async Task High_priority_producers_win_and_unnamed_or_hidden_media_take_the_original_paths()
    {
        var spec = new NowPlayingProducers();
        var unnamed = spec.Apply("""{"id":"a-1","type":"mediaChange","mediaUrl":"https://example.test/a%20b.mp4","mediaId":42}""", T0);
        await Assert.That(unnamed.OfType<NowPlayingAddVideo>().Single()).IsEqualTo(new NowPlayingAddVideo("https://example.test/a b.mp4", null, "42"));
        // A name arriving later names the stored row.
        await Assert.That(spec.Apply("""{"id":"a-1","type":"mediaInfo","mediaName":"Clip"}""", T0.AddSeconds(1)).OfType<NowPlayingRename>().Single().VideoName).IsEqualTo("Clip");

        var hidden = spec.Apply("""{"id":"b-1","type":"mediaChange","mediaUrl":"https://example.test/secret","mediaName":"Secret","hideUrl":true,"highPriority":true}""", T0.AddSeconds(2));
        await Assert.That(hidden.OfType<NowPlayingSet>().Single()).IsEqualTo(new NowPlayingSet("Secret", "b", "Secret", 0, null, true));
        await Assert.That(hidden.OfType<NowPlayingTiming>().Single().UrlHidden).IsTrue();
        // The newer, low-priority change does not take over from the high-priority producer.
        var later = spec.Apply("""{"id":"c-1","type":"mediaChange","mediaUrl":"https://example.test/c.mp4","mediaName":"C"}""", T0.AddSeconds(3));
        await Assert.That(later.OfType<NowPlayingSet>().Count()).IsEqualTo(0);
        await Assert.That(later.OfType<NowPlayingTiming>().Single().VideoUrl).IsEqualTo("Secret");
        await Assert.That(spec.HasProducer).IsTrue();
        spec.Reset();
        await Assert.That(spec.HasProducer).IsFalse();
    }

    [Test]
    public async Task Nowplaying_lines_store_the_play_and_hold_back_the_plain_video_line()
    {
        const string here = "wrld_protv:1~region(eu)";
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-protv");
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);

        await pc.SendLogAsync(
            GameLog.Location(here, "Cinema").At(at),
            GameLog.NowPlaying("""{"id":"Main-1","type":"mediaChange","mediaUrl":"https://example.test/m.mp4","mediaName":"Movie","mediaOwnerName":"Ann","mediaLength":5400}""").At(at.AddSeconds(1)),
            GameLog.VideoPlay("https://example.test/m.mp4").At(at.AddSeconds(2)),
            GameLog.NowPlaying("""{"id":"Main-1","type":"mediaPlay","mediaTime":3}""").At(at.AddSeconds(3)));

        await using var db = app.CreateDbContext();
        var plays = await db.VideoPlays.Where(v => v.InstanceLocation == here).ToListAsync();
        await Assert.That(plays.Select(p => (p.VideoUrl, p.VideoId, p.VideoName, p.VideoLength, p.DisplayName))).IsEquivalentTo(new[]
        {
            ("https://example.test/m.mp4", (string?)"Main", (string?)"Movie", (double?)5400, (string?)"Ann"),
        });
    }
}
