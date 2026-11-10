using VRCX.Common.Services.Live;

namespace VRCX.Common.Tests;

// The original's gameLog/mediaParsers.js: YouTube ids and the "[VRCX]" lines dance and cinema worlds write.
public class MediaParsersTests
{
    [Test]
    [Arguments("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [Arguments("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10", "dQw4w9WgXcQ")]
    [Arguments("https://music.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [Arguments("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [Arguments("https://t-ne.x0.to/?url=https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [Arguments("https://u2b.cx/https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [Arguments("https://example.test/video.mp4", null)]
    [Arguments("not a url", null)]
    public async Task YouTube_ids_are_found_as_the_original_finds_them(string url, string? id) =>
        await Assert.That(MediaParsers.YouTubeId(url)).IsEqualTo(id);

    [Test]
    public async Task A_VRDancing_line_names_its_video_and_dancer()
    {
        var play = MediaParsers.ParseVrcxLine("""VideoPlay(VRDancing) "https://example.test/d.mp4",12.5,200.1,42,"Dancer","<b>[Hard]</b> Song Title" """);
        await Assert.That(play).IsEqualTo(new MediaPlay("https://example.test/d.mp4", "42", "Song Title", "Dancer", 12.5, 200.1));
    }

    [Test]
    public async Task A_VRDancing_YouTube_line_is_left_to_the_YouTube_lookup()
    {
        var play = MediaParsers.ParseVrcxLine("""VideoPlay(VRDancing) "https://youtu.be/dQw4w9WgXcQ",0,200,-1,"Dancer","Title" """);
        await Assert.That(play).IsEqualTo(new MediaPlay("https://youtu.be/dQw4w9WgXcQ", "YouTube", null, "Dancer", 0, 200));
    }

    [Test]
    public async Task An_LSMedia_line_is_named_by_its_title()
    {
        var play = MediaParsers.ParseVrcxLine("LSMedia 0,6298.292,Natsumi-sama,The Outfit (2022), 1080p");
        await Assert.That(play).IsEqualTo(new MediaPlay("The Outfit (2022)", "LSMedia", "The Outfit (2022)", "Natsumi-sama", 0, 6298.292));
    }

    [Test]
    public async Task A_PopcornPalace_line_is_named_and_an_empty_one_is_nothing()
    {
        var play = MediaParsers.ParseVrcxLine("""VideoPlay(PopcornPalace) {"videoName": "How to Train Your Dragon - 2025-06-06", "videoPos": 37.28777, "videoLength": 11474.05, "thumbnailUrl": "", "displayName": "miner28_3", "isPaused": false}""");
        await Assert.That(play).IsEqualTo(new MediaPlay("How to Train Your Dragon - 2025-06-06", "PopcornPalace", "How to Train Your Dragon - 2025-06-06", "miner28_3", 37.28777, 11474.05));
        await Assert.That(MediaParsers.ParseVrcxLine("""VideoPlay(PopcornPalace) {"videoName": ""}""")).IsNull();
        await Assert.That(MediaParsers.ParseVrcxLine("VideoPlay(PyPyDance) \"x\",1,2,\"y\"")).IsNull();
    }

    [Test]
    public async Task VrDancing_position_equal_to_length_counts_as_the_start()
    {
        var play = MediaParsers.ParseVrcxLine("""VideoPlay(VRDancing) "https://example.test/d.mp4",200,200,42,"Dancer","Song" """);
        await Assert.That(play!.VideoPos).IsEqualTo(0);
    }

    [Test]
    [Arguments("PT3M33S", 213)]
    [Arguments("PT1H", 3600)]
    [Arguments("PT1H5S", 3605)]
    [Arguments("PT45M", 2700)]
    [Arguments("P1DT1S", 86401)]
    [Arguments("bogus", 0)]
    public async Task YouTube_durations_read_as_seconds(string duration, double seconds) =>
        await Assert.That(MediaParsers.YouTubeSeconds(duration)).IsEqualTo(seconds);
}
