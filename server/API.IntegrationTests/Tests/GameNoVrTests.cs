using VRCX.Common.Live;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// The original's isGameNoVR: the log's desktop-mode line marks the PC's VRChat as desktop mode and
// openvr-init clears it; the Desktop Mode notification condition reads it.
public sealed class GameNoVrTests
{
    [Test]
    public async Task Desktop_mode_and_openvr_lines_set_the_pcs_mode()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-novr");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        await pc.SendLogAsync(GameLog.DesktopMode());
        await live.WaitForAsync<DesktopChanged>(d => d.ClientId == "pc-novr" && d.GameNoVr);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("desktop"))).GetProperty("gameNoVr").GetBoolean()).IsTrue();

        await pc.SendLogAsync(GameLog.OpenVrInit());
        await live.WaitForAsync<DesktopChanged>(d => d.ClientId == "pc-novr" && !d.GameNoVr && d.Connected);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("desktop"))).GetProperty("gameNoVr").GetBoolean()).IsFalse();
    }
}

// The original's checkYoutubeBotError: a fresh "Sign in to confirm" video error reaches the page.
public sealed class YouTubeBotErrorTests
{
    [Test]
    public async Task A_fresh_sign_in_to_confirm_error_is_published()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-ytbot");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        await pc.SendLogAsync(GameLog.Event("VideoError: [AVProVideo] Error: Loading failed. ERROR: [youtube] abc: Sign in to confirm you're not a bot"));
        var error = await live.WaitForAsync<YouTubeBotError>(e => e.AccountId == app.AccountId);
        await Assert.That(error.Message).Contains("Sign in to confirm");
    }
}

// Avatar changes are kept for notifications but, as in the original's table, only listed when asked for.
public sealed class AvatarChangeRowTests
{
    [Test]
    public async Task Avatar_changes_are_listed_only_when_asked_for()
    {
        const string here = "wrld_avatar:1~region(eu)";
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-avatar");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"), GameLog.Location(here, "Hub"), GameLog.PlayerJoined("Ann", "usr_ann"));
        await pc.SendLogAsync(GameLog.AvatarChange("Ann", "Fox"), GameLog.AvatarChange("Ann", "Wolf"));

        var asked = await app.GetJsonAsync(app.AccountPath("game-log?types=AvatarChange&limit=5"));
        var row = asked.EnumerateArray().Single();
        await Assert.That(row.GetProperty("type").GetString()).IsEqualTo("AvatarChange");
        await Assert.That(row.GetProperty("data").GetString()).IsEqualTo("Wolf");
        var all = await app.GetJsonAsync(app.AccountPath("game-log?limit=50"));
        await Assert.That(all.EnumerateArray().Any(r => r.GetProperty("type").GetString() == "AvatarChange")).IsFalse();
    }
}
