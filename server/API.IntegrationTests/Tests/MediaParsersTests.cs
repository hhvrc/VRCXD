using VRCX.Common.Services.Live;

namespace VRCX.API.IntegrationTests.Tests;

// The original's mediaParsers: what a dance or cinema world's "[VRCX]" line says plays.
public sealed class MediaParsersTests
{
    [Test]
    public async Task VRDancing_lines_name_the_song_and_its_dancer()
    {
        var play = MediaParsers.ParseVrcxLine("""VideoPlay(VRDancing) "https://example.com/v.mp4",10,300,42,"Alice","Cool Song" """)!;
        await Assert.That(play).IsEqualTo(new MediaPlay("https://example.com/v.mp4", "42", "Cool Song", "Alice", 10, 300));
        // -1 is YouTube, named by the YouTube lookup instead.
        var youTube = MediaParsers.ParseVrcxLine("""VideoPlay(VRDancing) "https://youtu.be/dQw4w9WgXcQ",0,300,-1,"Alice","Song" """)!;
        await Assert.That((youTube.VideoId, youTube.VideoName)).IsEqualTo(("YouTube", (string?)null));
        var tagged = MediaParsers.ParseVrcxLine("""VideoPlay(VRDancing) "https://example.com/v.mp4",0,200,5,"Bob","[Tag]</b> Actual Title" """)!;
        await Assert.That(tagged.VideoName).IsEqualTo("Actual Title");
        var ended = MediaParsers.ParseVrcxLine("""VideoPlay(VRDancing) "https://example.com/v.mp4",300,300,5,"Bob","Title" """)!;
        await Assert.That(ended.VideoPos).IsEqualTo(0);
        await Assert.That(MediaParsers.ParseVrcxLine("VideoPlay(VRDancing) bad data")).IsNull();
    }

    [Test]
    public async Task LSMedia_lines_need_a_name()
    {
        var play = MediaParsers.ParseVrcxLine("LSMedia 0,6298.292,Natsumi-sama,The Outfit (2022),")!;
        await Assert.That(play).IsEqualTo(new MediaPlay("The Outfit (2022)", "LSMedia", "The Outfit (2022)", "Natsumi-sama", 0, 6298.292));
        await Assert.That(MediaParsers.ParseVrcxLine("LSMedia 0,4268.981,Natsumi-sama,,")).IsNull();
        await Assert.That(MediaParsers.ParseVrcxLine("LSMedia bad data")).IsNull();
    }

    [Test]
    public async Task PopcornPalace_lines_are_json_and_an_empty_name_is_a_stop()
    {
        var play = MediaParsers.ParseVrcxLine("""VideoPlay(PopcornPalace) {"videoName":"How to Train Your Dragon","videoPos":37.28,"videoLength":11474.05,"thumbnailUrl":"https://example.com/thumb.jpg","displayName":"miner28_3","isPaused":false}""")!;
        await Assert.That(play).IsEqualTo(new MediaPlay("How to Train Your Dragon", "PopcornPalace", "How to Train Your Dragon", "miner28_3", 37.28, 11474.05));
        await Assert.That(MediaParsers.ParseVrcxLine("""VideoPlay(PopcornPalace) {"videoName":"","videoPos":0,"videoLength":0,"displayName":"user"}""")).IsNull();
        await Assert.That(MediaParsers.ParseVrcxLine("VideoPlay(PopcornPalace) {bad json")).IsNull();
        await Assert.That(MediaParsers.ParseVrcxLine("garbage")).IsNull();
    }

    [Test]
    public async Task YouTube_ids_come_from_plain_and_proxied_urls()
    {
        await Assert.That(MediaParsers.YouTubeId("https://youtu.be/dQw4w9WgXcQ")).IsEqualTo("dQw4w9WgXcQ");
        await Assert.That(MediaParsers.YouTubeId("https://t-ne.x0.to/?url=https://www.youtube.com/watch?v=abcdefghijk")).IsEqualTo("abcdefghijk");
        await Assert.That(MediaParsers.YouTubeId("https://example.com/video.mp4")).IsNull();
        await Assert.That(MediaParsers.IsRpcWorld("wrld_f20326da-f1ac-45fc-a062-609723b097b1:1")).IsTrue();
        await Assert.That(MediaParsers.IsRpcWorld("wrld_123:456")).IsFalse();
    }
}
