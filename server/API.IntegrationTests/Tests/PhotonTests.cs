using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Live;
using VRCX.Common.Services.Live.Photon;
using VRCX.Common.VrcxDb;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// The Photon logger's stream on the pipe (the original's ipcEvent Photon cases and photon store).
public sealed class PhotonTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-10T20:00:00Z");
    private static readonly PhotonSettings NoBlacklist = new(new Dictionary<string, string>(), PhotonLobby.DefaultChatboxBlacklist);

    private static string Dt(int seconds) => T0.AddSeconds(seconds).ToString("O");

    private static string User(string id, string name, string avatar = "avtr_a", bool vr = true, string extra = "") =>
        $$"""{"user":{"id":"{{id}}","displayName":"{{name}}","status":"active","statusDescription":""},"avatarDict":{"id":"{{avatar}}","name":"Avatar {{avatar}}","authorId":"usr_author","releaseStatus":"public"},"inVRMode":{{(vr ? "true" : "false")}},"canModerateInstance":false,"groupOnNameplate":""{{extra}}}""";

    private static JsonElement Event(int code, string parameters, int seconds) =>
        JsonSerializer.Deserialize<JsonElement>("{\"type\":\"OnEvent\",\"OnEventData\":{\"Code\":" + code + ",\"Parameters\":{" + parameters + "}},\"dt\":\"" + Dt(seconds) + "\"}");

    private static JsonElement Joined(int seconds) => JsonSerializer.Deserialize<JsonElement>(
        $$$"""{"type":"OnOperationResponse","OnOperationResponseData":{"OperationCode":226,"Parameters":{"248":{"248":1},"254":1,"249":{"1":{{{User("usr_me", "Me")}}},"2":{{{User("usr_ann", "Ann")}}}},"252":[1,2]}},"dt":"{{{Dt(seconds)}}}"}""");

    private static PhotonLobby InInstance()
    {
        var lobby = new PhotonLobby();
        lobby.Reset("wrld_1:1");
        lobby.Apply(Joined(0), NoBlacklist, T0);
        return lobby;
    }

    [Test]
    public async Task Joining_reads_the_lobby_and_others_joining_and_leaving_are_logged()
    {
        var lobby = InInstance();
        await Assert.That((lobby.Master, lobby.CurrentUser)).IsEqualTo((1, 1));
        await Assert.That(lobby.DisplayNameOf(2)).IsEqualTo("Ann");
        await Assert.That(lobby.Events).IsEmpty();

        lobby.Apply(Event(255, $$"""  "254":3, "249":{{User("usr_bob", "Bob", vr: false, extra: ",\"modTag\":\"mod\"")}}, "252":[1,2,3]  """, 5), NoBlacklist, T0.AddSeconds(5));
        var joined = lobby.Events[0];
        await Assert.That((joined.Type, joined.Text, joined.DisplayName, joined.UserId, joined.Platform)).IsEqualTo(("OnPlayerJoined", "has joined", "Bob", "usr_bob", "Desktop"));
        await Assert.That(lobby.Events[1].Text).IsEqualTo("Moderator has joined mod");

        lobby.Apply(Event(254, """ "254":3, "252":[1,2] """, 20), NoBlacklist, T0.AddSeconds(20));
        await Assert.That((lobby.Events[0].Type, lobby.Events[0].Text)).IsEqualTo(("OnPlayerLeft", "has left"));
        await Assert.That(lobby.Current.ContainsKey(3)).IsFalse();

        lobby.Apply(Event(4, """ "254":2 """, 21), NoBlacklist, T0.AddSeconds(21));
        await Assert.That((lobby.Master, lobby.Events[0].Type, lobby.Events[0].IsMaster)).IsEqualTo((2, "MasterMigrate", false));

        // Another instance starts over and keeps the last table as the previous one.
        lobby.Reset("wrld_2:2");
        await Assert.That(lobby.Events).IsEmpty();
        await Assert.That(lobby.PreviousEvents.Count).IsEqualTo(4);
    }

    [Test]
    public async Task Chatbox_messages_skip_repeats_blacklists_and_your_own()
    {
        var lobby = InInstance();
        var effects = lobby.Apply(Event(43, """ "254":2, "245":"hello" """, 1), NoBlacklist, T0);
        await Assert.That(effects.OfType<PhotonNoty>().Single().Text).IsEqualTo("hello");
        await Assert.That(lobby.Apply(Event(43, """ "254":2, "245":"hello" """, 2), NoBlacklist, T0)).IsEmpty();
        await Assert.That(lobby.Apply(Event(43, """ "254":2, "245":"Now Playing: x" """, 3), NoBlacklist, T0)).IsEmpty();
        await Assert.That(lobby.Apply(Event(43, """ "254":1, "245":"mine" """, 4), NoBlacklist, T0)).IsEmpty();
        var muted = new PhotonSettings(new Dictionary<string, string> { ["usr_ann"] = "Ann" }, []);
        await Assert.That(lobby.Apply(Event(43, """ "254":2, "245":"other" """, 5), muted, T0)).IsEmpty();
        await Assert.That(lobby.Events.Count(e => e.Type == "ChatBoxMessage")).IsEqualTo(1);
    }

    [Test]
    public async Task Moderations_wait_for_the_player_to_be_known()
    {
        var lobby = InInstance();
        var effects = lobby.Apply(Event(33, """ "245":{"0":21,"1":2,"10":true,"11":false} """, 1), NoBlacklist, T0);
        await Assert.That(effects.OfType<PhotonModeration>().Single()).IsEqualTo(new PhotonModeration(2, "usr_ann", "Ann", true, false, T0.AddSeconds(1)));

        // An unknown player's moderation waits until their user arrives.
        await Assert.That(lobby.Apply(Event(33, """ "245":{"0":21,"10":[5],"11":[5,6]} """, 2), NoBlacklist, T0).OfType<PhotonModeration>()).IsEmpty();
        effects = lobby.Apply(Event(42, $$""" "254":5, "245":{{User("usr_cat", "Cat")}} """, 3), NoBlacklist, T0);
        await Assert.That(effects.OfType<PhotonModeration>().Single()).IsEqualTo(new PhotonModeration(5, "usr_cat", "Cat", true, true, T0.AddSeconds(2)));
    }

    [Test]
    public async Task Portals_avatars_groups_rpcs_and_emoji()
    {
        var lobby = InInstance();
        var spawn = lobby.Apply(Event(70, """ "245":[20,"p1","usr_ann",0,0,"abc",0,0,{"name":"Club"}] """, 1), NoBlacklist, T0).OfType<PhotonPortalSpawn>().Single();
        await Assert.That((spawn.UserId, spawn.ShortName, spawn.WorldName)).IsEqualTo(("usr_ann", "abc", "Club"));
        lobby.Apply(Event(70, """ "245":[23,"p1",0,4] """, 2), NoBlacklist, T0);
        lobby.Apply(Event(254, """ "254":2, "252":[1] """, 3), NoBlacklist, T0.AddSeconds(3));
        await Assert.That(lobby.Events[0].Text).IsEqualTo("has left through portal to \"Club\"");
        lobby.Apply(Event(70, """ "245":[22,"p1"] """, 61), NoBlacklist, T0);
        await Assert.That(lobby.Events[0].Text).IsEqualTo("DeletedPortal after 1m with 4 players to \"Club\"");

        lobby = InInstance();
        var effects = lobby.Apply(Event(42, $$""" "254":2, "245":{{User("usr_ann", "Ann", avatar: "avtr_b", extra: "").Replace("\"groupOnNameplate\":\"\"", "\"groupOnNameplate\":\"grp_x\"")}} """, 4), NoBlacklist, T0);
        await Assert.That(effects.OfType<PhotonNoty>().Single()).IsEqualTo(new PhotonNoty("AvatarChange", T0.AddSeconds(4), "usr_ann", "Ann", "Avatar avtr_b", effects.OfType<PhotonNoty>().Single().Avatar));
        await Assert.That(lobby.Events[0].Text).IsEqualTo("ChangeAvatar Avatar avtr_b");
        var group = effects.OfType<PhotonGroupChange>().Single().Entry;
        await Assert.That((group.GroupId, group.PreviousGroupId)).IsEqualTo(("grp_x", ""));
        await Assert.That(effects.OfType<PhotonAvatarSeen>().Count()).IsEqualTo(1);

        JsonElement Vrc(string data) => JsonSerializer.Deserialize<JsonElement>("{\"type\":\"VRCEvent\",\"VRCEventData\":{" + data + "},\"dt\":\"" + Dt(5) + "\"}");
        lobby.Apply(Vrc(""" "Sender":2,"EventType":14,"EventName":"ChangeVisibility","Data":[true] """), NoBlacklist, T0);
        await Assert.That((lobby.Events[0].Type, lobby.Events[0].Text)).IsEqualTo(("Camera", "EnableCamera"));
        lobby.Apply(Vrc(""" "Sender":2,"EventType":14,"EventName":"Wave","Data":[{"a":1}] """), NoBlacklist, T0);
        await Assert.That(lobby.Events[0].Text).IsEqualTo("Wave [{a:1}]");
        var rpc = lobby.Apply(Vrc(""" "Sender":2,"EventType":40,"EventName":"X" """), NoBlacklist, T0).OfType<PhotonGameLogEvent>().Single();
        await Assert.That(rpc.Data).IsEqualTo("Ann called non existent RPC 40");

        lobby.Apply(Event(71, """ "254":2, "245":[0,0,15] """, 6), NoBlacklist, T0);
        await Assert.That((lobby.Events[0].Type, lobby.Events[0].Text)).IsEqualTo(("SpawnEmoji", "Thinking"));
        lobby.Apply(Event(71, """ "254":2, "245":[1,"file_e"] """, 7), NoBlacklist, T0);
        await Assert.That(lobby.Events[0].ImageUrl).IsEqualTo("https://api.vrchat.cloud/api/1/file/file_e/1/");
    }

    [Test]
    public async Task Players_still_for_too_long_time_out()
    {
        var lobby = InInstance();
        var now = T0.AddSeconds(100);
        var list = JsonSerializer.Deserialize<JsonElement>($$"""{"type":"Event7List","Event7List":{"1":"{{now.ToString("O")}}","2":"{{now.AddSeconds(-8).ToString("O")}}"},"dt":"{{now.ToString("O")}}"}""");
        lobby.Apply(list, NoBlacklist, now);
        var timeouts = lobby.Timeouts(now.AddSeconds(1), 6000, 2);
        await Assert.That(timeouts.Single()).IsEqualTo(new PhotonTimeout("usr_ann", "Ann", 9, 9000));
        // The list is old, or only you are there: nobody.
        await Assert.That(lobby.Timeouts(now.AddSeconds(2), 6000, 2)).IsEmpty();
        await Assert.That(lobby.Timeouts(now.AddSeconds(1), 6000, 1)).IsEmpty();
        await Assert.That(PhotonLobby.TimeToText(9_000)).IsEqualTo("10s");
        await Assert.That(PhotonLobby.TimeToText(3_725_000)).IsEqualTo("1h 2m");
    }

    [Test]
    public async Task The_pipes_photon_stream_fills_the_lobby_and_records_moderations()
    {
        PhotonService.PublishDelay = TimeSpan.FromMilliseconds(20);
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var live = await app.ConnectLiveAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-photon");
        const string here = "wrld_4cf554b4-430c-4f8f-b53e-1f294eed230b:1";
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"), GameLog.Location(here, "Club"));

        // Closed game: the stream is dropped.
        await pc.SendIpcAsync(Ipc.Photon(Joined(0).GetRawText()));
        await pc.GameStateAsync(true);
        await pc.SendIpcAsync(Ipc.Photon(Joined(0).GetRawText()));
        await live.WaitForAsync<PhotonUpdated>(p => p.AccountId == app.AccountId);
        var lobby = await app.Api.GetFromJsonAsync<JsonElement>(app.AccountPath("photon"));
        await Assert.That(lobby.GetProperty("enabled").GetBoolean()).IsTrue();
        await Assert.That(lobby.GetProperty("players").GetArrayLength()).IsEqualTo(2);

        await pc.SendIpcAsync(Ipc.Photon(Event(33, """ "245":{"0":21,"1":2,"10":true,"11":false} """, 1).GetRawText()));
        var update = await live.WaitForAsync<PhotonUpdated>(p => p.NotysJson.Contains("Blocked"));
        await Assert.That(update.NotysJson).Contains("usr_ann");
        await using var db = await app.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();
        var row = await db.ModerationsAgainst.SingleAsync(r => r.OwnerUserId == app.AccountId);
        await Assert.That((row.UserId, row.Block, row.Mute)).IsEqualTo(("usr_ann", true, false));

        // Said again: nothing new to tell.
        await pc.SendIpcAsync(Ipc.Photon(Event(33, """ "245":{"0":21,"1":2,"10":true,"11":false} """, 2).GetRawText()));
        await pc.SendIpcAsync(Ipc.Photon(Event(33, """ "245":{"0":21,"1":2,"10":false,"11":false} """, 3).GetRawText()));
        update = await live.WaitForAsync<PhotonUpdated>(p => p.NotysJson.Contains("Unblocked"));
        await Assert.That(update.NotysJson).DoesNotContain("\"Blocked\"");
        await Assert.That(await db.ModerationsAgainst.CountAsync(r => r.OwnerUserId == app.AccountId)).IsEqualTo(0);
    }

    [Test]
    public async Task Hud_lines_read_as_the_original_drew_them()
    {
        PhotonFeedEntry Entry(string type, string? text = null) => new() { Type = type, Text = text, DisplayName = "Ann", UserId = "usr_ann" };
        var avatar = new PhotonAvatar("avtr_b", "Fox", null, null, null, "private", null, null);
        await Assert.That(PhotonHud.Line(new PhotonFeedEntry { Type = "ChangeAvatar", Avatar = avatar }).Text).IsEqualTo("ChangeAvatar Fox (Private)");
        await Assert.That(PhotonHud.Line(new PhotonFeedEntry { Type = "OnPlayerJoined", Platform = "Quest", Avatar = avatar }).Text).IsEqualTo("has joined Android Fox");
        await Assert.That(PhotonHud.Line(new PhotonFeedEntry { Type = "ChangeStatus", Status = "busy", PreviousStatus = "active", StatusDescription = "afk", PreviousStatusDescription = "afk" }).Text).IsEqualTo("ChangeStatus active → busy");
        await Assert.That(PhotonHud.Line(new PhotonFeedEntry { Type = "Moderation", Text = "Blocked", Color = "yellow" })).IsEqualTo(("Blocked", "yellow"));
        await Assert.That(PhotonHud.Line(Entry("SpawnEmoji", "Thinking")).Text).IsEqualTo("SpawnEmoji Thinking");
        await Assert.That(PhotonHud.Passes("VIP", "usr_ann", true, false)).IsFalse();
        await Assert.That(PhotonHud.Passes("Friends", "", true, true)).IsFalse();
        await Assert.That(PhotonHud.Passes("Everyone", "", false, false)).IsTrue();
        await Assert.That(PhotonHud.TypeShown(["ChatBoxMessage"], "SpawnEmoji")).IsFalse();
    }

    [Test]
    public async Task The_event_and_timeout_huds_reach_the_pcs_steamvr_overlay()
    {
        PhotonService.PublishDelay = TimeSpan.FromMilliseconds(20);
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-hud");
        const string here = "wrld_4cf554b4-430c-4f8f-b53e-1f294eed230b:1";
        await pc.SendLogAsync(GameLog.UserAuthenticated(app.AccountId!, "Me"), GameLog.Location(here, "Club"));
        await pc.GameStateAsync(true);

        var settings = await app.GetJsonAsync(app.AccountPath("settings"));
        var body = System.Text.Json.Nodes.JsonNode.Parse(settings.GetRawText())!.AsObject();
        body["openVr"] = true;
        body["photonEventOverlay"] = true;
        body["photonEventTypeOverlayFilter"] = new System.Text.Json.Nodes.JsonArray("SpawnEmoji");
        body["timeoutHudOverlay"] = true;
        body["photonOverlayMessageTimeout"] = 4000;
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync(app.AccountPath("settings"), body), "save settings");
        await pc.NextAgentSettingsAsync();
        await using (var db = await app.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync())
        {
            await app.Services.GetRequiredService<VRCX.Common.Services.Ingestion.VrchatStore>().EnsureUserAsync(db, "usr_ann", "Ann", DateTimeOffset.UtcNow, default);
            db.CustomUserTags.Add(new CustomUserTag { OwnerUserId = app.AccountId!, UserId = "usr_ann", Tag = "Pal", Colour = "#ff0000" });
            await db.SaveChangesAsync();
        }

        await pc.SendIpcAsync(Ipc.Photon(Joined(0).GetRawText()));
        await pc.SendIpcAsync(Ipc.Photon(Event(71, """ "254":2, "245":[0,0,15] """, 6).GetRawText()));
        var hud = await NextHudAsync(pc, h => h.Added.Count > 0);
        await Assert.That(hud.Added.Select(a => (a.DisplayName, a.Text))).IsEquivalentTo([("Ann", "SpawnEmoji Thinking")]);
        await Assert.That(hud.MessageTimeoutMs).IsEqualTo(4000);
        await Assert.That(hud.Added[0].Colour).IsEqualTo("#ff0000");

        // Ann has been still for 8 s, past the 6 s threshold.
        var now = DateTimeOffset.UtcNow;
        var list = $$"""{"type":"Event7List","Event7List":{"1":"{{now.ToString("O")}}","2":"{{now.AddSeconds(-8).ToString("O")}}"},"dt":"{{now.ToString("O")}}"}""";
        await pc.SendIpcAsync(Ipc.Photon(list));
        hud = await NextHudAsync(pc, h => h.TimeoutsSet);
        await Assert.That(hud.Timeouts.Select(t => t.DisplayName)).IsEquivalentTo(["Ann"]);
    }

    private static async Task<HudFeed> NextHudAsync(FakeDesktop pc, Func<HudFeed, bool> match)
    {
        while (true)
        {
            var action = await pc.NextActionAsync();
            if (action.ActionCase == ClientAction.ActionOneofCase.UpdateHud && match(action.UpdateHud)) return action.UpdateHud;
        }
    }
}
