using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services.Presence;
using VRCX.Common.Services.Sync;
using VRCX.Common.VrcxDb;

namespace VRCX.API.IntegrationTests.Tests;

// The user pop-up's action menu, with VRChat faked.
public sealed class UserActionsTests
{
    private const string Account = "usr_actions_account";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Actions","username":"actions"}""");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "actions", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    private async Task<VrcxContext> DbAsync() =>
        await Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();

    private async Task AddUserAsync(string id)
    {
        await using var db = await DbAsync();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new User { Id = id, DisplayName = id, FirstSeenAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task A_block_is_stored_and_undoing_it_removes_it()
    {
        const string target = "usr_actions_blocked";
        using var client = await LoginAsync();
        await AddUserAsync(target);
        Factory.Vrchat
            .Json("auth/user/playermoderations", $$"""{"id":"pmod_actions","type":"block","sourceUserId":"{{Account}}","targetUserId":"{{target}}"}""")
            .Json("auth/user/unplayermoderate", """{"success":{"message":"ok","status_code":200}}""");

        var sent = await client.PostAsync($"/1/accounts/{Account}/moderations/{target}/block", null);
        await Assert.That(sent.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await using (var db = await DbAsync())
            await Assert.That(await db.PlayerModerations.CountAsync(m => m.TargetUserId == target && m.Type == "block")).IsEqualTo(1);

        var removed = await client.DeleteAsync($"/1/accounts/{Account}/moderations/{target}/block");
        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await using (var db = await DbAsync())
            await Assert.That(await db.PlayerModerations.CountAsync(m => m.TargetUserId == target)).IsEqualTo(0);
    }

    [Test]
    public async Task The_friend_log_merges_friendships_requests_and_changes_and_hiding_keeps_the_history()
    {
        const string gone = "usr_log_gone", stays = "usr_log_stays", fresh = "usr_log_fresh", asked = "usr_log_asked";
        using var client = await LoginAsync();
        foreach (var id in new[] { gone, stays, fresh, asked }) await AddUserAsync(id);
        var start = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long goneFriendship;
        await using (var db = await DbAsync())
        {
            var ended = new Friendship { OwnerUserId = Account, FriendUserId = gone, FriendNumber = 1, StartedAt = start, EndedAt = start.AddDays(5), LastConfirmedAt = start };
            db.Friendships.AddRange(
                ended,
                new Friendship { OwnerUserId = Account, FriendUserId = stays, FriendNumber = 2, StartedAt = start, LastConfirmedAt = start },
                new Friendship { OwnerUserId = Account, FriendUserId = fresh, FriendNumber = 3, StartedAt = start.AddDays(10), LastConfirmedAt = start });
            db.UserDisplayNameChanges.AddRange(
                new UserDisplayNameChange { UserId = stays, ChangedAt = start.AddDays(1), PreviousDisplayName = "Old", DisplayName = "New" },
                new UserDisplayNameChange { UserId = gone, ChangedAt = start.AddDays(6), PreviousDisplayName = "After", DisplayName = "Unfriended" },
                new UserDisplayNameChange { UserId = fresh, ChangedAt = start.AddDays(11), PreviousDisplayName = "Then", DisplayName = "Now" });
            db.UserTrustChanges.AddRange(
                new UserTrustChange { UserId = stays, ChangedAt = start.AddDays(2), PreviousTrustLevel = "Known User", TrustLevel = "Trusted User" },
                new UserTrustChange { UserId = stays, ChangedAt = start.AddDays(3), PreviousTrustLevel = "Trusted User", TrustLevel = "Veteran User" });
            await db.SaveChangesAsync();
            goneFriendship = ended.Id;
        }
        Factory.Vrchat.Json($"user/{asked}/friendRequest", """{"success":{"message":"ok","status_code":200}}""");
        var request = await client.PostAsync($"/1/accounts/{Account}/friend-requests/{asked}", null);
        await Assert.That(request.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var log = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/friend-log");
        var mine = log.EnumerateArray().Where(e => e.GetProperty("userId").GetString()!.StartsWith("usr_log_")).ToArray();
        string[] Rows(string user) => mine.Where(e => e.GetProperty("userId").GetString() == user).Select(e => e.GetProperty("type").GetString()!).Order().ToArray();

        // Starting friends are no news; a name change after the friendship ended is not theirs to log.
        await Assert.That(Rows(gone)).IsEquivalentTo(new[] { "Unfriend" });
        // The trusted to veteran flip is ignored, as in the original.
        await Assert.That(Rows(stays)).IsEquivalentTo(new[] { "DisplayName", "TrustLevel" });
        await Assert.That(Rows(fresh)).IsEquivalentTo(new[] { "DisplayName", "Friend" });
        await Assert.That(Rows(asked)).IsEquivalentTo(new[] { "FriendRequest" });
        // A row shows the name they had at the time.
        var friendRow = mine.Single(e => e.GetProperty("type").GetString() == "Friend");
        await Assert.That(friendRow.GetProperty("displayName").GetString()).IsEqualTo("Then");

        var hide = await client.DeleteAsync($"/1/accounts/{Account}/friend-log/{Uri.EscapeDataString($"Unfriend:{goneFriendship}")}");
        await Assert.That(hide.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/friend-log");
        await Assert.That(after.EnumerateArray().Any(e => e.GetProperty("userId").GetString() == gone)).IsFalse();
        await using (var db = await DbAsync())
            await Assert.That(await db.Friendships.CountAsync(f => f.Id == goneFriendship && f.EndedAt != null)).IsEqualTo(1);
    }

    [Test]
    public async Task A_friend_missing_from_the_list_is_only_unfriended_once_vrchat_confirms()
    {
        const string missed = "usr_sync_missed", listedInIds = "usr_sync_inids", gone = "usr_sync_gone";
        using var client = await LoginAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var id in new[] { missed, listedInIds, gone }) await AddUserAsync(id);
        await using (var db = await DbAsync())
        {
            foreach (var id in new[] { missed, listedInIds, gone })
                db.Friendships.Add(new Friendship { OwnerUserId = Account, FriendUserId = id, FriendNumber = 900, StartedAt = now, LastConfirmedAt = now });
            await db.SaveChangesAsync();
        }
        Factory.Vrchat
            .Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Me","username":"me","friends":["{{listedInIds}}"]}""")
            .Json("auth/user/friends?offline=false&n=100&offset=0", "[]")
            .Json("auth/user/friends?offline=true&n=100&offset=0", "[]")
            .Json($"user/{missed}/friendStatus", """{"isFriend":true,"outgoingRequest":false,"incomingRequest":false}""")
            .Json($"user/{gone}/friendStatus", """{"isFriend":false,"outgoingRequest":false,"incomingRequest":false}""");

        var sync = Factory.Services.GetRequiredService<IAccountSyncService>();
        await sync.SyncFriendsAsync(Account, CancellationToken.None);

        await using (var db = await DbAsync())
        {
            var ended = await db.Friendships.Where(f => f.OwnerUserId == Account && f.EndedAt != null && f.FriendNumber == 900).Select(f => f.FriendUserId).ToListAsync();
            await Assert.That(ended).IsEquivalentTo(new[] { gone });
        }
    }

    [Test]
    public async Task Friend_details_count_game_log_joins_once_and_unfriending_ends_the_friendship()
    {
        const string friend = "usr_details_friend";
        using var client = await LoginAsync();
        await AddUserAsync(friend);
        var at = new DateTimeOffset(2002, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var db = await DbAsync())
        {
            db.Friendships.Add(new Friendship { OwnerUserId = Account, FriendUserId = friend, FriendNumber = 700, StartedAt = at, LastConfirmedAt = at });
            db.Worlds.Add(new World { Id = "wrld_details", Name = "w", AuthorId = friend, FirstSeenAt = at, UpdatedAt = at });
            db.Instances.Add(new Instance { Location = "wrld_details:1", WorldId = "wrld_details", InstanceId = "1", InstanceName = "1", Type = InstanceAccessType.Public, FirstSeenAt = at });
            db.InstanceVisits.AddRange(
                new InstanceVisit { UserId = friend, InstanceLocation = "wrld_details:1", JoinedAt = at, LeftAt = at.AddMinutes(10), Source = VisitSource.GameLog },
                new InstanceVisit { UserId = friend, InstanceLocation = "wrld_details:1", JoinedAt = at, LeftAt = at.AddMinutes(10), Source = VisitSource.GameLog },
                new InstanceVisit { UserId = friend, InstanceLocation = "wrld_details:1", JoinedAt = at.AddHours(1), LeftAt = at.AddHours(2), Source = VisitSource.Pipeline });
            db.UserMemos.Add(new UserMemo { UserId = friend, Memo = "a memo", UpdatedAt = at });
            await db.SaveChangesAsync();
        }

        var details = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/friends/details");
        var row = details.GetProperty("friends").EnumerateArray().Single(f => f.GetProperty("userId").GetString() == friend);
        await Assert.That(row.GetProperty("joinCount").GetInt32()).IsEqualTo(1);
        await Assert.That(row.GetProperty("timeTogetherMs").GetInt64()).IsEqualTo(600_000);
        await Assert.That(row.GetProperty("memo").GetString()).IsEqualTo("a memo");
        await Assert.That(row.GetProperty("needsLoad").GetBoolean()).IsTrue();

        Factory.Vrchat.Json($"auth/user/friends/{friend}", """{"success":{"message":"ok","status_code":200}}""");
        var removed = await client.DeleteAsync($"/1/accounts/{Account}/friends/{friend}");
        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/friends/details");
        await Assert.That(after.GetProperty("friends").EnumerateArray().Any(f => f.GetProperty("userId").GetString() == friend)).IsFalse();
    }

    [Test]
    public async Task The_game_log_lists_every_kind_scoped_to_the_account_and_hiding_keeps_the_row()
    {
        const string other = "usr_gamelog_other", mine = "wrld_gamelog:1", notMine = "wrld_gamelog:2";
        using var client = await LoginAsync();
        await AddUserAsync(other);
        var at = new DateTimeOffset(2003, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long videoId;
        await using (var db = await DbAsync())
        {
            db.Worlds.Add(new World { Id = "wrld_gamelog", Name = "Game Log World", AuthorId = other, FirstSeenAt = at, UpdatedAt = at });
            foreach (var location in new[] { mine, notMine })
                db.Instances.Add(new Instance { Location = location, WorldId = "wrld_gamelog", InstanceId = location[^1..], InstanceName = location[^1..], Type = InstanceAccessType.Public, FirstSeenAt = at });
            db.InstanceVisits.AddRange(
                new InstanceVisit { UserId = Account, InstanceLocation = mine, JoinedAt = at, LeftAt = at.AddHours(1), Source = VisitSource.GameLog },
                new InstanceVisit { UserId = other, InstanceLocation = mine, JoinedAt = at.AddMinutes(1), LeftAt = at.AddMinutes(2), Source = VisitSource.GameLog },
                new InstanceVisit { UserId = other, InstanceLocation = mine, JoinedAt = at.AddMinutes(1), LeftAt = at.AddMinutes(2), Source = VisitSource.GameLog },
                new InstanceVisit { UserId = other, InstanceLocation = notMine, JoinedAt = at, Source = VisitSource.GameLog });
            var video = new VideoPlay { At = at.AddMinutes(3), InstanceLocation = mine, VideoUrl = "https://example.test/gamelogvideo", VideoName = "A video" };
            db.VideoPlays.Add(video);
            db.ResourceLoads.Add(new ResourceLoad { At = at.AddMinutes(4), InstanceLocation = mine, Url = "https://example.test/gamelogimage", Kind = "image" });
            db.GameLogEvents.Add(new GameLogEvent { At = at.AddMinutes(5), InstanceLocation = mine, Kind = "Event", Data = "gamelog event text" });
            await db.SaveChangesAsync();
            videoId = video.Id;
        }

        var all = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/game-log?limit=5000");
        string[] Types(JsonElement rows) => rows.EnumerateArray().Where(r => r.TryGetProperty("createdAt", out var c) && c.GetDateTimeOffset().Year == 2003).Select(r => r.GetProperty("type").GetString()!).Order().ToArray();
        await Assert.That(Types(all)).IsEquivalentTo(new[] { "Event", "ImageLoad", "Location", "OnPlayerJoined", "OnPlayerLeft", "VideoPlay" });

        var searched = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/game-log?search=gamelog%20event&types=Event");
        await Assert.That(searched.EnumerateArray().Single().GetProperty("data").GetString()).IsEqualTo("gamelog event text");

        var sessions = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/game-log/sessions?limit=200");
        var stay = sessions.GetProperty("locations").EnumerateArray().Single(l => l.GetProperty("location").GetString() == mine);
        await Assert.That(stay.GetProperty("timeMs").GetInt64()).IsEqualTo(3_600_000);
        await Assert.That(sessions.GetProperty("events").EnumerateArray().Count(e => e.GetProperty("type").GetString() == "VideoPlay" && e.GetProperty("location").GetString() == mine)).IsEqualTo(1);

        var hide = await client.DeleteAsync($"/1/accounts/{Account}/game-log/{Uri.EscapeDataString($"VideoPlay:{videoId}")}");
        await Assert.That(hide.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/game-log?types=VideoPlay");
        await Assert.That(after.EnumerateArray().Any(r => r.GetProperty("key").GetString() == $"VideoPlay:{videoId}")).IsFalse();
        await using (var db = await DbAsync())
            await Assert.That(await db.VideoPlays.CountAsync(v => v.Id == videoId)).IsEqualTo(1);
    }

    [Test]
    public async Task Player_list_shows_the_game_log_stay_until_vrchat_says_the_account_moved()
    {
        const string other = "usr_player_other", onlyPresence = "usr_player_presence";
        using var client = await LoginAsync();
        await AddUserAsync(other);
        await AddUserAsync(onlyPresence);
        var at = new DateTimeOffset(2005, 1, 1, 0, 0, 0, TimeSpan.Zero);
        const string first = "wrld_player:1", second = "wrld_player:2";
        await using (var db = await DbAsync())
        {
            db.Worlds.Add(new World { Id = "wrld_player", Name = "Player World", FirstSeenAt = at });
            foreach (var (location, name) in new[] { (first, "1"), (second, "2") })
                db.Instances.Add(new Instance { Location = location, WorldId = "wrld_player", InstanceId = name, InstanceName = name, Type = InstanceAccessType.Public, FirstSeenAt = at });
            db.InstanceVisits.AddRange(
                new InstanceVisit { UserId = Account, InstanceLocation = first, JoinedAt = at, Source = VisitSource.GameLog },
                new InstanceVisit { UserId = other, InstanceLocation = first, JoinedAt = at, Source = VisitSource.GameLog },
                new InstanceVisit { UserId = onlyPresence, InstanceLocation = second, JoinedAt = at.AddHours(1), Source = VisitSource.Pipeline });
            await db.SaveChangesAsync();
        }

        var path = $"/1/accounts/{Account}/player-list";
        var page = await client.GetFromJsonAsync<JsonElement>(path);
        await Assert.That(page.GetProperty("location").GetString()).IsEqualTo(first);
        await Assert.That(page.GetProperty("world").GetProperty("name").GetString()).IsEqualTo("Player World");
        await Assert.That(page.GetProperty("players").GetArrayLength()).IsEqualTo(2);

        var presence = Factory.Services.GetRequiredService<IPresenceCache>();
        await presence.SetManyAsync([new PresenceWrite(Account, new UserPresence { CurrentInstanceLocation = second, LocationSince = at.AddHours(1) }, null)], CancellationToken.None);
        page = await client.GetFromJsonAsync<JsonElement>(path);
        await Assert.That(page.GetProperty("location").GetString()).IsEqualTo(second);
        await Assert.That(page.GetProperty("players")[0].GetProperty("userId").GetString()).IsEqualTo(onlyPresence);
    }

    [Test]
    public async Task Search_tabs_ask_vrchat_with_the_original_parameters_and_store_users()
    {
        using var client = await LoginAsync();
        Factory.Vrchat.Json("users", """[{"id":"usr_search_found","displayName":"Found","tags":["system_trust_known"]}]""");
        Factory.Vrchat.Json("worlds/recent", """[{"id":"wrld_recent","name":"Recent"}]""");
        Factory.Vrchat.Json("config", """{"dynamicWorldRows":[{"index":0,"name":"Featured","sortHeading":"featured"}]}""");
        Factory.Vrchat.Json("groups", """[{"id":"grp_found","name":"Group"}]""");
        Factory.Vrchat.Json("avatars", """{"results":[{"id":"avtr_market","name":"Market"}],"hasNext":true}""");
        Factory.Vrchat.Json("economy/store/shelves", """[{"id":"shelf","shelfBackgroundImageId":"file_bg","shelfMobileLogoImageId":"file_logo","listings":[]}]""");
        Factory.Vrchat.Json("economy/purchases", """[{"purchaseId":"p","listingId":"l","purchaseActive":true}]""");
        Factory.Vrchat.Json($"user/{Account}/economy/balance", """{"balance":120}""");
        Factory.Vrchat.Json("economy/purchase/listing", """{"ok":true}""");
        var root = $"/1/accounts/{Account}/search";

        var users = await client.GetFromJsonAsync<JsonElement>($"{root}/users?search=Fo&offset=60&sort=last_login");
        await Assert.That(users[0].GetProperty("id").GetString()).IsEqualTo("usr_search_found");
        await Assert.That(Factory.Vrchat.Calls).Contains("GET users?search=Fo&n=60&offset=60&customFields=displayName&sort=last_login");
        var worlds = await client.GetFromJsonAsync<JsonElement>($"{root}/worlds?option=recent&order=descending&tag=system_approved");
        await Assert.That(worlds[0].GetProperty("id").GetString()).IsEqualTo("wrld_recent");
        var rows = await client.GetFromJsonAsync<JsonElement>($"{root}/world-rows");
        await Assert.That(rows[0].GetProperty("name").GetString()).IsEqualTo("Featured");
        var groups = await client.GetFromJsonAsync<JsonElement>($"{root}/groups?query=G");
        await Assert.That(groups.GetArrayLength()).IsEqualTo(1);
        var market = await client.GetFromJsonAsync<JsonElement>($"{root}/marketplace?search=M");
        await Assert.That(market.GetProperty("hasNext").GetBoolean()).IsTrue();
        await Assert.That(Factory.Vrchat.Calls).Contains("GET avatars?n=50&offset=0&releaseStatus=public&order=ascending&marketplace=all&metadata=true&search=M");
        var store = await client.GetFromJsonAsync<JsonElement>($"{root}/store");
        await Assert.That(store.GetProperty("shelves").GetArrayLength()).IsEqualTo(3);
        // The shelf's image ids pass through, so the store page can pick its banner and logo.
        await Assert.That(store.GetProperty("shelves")[0].GetProperty("shelfBackgroundImageId").GetString()).IsEqualTo("file_bg");
        await Assert.That(store.GetProperty("shelves")[0].GetProperty("shelfMobileLogoImageId").GetString()).IsEqualTo("file_logo");
        await Assert.That(store.GetProperty("balance").GetInt32()).IsEqualTo(120);
        var bought = await client.PostAsJsonAsync($"{root}/store/purchase", new { listingId = "prod_l", totalPrice = 100, quantity = 1 });
        await Assert.That(bought.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(Factory.Vrchat.Calls).Contains("POST economy/purchase/listing?listingId=prod_l&totalPrice=100&quantity=1");
        var bad = await client.GetAsync($"{root}/avatars?provider=file:///etc/passwd&search=abc");
        await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        await using var db = await DbAsync();
        await Assert.That(await db.Users.AnyAsync(u => u.Id == "usr_search_found")).IsTrue();
    }

    [Test]
    public async Task Favorite_groups_are_renamed_and_emptied_on_vrchat_and_local_groups_live_while_empty()
    {
        using var client = await LoginAsync();
        Factory.Vrchat.Json($"favorite/group/friend/group_0/{Account}", """{"id":"fvgrp_0","name":"group_0","type":"friend","displayName":"Close","visibility":"friends"}""");
        Factory.Vrchat.Json("favorite/groups", "[]");
        Factory.Vrchat.Json("favorites", "[]");
        Factory.Vrchat.Json("avatars/favorites", "[]");
        Factory.Vrchat.Json("worlds/favorites", "[]");
        var root = $"/1/accounts/{Account}/favorites";

        var saved = await client.PutAsJsonAsync($"{root}/groups/friend/group_0", new { displayName = "Close", visibility = "friends" });
        await Assert.That(saved.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Bodies[$"PUT favorite/group/friend/group_0/{Account}"]).Contains("friends");
        var cleared = await client.DeleteAsync($"{root}/groups/friend/group_0");
        await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Calls).Contains($"DELETE favorite/group/friend/group_0/{Account}");

        await client.PostAsJsonAsync($"{root}/local/groups", new { type = "Friend", name = "Empty" });
        var groups = await client.GetFromJsonAsync<JsonElement>($"{root}/local/groups?type=Friend");
        await Assert.That(groups.EnumerateArray().Any(g => g.GetProperty("name").GetString() == "Empty" && g.GetProperty("count").GetInt32() == 0)).IsTrue();
        await client.PutAsJsonAsync($"{root}/local/groups", new { type = "Friend", name = "Empty", newName = "Renamed" });
        groups = await client.GetFromJsonAsync<JsonElement>($"{root}/local/groups?type=Friend");
        await Assert.That(groups.EnumerateArray().Select(g => g.GetProperty("name").GetString()).ToList()).Contains("Renamed");
        await client.DeleteAsync($"{root}/local/groups?type=Friend&name=Renamed");
        groups = await client.GetFromJsonAsync<JsonElement>($"{root}/local/groups?type=Friend");
        await Assert.That(groups.GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task Notification_actions_reach_vrchat_and_never_remove_a_row()
    {
        const string sender = "usr_notify_sender";
        using var client = await LoginAsync();
        await AddUserAsync(sender);
        var at = new DateTimeOffset(2004, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Notification Row(string id, string type, int version = 1, bool seen = true) => new() { Id = id, OwnerUserId = Account, Type = type, Version = version, SenderUserId = sender, Message = "m", CreatedAt = at, FetchedAt = at, Seen = seen };
        await using (var db = await DbAsync())
        {
            db.Notifications.AddRange(Row("not_hide", "invite"), Row("not_accept", "friendRequest"), Row("not_v2", "group.invite", 2), Row("not_response", "requestInvite"), Row("not_log", "boop"), Row("not_ignored", "ignoredFriendRequest"), Row("not_unseen", "message", 1, false), Row("not_single", "message", 2, false));
            await db.SaveChangesAsync();
        }
        var ok = """{"success":{"message":"ok","status_code":200}}""";
        foreach (var path in new[] { "auth/user/notifications/not_hide/hide", "auth/user/notifications/not_accept/accept", "notifications/not_v2/respond", "invite/not_response/response", "auth/user/notifications/not_response/hide", "auth/user/notifications/not_unseen/see", "notifications/not_single/see", $"user/{sender}/friendRequest" })
            Factory.Vrchat.Json(path, ok);

        async Task PostAsync(string path, object? body = null)
        {
            var response = await client.PostAsJsonAsync($"/1/accounts/{Account}/notifications/{path}", body ?? new { });
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
        await PostAsync("not_hide/hide");
        await PostAsync("not_accept/accept");
        await PostAsync("not_ignored/hide");
        await PostAsync("not_v2/respond", new { responseType = "accept", responseData = "d" });
        await PostAsync("not_response/invite-response", new { responseSlot = 3 });
        await PostAsync("not_single/see");
        await Assert.That(Factory.Vrchat.Calls).Contains("POST notifications/not_single/see");
        await PostAsync("see-all");
        var removed = await client.DeleteAsync($"/1/accounts/{Account}/notifications/not_log");
        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        await Assert.That(Factory.Vrchat.Bodies["POST notifications/not_v2/respond"]).Contains("accept");
        await Assert.That(Factory.Vrchat.Bodies["POST invite/not_response/response"]).Contains("3");
        await Assert.That(Factory.Vrchat.Calls).Contains($"DELETE user/{sender}/friendRequest");

        var page = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/notifications?includeHidden=true&pageSize=500");
        var shown = page.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("id").GetString()!).ToHashSet();
        await Assert.That(shown.Contains("not_hide")).IsTrue();
        await Assert.That(shown.Contains("not_accept")).IsFalse();
        await Assert.That(shown.Contains("not_log")).IsFalse();
        await using (var db = await DbAsync())
        {
            await Assert.That(await db.Notifications.CountAsync(n => n.SenderUserId == sender)).IsEqualTo(8);
            await Assert.That((await db.Notifications.FirstAsync(n => n.Id == "not_unseen")).Seen).IsTrue();
            await Assert.That((await db.Notifications.FirstAsync(n => n.Id == "not_v2")).ExpiresAt).IsNotNull();
            await Assert.That((await db.Notifications.FirstAsync(n => n.Id == "not_log")).LogHidden).IsTrue();
        }
    }

    [Test]
    public async Task Clearing_a_moderation_type_asks_vrchat_and_drops_only_that_type()
    {
        const string target = "usr_actions_clear";
        using var client = await LoginAsync();
        await AddUserAsync(target);
        await using (var db = await DbAsync())
        {
            db.PlayerModerations.AddRange(
                new global::VRCX.Common.VrcxDb.PlayerModeration { Id = "pmod_clear_1", OwnerUserId = Account, TargetUserId = target, Type = "unmute", FetchedAt = DateTimeOffset.UtcNow },
                new global::VRCX.Common.VrcxDb.PlayerModeration { Id = "pmod_clear_2", OwnerUserId = Account, TargetUserId = target, Type = "muteChat", FetchedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Factory.Vrchat.Json("auth/user/unplayermoderate", """{"success":{"message":"ok","status_code":200}}""");

        var cleared = await client.DeleteAsync($"/1/accounts/{Account}/moderations/unmute");
        await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Bodies["PUT auth/user/unplayermoderate"]).Contains("unmute");
        await using (var db = await DbAsync())
        {
            await Assert.That(await db.PlayerModerations.CountAsync(m => m.Id == "pmod_clear_1")).IsEqualTo(0);
            await Assert.That(await db.PlayerModerations.CountAsync(m => m.Id == "pmod_clear_2")).IsEqualTo(1);
        }

        var list = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/moderations");
        var row = list.EnumerateArray().Single(m => m.GetProperty("id").GetString() == "pmod_clear_2");
        await Assert.That(row.GetProperty("sourceUserId").GetString()).IsEqualTo(Account);
    }

    [Test]
    public async Task An_unknown_moderation_type_is_not_routed()
    {
        using var client = await LoginAsync();
        var response = await client.PostAsync($"/1/accounts/{Account}/moderations/usr_x/hideAvatar", null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Accepting_uses_the_stored_notification_and_hides_it()
    {
        const string sender = "usr_actions_sender";
        using var client = await LoginAsync();
        await AddUserAsync(sender);
        await using (var db = await DbAsync())
        {
            db.Notifications.Add(new Notification { Id = "not_actions", OwnerUserId = Account, Type = "friendRequest", SenderUserId = sender, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Factory.Vrchat.Json("auth/user/notifications/not_actions/accept", """{"success":{"message":"ok","status_code":200}}""");

        var response = await client.PostAsync($"/1/accounts/{Account}/friend-requests/{sender}/accept", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Calls).Contains("PUT auth/user/notifications/not_actions/accept");
        await using (var db = await DbAsync())
            await Assert.That((await db.Notifications.SingleAsync(n => n.Id == "not_actions")).Hidden).IsTrue();
    }

    [Test]
    public async Task Boop_and_request_invite_reach_vrchat()
    {
        const string target = "usr_actions_boop";
        using var client = await LoginAsync();
        Factory.Vrchat
            .Json($"users/{target}/boop", """{"success":{"message":"ok","status_code":200}}""")
            .Json($"requestInvite/{target}", """{"id":"not_sent","type":"requestInvite"}""");

        var boop = await client.PostAsJsonAsync($"/1/accounts/{Account}/users/{target}/boop", new { emojiId = "default_wave" });
        var invite = await client.PostAsync($"/1/accounts/{Account}/invite-requests/{target}", null);

        await Assert.That(boop.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(invite.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Booping_back_hides_that_users_open_boops()
    {
        const string target = "usr_actions_boopback";
        using var client = await LoginAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        Notification Boop(string id, string link, DateTimeOffset? expires = null) =>
            new() { Id = id, OwnerUserId = Account, Type = "boop", Version = 2, Link = link, Title = "Booped you", CreatedAt = at, FetchedAt = at, ExpiresAt = expires };
        await using (var db = await DbAsync())
        {
            db.Notifications.AddRange(
                Boop("not_boop_open", $"user:{target}", DateTimeOffset.UtcNow.AddHours(1)),
                Boop("not_boop_expired", $"user:{target}", DateTimeOffset.UtcNow.AddMinutes(-1)),
                Boop("not_boop_other", "user:usr_someone_else"));
            await db.SaveChangesAsync();
        }
        var ok = """{"success":{"message":"ok","status_code":200}}""";
        Factory.Vrchat.Json($"users/{target}/boop", ok).Json("notifications/not_boop_open", ok);

        var boop = await client.PostAsJsonAsync($"/1/accounts/{Account}/users/{target}/boop", new { emojiId = (string?)null });

        await Assert.That(boop.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Calls).Contains("DELETE notifications/not_boop_open");
        await Assert.That(Factory.Vrchat.Calls).DoesNotContain("DELETE notifications/not_boop_expired");
        await Assert.That(Factory.Vrchat.Calls).DoesNotContain("DELETE notifications/not_boop_other");
        await using var check = await DbAsync();
        var hidden = await check.Notifications.Where(n => n.OwnerUserId == Account && n.Type == "boop").ToDictionaryAsync(n => n.Id, n => n.Hidden);
        await Assert.That(hidden["not_boop_open"]).IsTrue();
        await Assert.That(hidden["not_boop_expired"]).IsFalse();
        await Assert.That(hidden["not_boop_other"]).IsFalse();
    }

    [Test]
    public async Task The_relationship_has_live_friend_state_and_the_stored_note()
    {
        const string target = "usr_actions_status";
        using var client = await LoginAsync();
        await AddUserAsync(target);
        await using (var db = await DbAsync())
        {
            db.UserNotes.Add(new UserNote { Id = "unt_actions", OwnerUserId = Account, TargetUserId = target, Note = "hello", FetchedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Factory.Vrchat.Json($"user/{target}/friendStatus", """{"isFriend":false,"incomingRequest":true,"outgoingRequest":false}""");

        var relationship = await client.GetFromJsonAsync<JsonElement>($"/1/accounts/{Account}/users/{target}/relationship");

        await Assert.That(relationship.GetProperty("incomingRequest").GetBoolean()).IsTrue();
        await Assert.That(relationship.GetProperty("note").GetString()).IsEqualTo("hello");
    }
}
