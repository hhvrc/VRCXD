using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// What the original kept in its own database (services/database): local favorites, avatar tags and
// time worn, memos, the friend log, the mutual graph and stored notifications. The server owns
// these now; the tests read them back the way the pages do.
public sealed class StoredDataTests
{
    private static async Task<JsonElement[]> ArrayAsync(VrcxScenario app, string path) =>
        (await app.GetJsonAsync(path)).EnumerateArray().ToArray();

    [Test]
    public async Task Local_friend_favorites_list_with_their_group_and_a_rename_moves_them_to_the_new_name()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_fav_a", "Fav A");
        app.Vrchat.Friend("usr_fav_b", "Fav B");
        await app.LoginAsync();

        foreach (var id in new[] { "usr_fav_a", "usr_fav_b" })
            await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsJsonAsync(app.AccountPath("favorites/local"), new { type = "friend", targetId = id, group = "Close" }), "add");
        var listed = await ArrayAsync(app, app.AccountPath("favorites/local?type=friend"));
        await Assert.That(listed.Select(f => (f.GetProperty("targetId").GetString()!, f.GetProperty("groupName").GetString()!))).IsEquivalentTo(new[] { ("usr_fav_a", "Close"), ("usr_fav_b", "Close") });

        await VrcxScenario.EnsureSuccessAsync(await app.Api.DeleteAsync(app.AccountPath("favorites/local?type=friend&targetId=usr_fav_b&group=Close")), "remove");
        // The rename takes the old name and the new one; the favorites move with it.
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync(app.AccountPath("favorites/local/groups"), new { type = "friend", name = "Close", newName = "Closest" }), "rename");

        listed = await ArrayAsync(app, app.AccountPath("favorites/local?type=friend"));
        await Assert.That(listed.Select(f => (f.GetProperty("targetId").GetString()!, f.GetProperty("groupName").GetString()!))).IsEquivalentTo(new[] { ("usr_fav_a", "Closest") });
        var groups = await ArrayAsync(app, app.AccountPath("favorites/local/groups?type=friend"));
        await Assert.That(groups.Select(g => g.GetProperty("name").GetString()!)).IsEquivalentTo(new[] { "Closest" });
    }

    [Test]
    public async Task Deleting_a_local_world_group_removes_it_and_what_it_held()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsJsonAsync(app.AccountPath("favorites/local"), new { type = "world", targetId = "wrld_trip", group = "Trips" }), "add trip");
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsJsonAsync(app.AccountPath("favorites/local"), new { type = "world", targetId = "wrld_home", group = "Keep" }), "add keep");

        await VrcxScenario.EnsureSuccessAsync(await app.Api.DeleteAsync(app.AccountPath("favorites/local/groups?type=world&name=Trips")), "delete group");

        var listed = await ArrayAsync(app, app.AccountPath("favorites/local?type=world"));
        await Assert.That(listed.Select(f => f.GetProperty("targetId").GetString()!)).IsEquivalentTo(new[] { "wrld_home" });
        var groups = await ArrayAsync(app, app.AccountPath("favorites/local/groups?type=world"));
        await Assert.That(groups.Select(g => g.GetProperty("name").GetString()!)).IsEquivalentTo(new[] { "Keep" });
    }

    [Test]
    public async Task Avatar_tags_keep_their_colour_or_none_and_replacing_them_with_none_removes_them()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        await using (var db = app.CreateDbContext())
        {
            db.Avatars.Add(new Avatar { Id = "avtr_tagged", Name = "Tagged", FirstSeenAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var put = await app.Api.PutAsJsonAsync(app.AccountPath("avatars/avtr_tagged/tags"), new object[] { new { tag = "wip", color = (string?)null }, new { tag = "dance", color = "blue" } });
        await VrcxScenario.EnsureSuccessAsync(put, "put tags");
        var tags = (await put.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        await Assert.That(tags.Select(t => t.GetProperty("tag").GetString()!)).IsEquivalentTo(new[] { "dance", "wip" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(tags[0].GetProperty("color").GetString()).IsEqualTo("blue");
        // No colour is left out of the JSON, which the page reads as none.
        await Assert.That(tags[1].TryGetProperty("color", out var none) && none.ValueKind != JsonValueKind.Null).IsFalse();

        var cleared = await app.Api.PutAsJsonAsync(app.AccountPath("avatars/avtr_tagged/tags"), Array.Empty<object>());
        await Assert.That((await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task Own_avatars_carry_their_tags_and_the_time_worn_with_zero_for_one_never_worn()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_tagger", "Tagger");
        app.Vrchat.Avatar("avtr_worn", "Worn", a => a.Author(me.Id, me.DisplayName));
        app.Vrchat.Avatar("avtr_new", "New", a => a.Author(me.Id, me.DisplayName));
        await app.LoginAsync();
        (await app.Api.GetAsync(app.AccountPath("avatars"))).EnsureSuccessStatusCode();
        await using (var db = app.CreateDbContext())
        {
            db.AvatarUsages.Add(new AvatarUsage { OwnerUserId = me.Id, AvatarId = "avtr_worn", LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1), TimeSpent = TimeSpan.FromSeconds(90) });
            await db.SaveChangesAsync();
        }
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PutAsJsonAsync(app.AccountPath("avatars/avtr_worn/tags"), new[] { new { tag = "main", color = "red" } }), "put tags");

        var own = (await ArrayAsync(app, app.AccountPath("avatars"))).ToDictionary(a => a.GetProperty("avatar").GetProperty("id").GetString()!);

        await Assert.That(own["avtr_worn"].GetProperty("timeSpentMs").GetInt64()).IsEqualTo(90_000L);
        await Assert.That(own["avtr_new"].GetProperty("timeSpentMs").GetInt64()).IsEqualTo(0L);
        await Assert.That(own["avtr_worn"].GetProperty("tags")[0].GetProperty("color").GetString()).IsEqualTo("red");
        await Assert.That(own["avtr_new"].GetProperty("tags").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task A_user_memo_is_kept_read_back_and_removed()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_memo", "Memo");
        await app.LoginAsync();

        await Assert.That((await app.Api.GetAsync("/1/users/usr_memo/memo")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var put = await app.Api.PutAsJsonAsync("/1/users/usr_memo/memo", new { memo = "met at the meetup" });
        await VrcxScenario.EnsureSuccessAsync(put, "put memo");

        var read = await app.GetJsonAsync("/1/users/usr_memo/memo");
        await Assert.That(read.GetProperty("userId").GetString()!).IsEqualTo("usr_memo");
        await Assert.That(read.GetProperty("memo").GetString()).IsEqualTo("met at the meetup");
        await Assert.That(read.GetProperty("updatedAt").ValueKind).IsEqualTo(JsonValueKind.String);

        await VrcxScenario.EnsureSuccessAsync(await app.Api.DeleteAsync("/1/users/usr_memo/memo"), "delete memo");
        await Assert.That((await app.Api.GetAsync("/1/users/usr_memo/memo")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task The_friend_log_is_newest_first_and_friends_carry_their_friend_number()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_first_a", "First A");
        app.Vrchat.Friend("usr_first_b", "First B");
        await app.LoginAsync();

        await app.Pipeline.FriendAddAsync(app.Vrchat.User("usr_carol", "Carol"));
        app.Time.Advance(TimeSpan.FromMinutes(5));
        await app.Pipeline.FriendAddAsync(app.Vrchat.User("usr_dave", "Dave"));

        var log = (await ArrayAsync(app, app.AccountPath("friend-log"))).Where(e => e.GetProperty("type").GetString() == "Friend").ToArray();
        await Assert.That(log.Select(e => e.GetProperty("userId").GetString()!)).IsEquivalentTo(new[] { "usr_dave", "usr_carol" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var numbers = (await ArrayAsync(app, app.AccountPath("friends"))).ToDictionary(f => f.GetProperty("userId").GetString()!, f => f.GetProperty("friendNumber").GetInt32());
        // The two at the first sync are 1 and 2; later friends count on from there.
        await Assert.That(new[] { numbers["usr_first_a"], numbers["usr_first_b"] }).IsEquivalentTo(new[] { 1, 2 });
        await Assert.That((numbers["usr_carol"], numbers["usr_dave"])).IsEqualTo((3, 4));
        await Assert.That(log[0].GetProperty("friendNumber").GetInt32()).IsEqualTo(4);
    }

    [Test]
    public async Task The_mutual_graph_lists_every_friend_with_the_links_fetched_and_none_for_the_rest()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_mg_alice", "Alice");
        app.Vrchat.Friend("usr_mg_bob", "Bob");
        app.Vrchat.Friend("usr_mg_carol", "Carol");
        await app.LoginAsync();
        app.Vrchat
            .Json("users/usr_mg_alice/mutuals/friends?n=100&offset=0", """[{"id":"usr_mg_bob","displayName":"Bob"}]""")
            .Status("users/usr_mg_carol/mutuals/friends?n=100&offset=0", HttpStatusCode.Forbidden, """{"error":{"message":"Forbidden","status_code":403}}""");

        var alice = await (await app.Api.PostAsync(app.AccountPath("mutuals/usr_mg_alice/refresh"), null)).Content.ReadFromJsonAsync<JsonElement>();
        var carol = await (await app.Api.PostAsync(app.AccountPath("mutuals/usr_mg_carol/refresh"), null)).Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That((alice.GetProperty("success").GetBoolean(), alice.GetProperty("optedOut").GetBoolean())).IsEqualTo((true, false));
        await Assert.That((carol.GetProperty("success").GetBoolean(), carol.GetProperty("optedOut").GetBoolean())).IsEqualTo((false, true));

        var snapshot = await app.GetJsonAsync(app.AccountPath("mutuals"));
        var friends = snapshot.GetProperty("friends");
        await Assert.That(friends.GetProperty("usr_mg_alice").EnumerateArray().Select(e => e.GetString()!)).IsEquivalentTo(new[] { "usr_mg_bob" });
        // Bob was never fetched and Carol shares nothing: both are still in the graph, without links.
        await Assert.That(friends.GetProperty("usr_mg_bob").GetArrayLength()).IsEqualTo(0);
        await Assert.That(friends.GetProperty("usr_mg_carol").GetArrayLength()).IsEqualTo(0);
        var meta = snapshot.GetProperty("meta");
        await Assert.That(meta.GetProperty("usr_mg_alice").GetProperty("lastFetchedAt").ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(meta.GetProperty("usr_mg_carol").GetProperty("optedOut").GetBoolean()).IsTrue();
        await Assert.That(meta.TryGetProperty("usr_mg_bob", out _)).IsFalse();
        await Assert.That(snapshot.GetProperty("names").GetProperty("usr_mg_bob").GetString()).IsEqualTo("Bob");
    }

    [Test]
    public async Task Notifications_keep_v1_details_and_v2_data_responses_and_image_as_vrchat_sent_them()
    {
        await using var app = await VrcxScenario.StartAsync();
        var alice = app.Vrchat.Friend("usr_note_alice", "Alice");
        await app.LoginAsync();
        var invite = app.Vrchat.Notification("invite", alice, n => n.Details(new JsonObject { ["worldId"] = "wrld_x:1", ["worldName"] = "World X", ["imageUrl"] = "https://img/photo" }));
        var boop = app.Vrchat.NotificationV2("boop", new JsonObject
        {
            ["senderUserId"] = alice.Id,
            ["title"] = "Alice booped you",
            ["imageUrl"] = "https://img/boop",
            ["data"] = new JsonObject { ["emojiId"] = "default_wave" },
            ["responses"] = new JsonArray(new JsonObject { ["type"] = "boop", ["text"] = "Boop back", ["icon"] = "reply", ["data"] = "" }),
        });
        await app.Pipeline.NotificationAsync(invite);
        await app.Pipeline.NotificationV2Async(boop);

        var items = (await app.GetJsonAsync(app.AccountPath("notifications?pageSize=500"))).GetProperty("items").EnumerateArray().ToDictionary(n => n.GetProperty("id").GetString()!);

        var v1 = items[invite.Id];
        await Assert.That(v1.GetProperty("version").GetInt32()).IsEqualTo(1);
        await Assert.That(v1.GetProperty("senderUserId").GetString()).IsEqualTo(alice.Id);
        await Assert.That(v1.GetProperty("details").GetProperty("worldName").GetString()).IsEqualTo("World X");
        await Assert.That(v1.GetProperty("details").GetProperty("imageUrl").GetString()).IsEqualTo("https://img/photo");
        var v2 = items[(string)boop["id"]!];
        await Assert.That(v2.GetProperty("version").GetInt32()).IsEqualTo(2);
        await Assert.That(v2.GetProperty("imageUrl").GetString()).IsEqualTo("https://img/boop");
        await Assert.That(v2.GetProperty("details").GetProperty("emojiId").GetString()).IsEqualTo("default_wave");
        await Assert.That(v2.GetProperty("responses")[0].GetProperty("text").GetString()).IsEqualTo("Boop back");
        await Assert.That(v2.GetProperty("seen").GetBoolean()).IsFalse();
    }
}
