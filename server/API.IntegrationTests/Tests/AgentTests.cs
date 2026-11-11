using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// The agent says what it does in its hello; the desktop status passes that on with SteamVR and the
// PC's folders, so pages offer only what the PC can do.
public sealed class AgentStatusTests
{
    [Test]
    public async Task The_status_carries_the_agents_capabilities_folders_and_SteamVR()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-caps", capabilities: [Capability.Launch, Capability.OpenInstance, Capability.Folders]);
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));
        await pc.GameStateAsync(running: true, steamVrRunning: true);

        var status = await app.GetJsonAsync(app.AccountPath("desktop"));
        await Assert.That(status.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString()!).ToList()).IsEquivalentTo(["launch", "open_instance", "folders"]);
        await Assert.That(status.GetProperty("os").GetString()).IsEqualTo("windows");
        await Assert.That(status.GetProperty("steamVrRunning").GetBoolean()).IsTrue();
        await Assert.That(status.GetProperty("folders").GetProperty("photos").GetString()).IsEqualTo(FakeDesktop.TestFolders.Photos);

        await pc.GameStateAsync(running: true, steamVrRunning: false);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("desktop"))).GetProperty("steamVrRunning").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task An_action_the_agent_did_not_declare_is_refused_before_it_is_sent()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-nocaps", capabilities: [Capability.Launch]);
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var response = await app.Api.PostAsJsonAsync(app.AccountPath("agent/open-folder"), new { folder = "photos" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains("Agent.Unsupported");
    }

    [Test]
    public async Task Relayed_actions_answer_with_what_the_agent_said()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-folders");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var open = app.Api.PostAsJsonAsync(app.AccountPath("agent/open-folder"), new { folder = "ugc_photos", relativePath = "Prints/a.png" });
        var action = await pc.NextActionAsync();
        await Assert.That(action.OpenFolder.Folder).IsEqualTo(Folder.UgcPhotos);
        await Assert.That(action.OpenFolder.RelativePath).IsEqualTo("Prints/a.png");
        await pc.CompleteAsync(action, ActionStatus.NotFound, "That file is not in the folder");
        await Assert.That((await open).StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        var pick = app.Api.PostAsJsonAsync(app.AccountPath("agent/pick-path"), new { purpose = "ugc_folder" });
        action = await pc.NextActionAsync();
        await Assert.That(action.PickPath.Purpose).IsEqualTo(PickPurpose.UgcFolder);
        await pc.CompleteAsync(action, json: """{"path":"D:/Prints"}""");
        var picked = await pick;
        await Assert.That(picked.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await picked.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("path").GetString()).IsEqualTo("D:/Prints");

        var bad = await app.Api.PostAsJsonAsync(app.AccountPath("agent/open-folder"), new { folder = "windows" });
        await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}

// The settings the agent acts on by itself reach it on connect and on every save.
public sealed class AgentSettingsPushTests
{
    [Test]
    public async Task Saved_settings_reach_the_pc_and_a_reconnect_gets_them_again()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var pc = await app.ConnectDesktopAsync("pc-settings");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var settings = await app.GetJsonAsync(app.AccountPath("settings"));
        var body = System.Text.Json.Nodes.JsonNode.Parse(settings.GetRawText())!.AsObject();
        body["ugcFolderPath"] = " D:/Prints ";
        body["enableAppLauncher"] = false;
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync(app.AccountPath("settings"), body), "save settings");

        var pushed = await pc.NextAgentSettingsAsync();
        await Assert.That(pushed.UgcFolder).IsEqualTo("D:/Prints");
        await Assert.That(pushed.AppLauncherEnabled).IsFalse();
        await Assert.That(pushed.AppLauncherKillOnExit).IsTrue();

        await pc.DisposeAsync();
        await using var again = await app.ConnectDesktopAsync("pc-settings");
        await Assert.That((await again.NextAgentSettingsAsync()).UgcFolder).IsEqualTo("D:/Prints");
    }

    [Test]
    public async Task OpenVR_settings_reach_the_pc_and_its_hmd_afk_reaches_the_status()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-vr");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var settings = await app.GetJsonAsync(app.AccountPath("settings"));
        var body = System.Text.Json.Nodes.JsonNode.Parse(settings.GetRawText())!.AsObject();
        body["openVr"] = true;
        body["openVrAlways"] = true;
        body["overlayWrist"] = true;
        body["overlayButton"] = true;
        body["overlayHand"] = 2;
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync(app.AccountPath("settings"), body), "save settings");
        var pushed = await pc.NextAgentSettingsAsync();
        await Assert.That((pushed.OpenVr, pushed.OpenVrAlways, pushed.WristOverlay, pushed.OverlayMenuButton, pushed.OverlayHand)).IsEqualTo((true, true, true, true, 2));

        await pc.HmdAfkAsync(true);
        var status = await app.GetJsonAsync(app.AccountPath("desktop"));
        await Assert.That(status.GetProperty("hmdAfk").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task An_agent_without_the_settings_capability_is_not_sent_them()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-old", capabilities: [Capability.Launch]);
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));
        var settings = await app.GetJsonAsync(app.AccountPath("settings"));
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsync(app.AccountPath("settings"), new StringContent(settings.GetRawText(), System.Text.Encoding.UTF8, "application/json")), "save settings");
        await Assert.That(async () => await pc.NextAgentSettingsAsync(TimeSpan.FromSeconds(1))).Throws<TimeoutException>();
    }

    [Test]
    public async Task Deleting_all_screenshot_metadata_is_relayed_with_the_confirmation()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-meta");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var delete = app.Api.PostAsJsonAsync(app.AccountPath("agent/screenshot-metadata/delete-all"), new { confirm = true });
        var action = await pc.NextActionAsync();
        await Assert.That(action.DeleteScreenshotMetadata.All).IsTrue();
        await Assert.That(action.DeleteScreenshotMetadata.Confirm).IsTrue();
        await pc.CompleteAsync(action, json: """{"changed":3}""");
        await Assert.That((await (await delete).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("changed").GetInt32()).IsEqualTo(3);

        var refused = app.Api.PostAsJsonAsync(app.AccountPath("agent/screenshot-metadata/delete-all"), new { confirm = false });
        await pc.CompleteAsync(await pc.NextActionAsync(), ActionStatus.NotConfirmed, "Not confirmed");
        await Assert.That((await refused).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}

// The original's runCheckIfGameCrashedFlow: VRChat closing without its quit line starts it again in
// the instance it was in, when the account asked for that.
public sealed class CrashRelaunchTests
{
    private const string Here = "wrld_crash:1~region(eu)";

    private static async Task<(VrcxScenario App, FakeDesktop Pc)> PlayingAsync(string clientId, bool relaunch, params LogEntry[] extra)
    {
        var app = await VrcxScenario.StartAsync();
        var relauncher = app.Services.GetRequiredService<CrashRelaunch>();
        relauncher.VrDelay = relauncher.DesktopDelay = TimeSpan.FromMilliseconds(100);
        await app.LoginAsync();
        await app.Api.PutAsJsonAsync(app.AccountPath("settings"), new { relaunchVrchatAfterCrash = relaunch });
        var pc = await app.ConnectDesktopAsync(clientId);
        await pc.GameStateAsync(running: true, steamVrRunning: true);
        await pc.SendLogAsync([GameLog.UserAuthenticated(app.AccountId!, "Me"), .. extra, GameLog.Location(Here, "Crash")]);
        return (app, pc);
    }

    [Test]
    public async Task A_crash_relaunches_into_the_last_instance()
    {
        var (app, pc) = await PlayingAsync("pc-crash", relaunch: true);
        await using var _ = app;
        await pc.GameStateAsync(running: false, steamVrRunning: true, closedGracefully: false);

        var launch = (await pc.NextActionAsync()).LaunchVrchat;
        await Assert.That(launch.LaunchUrl).IsEqualTo($"vrchat://launch?ref=vrcx.app&id={Here}");
        await Assert.That(launch.DesktopMode).IsFalse();
        await using var db = app.CreateDbContext();
        await Assert.That(await db.GameLogEvents.CountAsync(e => e.Data == CrashRelaunch.Message && e.InstanceLocation == Here)).IsEqualTo(1);

        // A second crash within two minutes is a crash loop: left alone.
        await pc.GameStateAsync(running: true, steamVrRunning: true);
        await pc.SendLogAsync(GameLog.Location(Here, "Crash"));
        await pc.GameStateAsync(running: false, steamVrRunning: true, closedGracefully: false);
        await Assert.That(async () => await pc.NextActionAsync(TimeSpan.FromSeconds(1))).Throws<TimeoutException>();
    }

    [Test]
    public async Task A_desktop_mode_crash_relaunches_in_desktop_mode_without_SteamVR()
    {
        var (app, pc) = await PlayingAsync("pc-crash-novr", relaunch: true, GameLog.DesktopMode());
        await using var _ = app;
        await pc.GameStateAsync(running: false, steamVrRunning: false, closedGracefully: false);
        await Assert.That((await pc.NextActionAsync()).LaunchVrchat.DesktopMode).IsTrue();
    }

    [Test]
    public async Task A_quit_SteamVR_closing_or_the_setting_off_does_not_relaunch()
    {
        foreach (var (id, relaunch, steamVr, graceful) in new[] { ("pc-quit", true, true, true), ("pc-vr-closed", true, false, false), ("pc-off", false, true, false) })
        {
            var (app, pc) = await PlayingAsync(id, relaunch);
            await using var _ = app;
            await pc.GameStateAsync(running: false, steamVrRunning: steamVr, closedGracefully: graceful);
            await Assert.That(async () => await pc.NextActionAsync(TimeSpan.FromSeconds(1))).Throws<TimeoutException>();
        }
    }
}

// VRChat's config.json, registry and local moderations relayed to the PC, and the registry backups
// kept here (the original's VRCX_VRChatRegistryBackups).
public sealed class AgentVrchatTests
{
    private const string Registry = """{"LOGGING_ENABLED":{"data":1,"type":4},"FOV":{"data":0.5,"type":100}}""";

    [Test]
    public async Task Config_registry_keys_and_moderations_reach_the_pc()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-vrc");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var write = app.Api.PutAsJsonAsync(app.AccountPath("agent/vrchat-config"), new { json = """{"cache_size":30}""" });
        var action = await pc.NextActionAsync();
        await Assert.That(action.WriteVrchatConfig.Json).IsEqualTo("""{"cache_size":30}""");
        await pc.CompleteAsync(action);
        await Assert.That((await write).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var set = app.Api.PutAsJsonAsync(app.AccountPath("agent/vrchat-registry/keys/LOGGING_ENABLED"), new { value = "1", type = 4 });
        action = await pc.NextActionAsync();
        await Assert.That(action.SetVrchatRegistryKey.Key).IsEqualTo("LOGGING_ENABLED");
        await Assert.That(action.SetVrchatRegistryKey.ValueJson).IsEqualTo("\"1\"");
        await Assert.That(action.SetVrchatRegistryKey.Type).IsEqualTo(4);
        await pc.CompleteAsync(action);
        await set;

        var hide = app.Api.PutAsJsonAsync(app.AccountPath("agent/local-moderations/usr_other"), new { type = 4 });
        action = await pc.NextActionAsync();
        await Assert.That(action.SetLocalModeration.CurrentUserId).IsEqualTo(app.AccountId!);
        await Assert.That(action.SetLocalModeration.UserId).IsEqualTo("usr_other");
        await pc.CompleteAsync(action, ActionStatus.NotFound, "VRChat has no local moderation list for this user yet");
        await Assert.That((await hide).StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        var delete = app.Api.PostAsJsonAsync(app.AccountPath("agent/vrchat-registry/delete"), new { confirm = true });
        action = await pc.NextActionAsync();
        await Assert.That(action.DeleteVrchatRegistry.Confirm_).IsTrue();
        await pc.CompleteAsync(action);
        await delete;
    }

    [Test]
    public async Task A_backup_is_kept_listed_restored_and_forgotten()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-backup");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var create = app.Api.PostAsJsonAsync(app.AccountPath("registry-backups"), new { name = "Before update" });
        var action = await pc.NextActionAsync();
        await Assert.That(action.ActionCase).IsEqualTo(ClientAction.ActionOneofCase.ReadVrchatRegistry);
        await pc.CompleteAsync(action, json: Registry);
        var created = await (await create).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = created.GetProperty("id").GetInt64();

        var list = await app.GetJsonAsync(app.AccountPath("registry-backups"));
        await Assert.That(list.GetArrayLength()).IsEqualTo(1);
        await Assert.That(list[0].GetProperty("name").GetString()).IsEqualTo("Before update");
        await Assert.That(list[0].GetProperty("data").GetProperty("FOV").GetProperty("type").GetInt32()).IsEqualTo(100);

        var restore = app.Api.PostAsJsonAsync(app.AccountPath($"registry-backups/{id}/restore"), new { confirm = true });
        action = await pc.NextActionAsync();
        await Assert.That(action.WriteVrchatRegistry.Confirm).IsTrue();
        await Assert.That(System.Text.Json.JsonDocument.Parse(action.WriteVrchatRegistry.Json).RootElement.GetProperty("LOGGING_ENABLED").GetProperty("data").GetInt32()).IsEqualTo(1);
        await pc.CompleteAsync(action);
        await Assert.That((await restore).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await app.Api.DeleteAsync(app.AccountPath($"registry-backups/{id}"));
        await Assert.That((await app.GetJsonAsync(app.AccountPath("registry-backups"))).GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task The_start_check_backs_up_when_due_and_offers_a_restore_once_the_registry_is_gone()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-auto");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        async Task<bool> CheckAsync(ActionStatus status, string json = "")
        {
            var check = app.Api.PostAsync(app.AccountPath("registry-backups/check"), null);
            await pc.CompleteAsync(await pc.NextActionAsync(), status, status == ActionStatus.Ok ? "" : "none", json);
            return (await (await check).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("askRestore").GetBoolean();
        }

        // With the registry there and no backup yet: an auto backup (it reads the registry once more).
        var first = app.Api.PostAsync(app.AccountPath("registry-backups/check"), null);
        await pc.CompleteAsync(await pc.NextActionAsync(), json: Registry);
        await pc.CompleteAsync(await pc.NextActionAsync(), json: Registry);
        await Assert.That((await (await first).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("askRestore").GetBoolean()).IsFalse();
        var list = await app.GetJsonAsync(app.AccountPath("registry-backups"));
        await Assert.That(list.GetArrayLength()).IsEqualTo(1);
        await Assert.That(list[0].GetProperty("name").GetString()).IsEqualTo("Auto Backup");

        // Not due again within three days: only the check's read.
        await Assert.That(await CheckAsync(ActionStatus.Ok, Registry)).IsFalse();
        await Assert.That((await app.GetJsonAsync(app.AccountPath("registry-backups"))).GetArrayLength()).IsEqualTo(1);

        // The registry gone: asked once, not again for the same backup.
        await Assert.That(await CheckAsync(ActionStatus.NotFound)).IsTrue();
        await Assert.That(await CheckAsync(ActionStatus.NotFound)).IsFalse();
    }
}

// runCheckVRChatDebugLoggingFlow: the game log needs VRChat's debug logging, so it is turned back on.
public sealed class DebugLoggingCheckTests
{
    [Test]
    public async Task Logging_turned_off_is_turned_back_on_and_the_page_is_told()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Services.GetRequiredService<DebugLoggingCheck>().Delay = TimeSpan.FromMilliseconds(50);
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-logging");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));
        using var events = app.Services.GetRequiredService<ILiveEventBus>().Subscribe();

        await pc.GameStateAsync(running: true, steamVrRunning: false);
        var read = await pc.NextActionAsync();
        await Assert.That(read.ReadVrchatRegistryKey.Key).IsEqualTo("LOGGING_ENABLED");
        await pc.CompleteAsync(read, json: """{"value":0}""");
        var set = await pc.NextActionAsync();
        await Assert.That(set.SetVrchatRegistryKey.ValueJson).IsEqualTo("\"1\"");
        await Assert.That(set.SetVrchatRegistryKey.Type).IsEqualTo(4);
        await pc.CompleteAsync(set);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        AgentAlert? alert = null;
        while (alert is null) alert = (await events.Reader.ReadAsync(wait.Token)).AgentAlert;
        await Assert.That(alert.Kind).IsEqualTo(AgentAlertKind.DebugLoggingEnabled);
        await Assert.That(alert.AccountId).IsEqualTo(app.AccountId!);

        // Already on: nothing written.
        await pc.GameStateAsync(running: false, steamVrRunning: false);
        await pc.CompleteAsync(await pc.NextActionAsync(), json: """{"value":1}""");
        await Assert.That(async () => await pc.NextActionAsync(TimeSpan.FromMilliseconds(500))).Throws<TimeoutException>();
    }
}

// VRChat's asset bundle cache on the PC, and the original's auto cache management on game stop.
public sealed class AgentCacheTests
{
    [Test]
    public async Task Cache_actions_reach_the_pc_with_the_entry()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-cache");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var check = app.Api.PostAsJsonAsync(app.AccountPath("agent/cache/check"), new { id = "file_a", version = 3, variant = "security", variantVersion = 2 });
        var action = await pc.NextActionAsync();
        await Assert.That(action.CheckCache.Id).IsEqualTo("file_a");
        await Assert.That(action.CheckCache.Version).IsEqualTo(3);
        await Assert.That(action.CheckCache.Variant).IsEqualTo("security");
        await Assert.That(action.CheckCache.VariantVersion).IsEqualTo(2);
        await pc.CompleteAsync(action, json: """{"size":10,"locked":false,"path":"C:/c/x"}""");
        await Assert.That((await (await check).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("size").GetInt64()).IsEqualTo(10);

        var clear = app.Api.PostAsJsonAsync(app.AccountPath("agent/cache/clear"), new { confirm = true });
        action = await pc.NextActionAsync();
        await Assert.That(action.ClearCache.Confirm_).IsTrue();
        await pc.CompleteAsync(action);
        await Assert.That((await clear).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Stopping_the_game_sweeps_the_cache_only_with_the_setting_on()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-sweep");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        await pc.GameStateAsync(running: true, steamVrRunning: false);
        await pc.GameStateAsync(running: false, steamVrRunning: false, closedGracefully: true);
        await Assert.That(async () => await pc.NextActionAsync(TimeSpan.FromMilliseconds(800))).Throws<TimeoutException>();

        await app.Api.PutAsJsonAsync(app.AccountPath("settings"), new { autoSweepVrchatCache = true });
        await pc.NextAgentSettingsAsync();
        await pc.GameStateAsync(running: true, steamVrRunning: false);
        await pc.GameStateAsync(running: false, steamVrRunning: false, closedGracefully: true);
        await Assert.That((await pc.NextActionAsync()).ActionCase).IsEqualTo(ClientAction.ActionOneofCase.SweepCache);
    }
}

// The original's save prints, stickers and emoji options, through the PC's agent.
public sealed class MediaSaverTests
{
    private const string PrintId = "prnt_0e8c4e32-7444-44ea-ade4-313c010d4bae";
    private const string InventoryId = "inv_0e8c4e32-7444-44ea-ade4-313c010d4bae";

    private sealed class PngFetcher : VRCX.Common.Services.Images.IImageFetcher
    {
        public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9];

        public Task<VRCX.Common.Services.Images.DownloadedImage?> FetchAsync(Uri url, CancellationToken ct) =>
            Task.FromResult<VRCX.Common.Services.Images.DownloadedImage?>(new(new MemoryStream(Png)));
    }

    [Test]
    public async Task Urls_and_file_names_are_the_originals()
    {
        await Assert.That(MediaSaver.PrintIdOf($"https://api.vrchat.cloud/api/1/prints/{PrintId}")).IsEqualTo(PrintId);
        await Assert.That(MediaSaver.PrintIdOf("https://api.vrchat.cloud/api/1/prints/user/usr_x")).IsNull();
        await Assert.That(MediaSaver.InventoryOf($"https://api.vrchat.cloud/api/1/user/usr_a/inventory/{InventoryId}")).IsEqualTo(("usr_a", InventoryId));
        var created = DateTimeOffset.Parse("2024-11-03T15:14:25.757Z");
        var date = MediaSaver.PrintDate(created, null, 60, DateTimeOffset.UtcNow);
        await Assert.That(MediaSaver.PrintFileName("Author", date, PrintId)).IsEqualTo($"Author_2024-11-03_16-14-25.757_{PrintId}.png");
        await Assert.That(MediaSaver.StickerFileName("Me", "2024-11-03T16:14:25.757Z", InventoryId)).IsEqualTo($"Me_2024-11-03_16-14-25.757_{InventoryId}.png");
        var animated = System.Text.Json.JsonDocument.Parse("""{"animationStyle":"bats","frames":4,"framesOverTime":10}""").RootElement;
        await Assert.That(MediaSaver.EmojiFileName("Me_inv", animated)).IsEqualTo("Me_inv_batsanimationStyle_4frames_10fps_linearloopStyle.png");
        var still = System.Text.Json.JsonDocument.Parse("""{"animationStyle":"aura"}""").RootElement;
        await Assert.That(MediaSaver.EmojiFileName("Me_inv", still)).IsEqualTo("Me_inv_auraanimationStyle.png");
    }

    [Test]
    public async Task A_print_and_a_sticker_seen_in_the_log_are_saved_on_the_pc()
    {
        MediaSaver.Spacing = TimeSpan.Zero;
        await using var app = await VrcxScenario.StartAsync(new() { ConfigureServices = s => s.Replace(ServiceDescriptor.Singleton<VRCX.Common.Services.Images.IImageFetcher>(new PngFetcher())) });
        await app.LoginAsync();
        app.Vrchat
            .Json($"prints/{PrintId}", $$$"""{"id":"{{{PrintId}}}","authorName":"Author","timestamp":"2024-11-03T15:14:25.757Z","files":{"image":"https://api.vrchat.cloud/api/1/file/file_p/1/file"}}""")
            .Json($"user/usr_b/inventory/{InventoryId}", """{"itemType":"sticker","flags":["ugc"],"created_at":"2024-11-03T16:14:25.757Z","metadata":{"imageUrl":"https://api.vrchat.cloud/api/1/file/file_s/1/file"}}""");
        await using var pc = await app.ConnectDesktopAsync("pc-media");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        // Off: nothing is saved.
        await pc.SendLogAsync(GameLog.ApiRequest($"https://api.vrchat.cloud/api/1/prints/{PrintId}"));
        await Assert.That(async () => await pc.NextActionAsync(TimeSpan.FromMilliseconds(800))).Throws<TimeoutException>();

        await app.Api.PutAsJsonAsync(app.AccountPath("settings"), new { saveInstancePrints = true, cropInstancePrints = true, saveInstanceStickers = true });
        await pc.NextAgentSettingsAsync();
        await pc.SendLogAsync(GameLog.StickerSpawn("usr_b", "Bee", InventoryId));
        var action = await pc.NextActionAsync();
        await Assert.That(action.SaveMedia.Kind).IsEqualTo(MediaKind.Sticker);
        await Assert.That(action.SaveMedia.MonthFolder).IsEqualTo("2024-11");
        await Assert.That(action.SaveMedia.FileName).IsEqualTo($"Bee_2024-11-03_16-14-25.757_{InventoryId}.png");
        await Assert.That(action.SaveMedia.Data.ToByteArray()).IsEquivalentTo(PngFetcher.Png);
        await pc.CompleteAsync(action, json: """{"path":"x"}""");

        // A print seen while the option was off is saved when seen again with it on.
        await pc.SendLogAsync(GameLog.ApiRequest($"https://api.vrchat.cloud/api/1/prints/{PrintId}"));
        action = await pc.NextActionAsync();
        await Assert.That(action.SaveMedia.Kind).IsEqualTo(MediaKind.Print);
        await Assert.That(action.SaveMedia.Crop).IsTrue();
        await Assert.That(action.SaveMedia.FileName).IsEqualTo($"Author_2024-11-03_15-14-25.757_{PrintId}.png");
        await pc.CompleteAsync(action, json: """{"path":"x"}""");

        // Seen twice: saved once.
        await pc.SendLogAsync(GameLog.ApiRequest($"https://api.vrchat.cloud/api/1/prints/{PrintId}"));
        await Assert.That(async () => await pc.NextActionAsync(TimeSpan.FromMilliseconds(800))).Throws<TimeoutException>();
    }
}

// Notifications and Export to Calendar, shown by the PC's agent.
public sealed class AgentNotifyTests
{
    [Test]
    public async Task A_notification_reaches_the_pc_with_its_overlays()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-notify");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var sent = app.Api.PostAsJsonAsync(app.AccountPath("agent/notify"), new
        {
            title = "Bee",
            body = "has come online",
            overlayText = "Bee has logged in",
            desktop = true,
            xsoverlay = true,
            ovrtoolkitHud = true,
            ovrtoolkitWrist = false,
            timeout = 3,
            opacity = 0.5,
            steamvrOverlay = true,
            position = "bottomRight",
            dark = true,
        });
        var action = await pc.NextActionAsync();
        await Assert.That(action.ShowNotification.Title).IsEqualTo("Bee");
        await Assert.That(action.ShowNotification.OverlayText).IsEqualTo("Bee has logged in");
        await Assert.That(action.ShowNotification.Xsoverlay).IsTrue();
        await Assert.That(action.ShowNotification.OvrtoolkitWrist).IsFalse();
        await Assert.That((action.ShowNotification.SteamvrOverlay, action.ShowNotification.Position, action.ShowNotification.Dark)).IsEqualTo((true, "bottomRight", true));
        await Assert.That(action.ShowNotification.Timeout).IsEqualTo(3);
        await Assert.That(action.ShowNotification.Image.IsEmpty).IsTrue();
        await pc.CompleteAsync(action);
        await Assert.That((await sent).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Export_to_calendar_sends_the_events_ics_to_the_pc()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        const string ics = "BEGIN:VCALENDAR\r\nEND:VCALENDAR";
        app.Vrchat.Json("calendar/grp_a/cal_b.ics", ics);
        await using var pc = await app.ConnectDesktopAsync("pc-calendar");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var opened = app.Api.PostAsync(app.AccountPath("calendar/grp_a/cal_b/open-on-pc"), null);
        var action = await pc.NextActionAsync();
        await Assert.That(action.OpenCalendarFile.Ics).IsEqualTo(ics);
        await pc.CompleteAsync(action);
        await Assert.That((await opened).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }
}

public sealed class AgentWristFeedTests
{
    [Test]
    public async Task The_wrist_feed_reaches_the_pc_with_at_most_25_rows()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-wrist");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));

        var rows = Enumerable.Range(0, 30).Select(i => new { time = "12:00", name = $"Bee{i}", text = "has logged in", friend = true, favorite = i == 0 });
        var sent = app.Api.PostAsJsonAsync(app.AccountPath("agent/wrist-feed"), new { rows, background = true, hideDevices = false, status = "12 (3)", locationSinceMs = 1_000L, cpuUsage = true, minimal = false, onlineFriends = 7, cpuLabel = "CPU:" });
        var action = await pc.NextActionAsync();
        await Assert.That(action.UpdateWristFeed.Rows.Count).IsEqualTo(25);
        await Assert.That((action.UpdateWristFeed.Rows[0].Name, action.UpdateWristFeed.Rows[0].Favorite)).IsEqualTo(("Bee0", true));
        await Assert.That((action.UpdateWristFeed.Background, action.UpdateWristFeed.Status)).IsEqualTo((true, "12 (3)"));
        await Assert.That((action.UpdateWristFeed.LocationSinceMs, action.UpdateWristFeed.CpuUsage, action.UpdateWristFeed.Minimal, action.UpdateWristFeed.OnlineFriends, action.UpdateWristFeed.CpuLabel))
            .IsEqualTo((1_000L, true, false, 7, "CPU:"));
        await pc.CompleteAsync(action);
        await Assert.That((await sent).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }
}

public sealed class DiscordPresenceTests
{
    private const string Here = "wrld_4cf554b4-430c-4f8f-b53e-1f294eed230b:12345~friends(usr_me)~region(eu)";

    private static PresenceInput Input(string location, string? status = "active", string? video = null) => new(
        VrchatLocation.Parse(location), DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), "The Black Cat", "https://img/thumb.png",
        40, true, null, 3, false, status, video, video is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_100_000), video is null ? 0 : 200);

    [Test]
    public async Task The_presence_is_the_originals()
    {
        var on = new AccountSettings { DiscordActive = true };
        var p = DiscordPresenceService.Build(Input(Here), on, DateTimeOffset.UtcNow);
        await Assert.That(p.Active).IsTrue();
        await Assert.That(p.Details).IsEqualTo("The Black Cat");
        await Assert.That(p.State).IsEqualTo("Friends #12345 (VR)");
        await Assert.That(p.LargeImageKey).IsEqualTo("https://img/thumb.png");
        await Assert.That(p.SmallImageKey).IsEqualTo("active");
        await Assert.That(p.DetailsUrl).IsEqualTo("https://vrchat.com/home/world/wrld_4cf554b4-430c-4f8f-b53e-1f294eed230b");
        await Assert.That((p.PartySize, p.PartyMax)).IsEqualTo((3, 40));
        await Assert.That(p.StartTime).IsEqualTo(1_700_000_000_000);
        await Assert.That(p.ButtonUrl).IsEqualTo("");
        await Assert.That(p.AppId).IsEqualTo(DiscordPresenceService.VrchatAppId);

        // Busy hides the instance.
        var busy = DiscordPresenceService.Build(Input(Here, "busy"), on, DateTimeOffset.UtcNow);
        await Assert.That(busy.Details).IsEqualTo("Private World");
        await Assert.That((busy.State, busy.PartyMax, busy.StartTime, busy.LargeImageKey)).IsEqualTo(("", 0, 0L, "vrchat"));

        // A public instance gets the Join button when asked, and a video in a dance world its app.
        const string pypy = "wrld_f20326da-f1ac-45fc-a062-609723b097b1:1";
        var dance = DiscordPresenceService.Build(Input(pypy, video: "Song"), on with { DiscordJoinButton = true }, DateTimeOffset.UtcNow);
        await Assert.That(dance.ButtonUrl).IsEqualTo("https://vrchat.com/home/launch?worldId=wrld_f20326da-f1ac-45fc-a062-609723b097b1&instanceId=1");
        await Assert.That((dance.Details, dance.AppId, dance.LargeImageKey, dance.ActivityType)).IsEqualTo(("Song", "784094509008551956", "pypy", 2));
        await Assert.That(dance.EndTime - dance.StartTime).IsEqualTo(200_000);

        await Assert.That(DiscordPresenceService.Build(Input(Here), new AccountSettings(), DateTimeOffset.UtcNow).Active).IsFalse();
        await Assert.That(DiscordPresenceService.LimitBytes(new string('é', 100), 127).Length).IsEqualTo(63);
    }

    [Test]
    public async Task Statuses_get_the_originals_names_and_icons()
    {
        await Assert.That(DiscordPresenceService.StatusInfo("active", true)).IsEqualTo(("Active", "active", false));
        await Assert.That(DiscordPresenceService.StatusInfo("join me", true)).IsEqualTo(("Join Me", "joinme", false));
        await Assert.That(DiscordPresenceService.StatusInfo("ask me", false)).IsEqualTo(("Ask Me", "askme", false));
        await Assert.That(DiscordPresenceService.StatusInfo("ask me", true)).IsEqualTo(("Ask Me", "askme", true));
        await Assert.That(DiscordPresenceService.StatusInfo("busy", false)).IsEqualTo(("Do Not Disturb", "busy", true));
        await Assert.That(DiscordPresenceService.StatusInfo("gone", false)).IsEqualTo(("Offline", "offline", true));
        await Assert.That(DiscordPresenceService.StatusInfo("", false)).IsEqualTo(("Offline", "offline", true));
    }

    [Test]
    public async Task Dance_and_cinema_worlds_have_their_own_apps()
    {
        foreach (var pypy in new[] { "wrld_f20326da-f1ac-45fc-a062-609723b097b1", "wrld_10e5e467-fc65-42ed-8957-f02cace1398c", "wrld_04899f23-e182-4a8d-b2c7-2c74c7c15534" })
            await Assert.That(DiscordPresenceService.RpcWorldOf(pypy)).IsEqualTo(new DiscordPresenceService.Rpc(2, 2, "784094509008551956", "pypy"));
        await Assert.That(DiscordPresenceService.RpcWorldOf("wrld_42377cf1-c54f-45ed-8996-5875b0573a83")!.BigIcon).IsEqualTo("vr_dancing");
        await Assert.That(DiscordPresenceService.RpcWorldOf("wrld_52bdcdab-11cd-4325-9655-0fb120846945")!.BigIcon).IsEqualTo("zuwa_zuwa_dance");
        foreach (var ls in new[] { "wrld_74970324-58e8-4239-a17b-2c59dfdf00db", "wrld_db9d878f-6e76-4776-8bf2-15bcdd7fc445", "wrld_435bbf25-f34f-4b8b-82c6-cd809057eb8e", "wrld_f767d1c8-b249-4ecc-a56f-614e433682c8" })
            await Assert.That(DiscordPresenceService.RpcWorldOf(ls)).IsEqualTo(new DiscordPresenceService.Rpc(3, 2, "968292722391785512", "ls_media"));
        await Assert.That(DiscordPresenceService.RpcWorldOf("wrld_266523e8-9161-40da-acd0-6bd82e075833")!.AppId).IsEqualTo("1095440531821170820");
        await Assert.That(DiscordPresenceService.RpcWorldOf("wrld_x")).IsNull();
        await Assert.That(DiscordPresenceService.RpcWorldOf("")).IsNull();
    }

    [Test]
    public async Task Only_a_real_instance_is_shown()
    {
        var on = new AccountSettings { DiscordActive = true };
        foreach (var location in new[] { "offline", "private", "", "traveling" })
            await Assert.That(DiscordPresenceService.Build(Input(location), on, DateTimeOffset.UtcNow).Active).IsFalse();
        var desktop = DiscordPresenceService.Build(Input(Here) with { GameNoVr = true }, on, DateTimeOffset.UtcNow);
        await Assert.That(desktop.State).IsEqualTo("Friends #12345 (Desktop)");
    }

    [Test]
    public async Task The_pc_shows_the_presence_while_playing_and_clears_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-discord");
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"));
        await app.Api.PutAsJsonAsync(app.AccountPath("settings"), new { discordActive = true, discordInstance = true, discordHideInvite = true, discordShowPlatform = true, discordWorldIntegration = true });
        await pc.NextAgentSettingsAsync();
        await pc.GameStateAsync(true);
        await pc.SendLogAsync(GameLog.Location(Here, "Club"));

        var action = await pc.NextActionAsync();
        while (!action.SetDiscordPresence.Active) action = await pc.NextActionAsync();
        await Assert.That(action.SetDiscordPresence.State).IsEqualTo("Friends #12345 (VR)");
        await Assert.That(action.SetDiscordPresence.PartyId).IsEqualTo("wrld_4cf554b4-430c-4f8f-b53e-1f294eed230b:12345");

        await pc.GameStateAsync(false);
        action = await pc.NextActionAsync();
        await Assert.That(action.SetDiscordPresence.Active).IsFalse();
    }
}
