using System.Net;
using System.Net.Http.Json;
using VRCX.Common.Live;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// The VRCX named pipe on the gaming PC (the original's Dotnet/IPC, ipcEvent and eventVrcxMessage),
// with the desktop app relaying what other programs write to it.
public sealed class IpcTests
{
    [Test]
    public async Task Custom_tags_from_the_pipe_are_kept_per_account()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-tags");

        await pc.SendIpcAsync(Ipc.CustomTag("usr_ann", "Streamer", "#ff00ff"));
        var set = await live.WaitForAsync<CustomTagChanged>(t => t.UserId == "usr_ann");
        await Assert.That(set.AccountId).IsEqualTo(app.AccountId);
        await Assert.That(set.Tag).IsEqualTo("Streamer");
        await Assert.That(set.Colour).IsEqualTo("#ff00ff");

        await pc.SendIpcAsync(Ipc.CustomTag("usr_ann", "Moderator", "red"));
        await pc.SendIpcAsync(Ipc.CustomTag("usr_bob", "Plain"));
        var tags = (await app.GetJsonAsync(app.AccountPath("custom-tags"))).EnumerateArray().ToList();
        await Assert.That(tags.Count).IsEqualTo(2);
        await Assert.That(tags[0].GetProperty("userId").GetString()).IsEqualTo("usr_ann");
        await Assert.That(tags[0].GetProperty("tag").GetString()).IsEqualTo("Moderator");
        await Assert.That(tags[0].GetProperty("colour").GetString()).IsEqualTo("red");
        // No colour sent, none kept (null is left out of the JSON).
        await Assert.That(tags[1].TryGetProperty("colour", out _)).IsFalse();

        // An empty tag removes the user's tag, as addCustomTag does.
        await pc.SendIpcAsync(Ipc.CustomTag("usr_bob", ""));
        await live.WaitForAsync<CustomTagChanged>(t => t.UserId == "usr_bob" && t.Tag == "");
        await Assert.That((await app.GetJsonAsync(app.AccountPath("custom-tags"))).GetArrayLength()).IsEqualTo(1);

        await pc.SendIpcAsync(Ipc.ClearCustomTags());
        await live.WaitForAsync<CustomTagChanged>(t => t.Cleared && t.AccountId == app.AccountId);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("custom-tags"))).GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task External_messages_become_game_log_rows_in_the_current_instance()
    {
        const string here = "wrld_ipc:1~region(eu)";
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-external");
        await pc.SendLogAsync(GameLog.Location(here, "Hub"));

        await pc.SendIpcAsync(Ipc.External("Ann waved", "usr_ann", "Ann"));
        var stored = await live.WaitForAsync<GameLogStored>(g => g.Kinds.Contains("external"));
        await Assert.That(stored.AccountId).IsEqualTo(app.AccountId);
        await Assert.That(stored.InstanceLocation).IsEqualTo(here);

        // notify false is stored all the same, but asks for no notification.
        await pc.SendIpcAsync(Ipc.External("quiet one", notify: false));
        await live.WaitForAsync<GameLogStored>(g => g.Kinds.Contains("external_quiet"));

        var rows = (await app.GetJsonAsync(app.AccountPath("game-log?types=External&limit=10"))).EnumerateArray().ToList();
        await Assert.That(rows.Count).IsEqualTo(2);
        var waved = rows.Single(r => r.GetProperty("message").GetString() == "Ann waved");
        await Assert.That(waved.GetProperty("displayName").GetString()).IsEqualTo("Ann");
        await Assert.That(waved.GetProperty("userId").GetString()).IsEqualTo("usr_ann");
        await Assert.That(waved.GetProperty("location").GetString()).IsEqualTo(here);
    }

    [Test]
    public async Task Noty_is_an_event_unless_a_photon_logger_or_a_newer_notifier_is_on_the_pipe()
    {
        const string here = "wrld_noty:1~region(eu)";
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-noty");
        await pc.SendLogAsync(GameLog.Location(here, "Hub"));

        await pc.SendIpcAsync(Ipc.MsgPing(21));
        await pc.SendIpcAsync(Ipc.Noty("old notifier"));
        await pc.SendIpcAsync(Ipc.MsgPing(22));
        await pc.SendIpcAsync(Ipc.Noty("new notifier"));

        await using var photon = await app.ConnectDesktopAsync("pc-photon");
        await photon.SendLogAsync(GameLog.Location(here, "Hub"));
        await photon.SendIpcAsync(Ipc.Ping());
        await photon.SendIpcAsync(Ipc.Noty("photon logger"));

        var events = (await app.GetJsonAsync(app.AccountPath("game-log?types=Event&limit=10"))).EnumerateArray()
            .Select(r => r.GetProperty("data").GetString()!).ToList();
        await Assert.That(events).IsEquivalentTo(["old notifier"]);
    }

    [Test]
    public async Task The_pipe_hears_that_vrcx_started_and_which_user_dialog_opened()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-announce");

        var launch = await pc.NextIpcAsync();
        await Assert.That(launch.Type).IsEqualTo("VRCXLaunch");
        await Assert.That(launch.MsgType).IsEqualTo("VRCXLaunch");

        var response = await app.Api.PostAsJsonAsync(app.AccountPath("desktop/ipc/user-dialog"), new { userId = "usr_ann" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var shown = await pc.NextIpcAsync();
        await Assert.That(shown.Type).IsEqualTo("VrcxMessage");
        await Assert.That(shown.MsgType).IsEqualTo("ShowUserDialog");
        await Assert.That(shown.Data).IsEqualTo("usr_ann");
    }

    [Test]
    public async Task A_user_dialog_with_no_desktop_connected_is_a_no_op()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var response = await app.Api.PostAsJsonAsync(app.AccountPath("desktop/ipc/user-dialog"), new { userId = "usr_ann" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }
}
