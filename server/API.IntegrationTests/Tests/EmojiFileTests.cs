using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// A boop's animated emoji plays from its file's flipbook settings, as the original's Emoji component.
public sealed class EmojiFileTests
{
    [Test]
    public async Task An_animated_emoji_reports_its_frames()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Json("file/file_anim", """{"id":"file_anim","name":"Emoji","tags":["emoji"],"versions":[],"animationStyle":"aura","frames":16,"framesOverTime":12,"loopStyle":"pingpong"}""");
        app.Vrchat.Json("file/file_still", """{"id":"file_still","name":"Emoji","tags":["emoji"],"versions":[]}""");

        var animated = await app.GetJsonAsync(app.AccountPath("emoji/file_anim"));
        var still = await app.GetJsonAsync(app.AccountPath("emoji/file_still"));

        await Assert.That(animated.GetProperty("frames").GetInt32()).IsEqualTo(16);
        await Assert.That(animated.GetProperty("framesOverTime").GetInt32()).IsEqualTo(12);
        await Assert.That(animated.GetProperty("loopStyle").GetString()).IsEqualTo("pingpong");
        await Assert.That(still.TryGetProperty("frames", out _)).IsFalse();
    }
}
