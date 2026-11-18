using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using VRCX.API.Mcp;
using VRCX.Common.VrcxDb;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

// The MCP endpoint for AI assistants: off by default, the API token in a header, read-only tools
// over what is stored (no VRChat call), memo writes, and never a silently picked account.
public sealed class McpTests
{
    private static readonly ScenarioOptions Enabled = new() { EnableMcp = true };

    private static readonly string[] ToolNames =
    [
        "find_user", "get_avatar", "get_current_instance", "get_favorites", "get_friend_log", "get_game_log", "get_game_sessions",
        "get_group", "get_hot_worlds", "get_instance_players", "get_moderations", "get_mutual_friends", "get_notifications",
        "get_online_friends", "get_previous_instances", "get_user", "get_user_history", "get_world", "list_accounts", "search_feed",
        "set_avatar_memo", "set_user_memo", "set_world_memo",
    ];

    private static readonly string[] WriteTools = ["set_avatar_memo", "set_user_memo", "set_world_memo"];

    private static StringContent Initialize() => new(
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""",
        Encoding.UTF8, "application/json");

    private static HttpRequestMessage Post(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = Initialize() };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }

    private static async Task<JsonElement> CallAsync(McpClient mcp, string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await mcp.CallToolAsync(tool, arguments);
        if (result.IsError == true) throw new InvalidOperationException($"{tool} failed: {Text(result)}");
        return JsonSerializer.SerializeToElement(result.StructuredContent);
    }

    private static async Task<string> CallFailingAsync(McpClient mcp, string tool, Dictionary<string, object?>? arguments = null)
    {
        var result = await mcp.CallToolAsync(tool, arguments);
        if (result.IsError != true) throw new InvalidOperationException($"{tool} should have failed but answered {JsonSerializer.Serialize(result.StructuredContent)}");
        return Text(result);
    }

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    private static List<string> Strings(JsonElement items, string property) => items.EnumerateArray().Select(i => i.GetProperty(property).GetString()!).ToList();

    [Test]
    public async Task The_endpoint_is_not_there_unless_enabled()
    {
        await using var app = await VrcxScenario.StartAsync();

        var response = await app.Api.SendAsync(Post("/1/mcp"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Without_the_token_in_a_header_it_answers_401()
    {
        await using var app = await VrcxScenario.StartAsync(Enabled);
        using var anonymous = app.CreateClient(authenticated: false);

        var none = await anonymous.SendAsync(Post("/1/mcp"));
        // The websocket and images take ?token=; MCP clients can always send a header, so it does not.
        var query = await anonymous.SendAsync(Post($"/1/mcp?token={VrcxScenario.ApiToken}"));
        var wrong = Post("/1/mcp");
        wrong.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-the-token-at-all");
        var bad = await anonymous.SendAsync(wrong);
        // Both header forms the rest of the API takes work.
        var header = await app.Api.SendAsync(Post("/1/mcp"));

        await Assert.That(none.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(query.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(header.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task The_tool_list_is_pinned_and_only_the_memo_tools_write()
    {
        await using var app = await VrcxScenario.StartAsync(Enabled);
        await using var mcp = await app.ConnectMcpAsync();

        var tools = await mcp.ListToolsAsync();

        await Assert.That(tools.Select(t => t.Name).Order(StringComparer.Ordinal).ToList()).IsEquivalentTo(ToolNames);
        foreach (var tool in tools.Select(t => t.ProtocolTool))
        {
            var writes = WriteTools.Contains(tool.Name);
            await Assert.That(tool.Annotations?.ReadOnlyHint).IsEqualTo(!writes).Because(tool.Name);
            // A memo write replaces the whole memo, so clients should confirm it; reads never destroy.
            await Assert.That(tool.Annotations?.DestructiveHint).IsEqualTo(writes).Because(tool.Name);
            await Assert.That(tool.Annotations?.OpenWorldHint == false).IsTrue().Because(tool.Name);
            await Assert.That(tool.OutputSchema).IsNotNull().Because(tool.Name);
            await Assert.That(tool.Description ?? "").IsNotEmpty().Because(tool.Name);
        }
        await Assert.That(mcp.ServerInstructions ?? "").Contains("Not observed is not the same as");
    }

    [Test]
    public async Task Every_tool_states_ReadOnly_and_Destructive_explicitly_and_is_registered()
    {
        // The attribute defaults (ReadOnly false, Destructive true) describe a dangerous tool; a new
        // tool must say what it is rather than inherit them by omission.
        var tools = typeof(McpSetup).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(m => (Method: m, Attribute: m.GetCustomAttributesData().SingleOrDefault(a => a.AttributeType == typeof(McpServerToolAttribute))))
            .Where(x => x.Attribute is not null)
            .ToList();

        await Assert.That(tools.Count).IsEqualTo(ToolNames.Length);
        foreach (var (method, attribute) in tools)
        {
            var named = attribute!.NamedArguments.ToDictionary(a => a.MemberName, a => a.TypedValue.Value);
            var where = $"{method.DeclaringType!.Name}.{method.Name}";
            await Assert.That(named.ContainsKey(nameof(McpServerToolAttribute.ReadOnly))).IsTrue().Because(where);
            await Assert.That(named.ContainsKey(nameof(McpServerToolAttribute.Destructive))).IsTrue().Because(where);
            await Assert.That(Equals(named.GetValueOrDefault(nameof(McpServerToolAttribute.UseStructuredContent)), true)).IsTrue().Because(where);
            await Assert.That(McpSetup.ToolTypes).Contains(method.DeclaringType!).Because(where);
        }
    }

    [Test]
    public async Task Pages_default_to_50_rows_and_never_return_more_than_200()
    {
        var rows = Enumerable.Range(0, 300).ToList();

        var first = McpPaging.Page(null, rows, 0, 0);
        var capped = McpPaging.Page(null, rows, 0, 1000);
        var last = McpPaging.Page(null, rows, 250, 100);

        await Assert.That(first.Items.Count).IsEqualTo(50);
        await Assert.That(first.Truncated).IsTrue();
        await Assert.That(first.NextOffset).IsEqualTo(50);
        await Assert.That(capped.Items.Count).IsEqualTo(200);
        await Assert.That(last.Items.Count).IsEqualTo(50);
        await Assert.That(last.Truncated).IsFalse();
        await Assert.That(last.NextOffset).IsNull();
    }

    [Test]
    public async Task With_two_accounts_a_tool_without_account_fails_with_the_list_instead_of_picking_one()
    {
        await using var app = await VrcxScenario.StartAsync(Enabled);
        app.Vrchat.SetMe("usr_mcp_first", "First");
        await app.LoginAsync();
        await using (var db = app.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.Add(new User { Id = "usr_mcp_second", DisplayName = "Second", FirstSeenAt = now, UpdatedAt = now });
            db.Accounts.Add(new Account { UserId = "usr_mcp_second", LoggedInAt = now });
            await db.SaveChangesAsync();
        }
        await using var mcp = await app.ConnectMcpAsync();

        var ambiguous = await CallFailingAsync(mcp, "get_online_friends");
        var unknown = await CallFailingAsync(mcp, "get_friend_log", new() { ["account"] = "Nobody" });
        var byName = await CallAsync(mcp, "get_online_friends", new() { ["account"] = "second" });
        var byId = await CallAsync(mcp, "get_friend_log", new() { ["account"] = "usr_mcp_first" });
        var accounts = await CallAsync(mcp, "list_accounts");

        await Assert.That(ambiguous).Contains("usr_mcp_first").And.Contains("usr_mcp_second");
        await Assert.That(unknown).Contains("Nobody").And.Contains("usr_mcp_second");
        await Assert.That(byName.GetProperty("account").GetProperty("id").GetString()).IsEqualTo("usr_mcp_second");
        await Assert.That(byId.GetProperty("account").GetProperty("id").GetString()).IsEqualTo("usr_mcp_first");
        await Assert.That(Strings(accounts.GetProperty("accounts"), "id")).IsEquivalentTo(["usr_mcp_first", "usr_mcp_second"]);
    }

    [Test]
    public async Task Friend_tools_answer_from_stored_presence_and_history_without_calling_VRChat()
    {
        await using var app = await VrcxScenario.StartAsync(Enabled);
        var club = app.Vrchat.World("wrld_mcp_club", "Club");
        var lounge = app.Vrchat.World("wrld_mcp_lounge", "Lounge");
        var alice = app.Vrchat.Friend("usr_mcp_alice", "Alice");
        var bob = app.Vrchat.Friend("usr_mcp_bob", "Bob");
        await app.LoginAsync();
        await app.Pipeline.FriendOnlineAsync(alice, "wrld_mcp_club:1~region(eu)", club);
        await app.Pipeline.FriendLocationAsync(alice, "wrld_mcp_lounge:2~region(eu)", lounge);
        var carol = app.Vrchat.User("usr_mcp_carol", "Carol");
        await app.Pipeline.FriendAddAsync(carol);
        await using (var db = app.CreateDbContext())
        {
            db.MutualFriendLinks.Add(new MutualFriendLink { OwnerUserId = app.AccountId!, FriendUserId = alice.Id, MutualUserId = bob.Id });
            db.MutualGraphMetas.Add(new MutualGraphMeta { OwnerUserId = app.AccountId!, FriendUserId = alice.Id, LastFetchedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var mcp = await app.ConnectMcpAsync();
        app.Vrchat.ClearRequests();

        var online = await CallAsync(mcp, "get_online_friends");
        var found = await CallAsync(mcp, "find_user", new() { ["query"] = "ali" });
        var user = await CallAsync(mcp, "get_user", new() { ["userId"] = alice.Id });
        var history = await CallAsync(mcp, "get_user_history", new() { ["userId"] = alice.Id, ["kinds"] = "presence" });
        var feed = await CallAsync(mcp, "search_feed", new() { ["types"] = "GPS" });
        var log = await CallAsync(mcp, "get_friend_log");
        var hot = await CallAsync(mcp, "get_hot_worlds");
        var mutuals = await CallAsync(mcp, "get_mutual_friends", new() { ["userId"] = alice.Id });

        await Assert.That(online.GetProperty("account").GetProperty("id").GetString()).IsEqualTo(app.AccountId);
        var friend = online.GetProperty("items").EnumerateArray().Single();
        await Assert.That(friend.GetProperty("userId").GetString()).IsEqualTo(alice.Id);
        await Assert.That(friend.GetProperty("state").GetString()).IsEqualTo("online");
        await Assert.That(friend.GetProperty("location").GetProperty("worldName").GetString()).IsEqualTo("Lounge");
        await Assert.That(online.GetProperty("truncated").GetBoolean()).IsFalse();

        var first = found.GetProperty("candidates")[0];
        await Assert.That(first.GetProperty("userId").GetString()).IsEqualTo(alice.Id);
        await Assert.That(first.GetProperty("isFriend").GetBoolean()).IsTrue();
        await Assert.That(first.GetProperty("matchedOn").GetString()).IsEqualTo("namePrefix");

        await Assert.That(user.GetProperty("displayName").GetString()).IsEqualTo("Alice");
        await Assert.That(user.GetProperty("relationship").GetProperty("isFriend").GetBoolean()).IsTrue();
        await Assert.That(user.GetProperty("presence").GetProperty("state").GetString()).IsEqualTo("online");
        await Assert.That(user.GetProperty("account").GetProperty("id").GetString()).IsEqualTo(app.AccountId);

        var kinds = history.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("value").GetString()).ToList();
        await Assert.That(kinds).Contains("online").And.Contains("location");

        var move = feed.GetProperty("items").EnumerateArray().Single();
        await Assert.That(move.GetProperty("userId").GetString()).IsEqualTo(alice.Id);
        await Assert.That(move.GetProperty("location").GetProperty("worldName").GetString()).IsEqualTo("Lounge");
        await Assert.That(move.GetProperty("previousLocation").GetProperty("worldName").GetString()).IsEqualTo("Club");

        // The first sync is the baseline; Carol, added after it, is the one Friend row.
        var rows = log.GetProperty("items").EnumerateArray().ToList();
        await Assert.That(rows.Select(r => (r.GetProperty("type").GetString(), r.GetProperty("userId").GetString()))).Contains(("Friend", carol.Id));

        await Assert.That(Strings(hot.GetProperty("worlds"), "worldId")).Contains("wrld_mcp_lounge");

        await Assert.That(mutuals.GetProperty("isFriend").GetBoolean()).IsTrue();
        await Assert.That(Strings(mutuals.GetProperty("mutuals").GetProperty("items"), "displayName")).IsEquivalentTo(["Bob"]);

        await Assert.That(app.Vrchat.Calls).IsEmpty();
    }

    [Test]
    public async Task Game_log_tools_answer_from_what_the_agent_shipped_without_calling_VRChat()
    {
        const string Here = "wrld_mcp_hub:7~region(eu)";
        await using var app = await VrcxScenario.StartAsync(Enabled);
        app.Vrchat.World("wrld_mcp_hub", "Hub");
        var ann = app.Vrchat.Friend("usr_mcp_ann", "Ann");
        await app.LoginAsync();
        await using var pc = await app.ConnectDesktopAsync("pc-mcp");
        await pc.GameRunningAsync(true);
        await pc.SendLogAsync(GameLog.Location(Here, "Hub"), GameLog.PlayerJoined("Ann", ann.Id), GameLog.PlayerJoined("Stranger", "usr_mcp_stranger"),
            GameLog.VideoPlay("https://example.invalid/v", "Ann"));
        await using var mcp = await app.ConnectMcpAsync();
        app.Vrchat.ClearRequests();

        var current = await CallAsync(mcp, "get_current_instance");
        var log = await CallAsync(mcp, "get_game_log", new() { ["types"] = "Location,OnPlayerJoined" });
        var sessions = await CallAsync(mcp, "get_game_sessions");
        var previous = await CallAsync(mcp, "get_previous_instances", new() { ["worldId"] = "wrld_mcp_hub" });
        var players = await CallAsync(mcp, "get_instance_players", new() { ["location"] = Here });
        var together = await CallAsync(mcp, "get_user", new() { ["userId"] = ann.Id });
        var neither = await CallFailingAsync(mcp, "get_previous_instances");

        await Assert.That(current.GetProperty("inInstance").GetBoolean()).IsTrue();
        await Assert.That(current.GetProperty("location").GetProperty("tag").GetString()).IsEqualTo(Here);
        await Assert.That(current.GetProperty("location").GetProperty("worldName").GetString()).IsEqualTo("Hub");
        await Assert.That(Strings(current.GetProperty("players").GetProperty("items"), "userId")).Contains(ann.Id);

        var types = log.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("type").GetString()).ToList();
        await Assert.That(types).Contains("Location").And.Contains("OnPlayerJoined").And.DoesNotContain("VideoPlay");

        var session = sessions.GetProperty("sessions").EnumerateArray().Single();
        await Assert.That(session.GetProperty("location").GetProperty("tag").GetString()).IsEqualTo(Here);
        await Assert.That(session.GetProperty("playerCount").GetInt32()).IsEqualTo(2);
        // Friends first.
        await Assert.That(session.GetProperty("players")[0].GetProperty("userId").GetString()).IsEqualTo(ann.Id);
        await Assert.That(session.GetProperty("players")[0].GetProperty("isFriend").GetBoolean()).IsTrue();
        await Assert.That(session.GetProperty("videoCount").GetInt32()).IsEqualTo(1);
        await Assert.That(sessions.GetProperty("truncated").GetBoolean()).IsFalse();

        await Assert.That(previous.GetProperty("items").GetArrayLength()).IsEqualTo(1);
        await Assert.That(previous.GetProperty("items")[0].GetProperty("location").GetProperty("worldName").GetString()).IsEqualTo("Hub");

        await Assert.That(Strings(players.GetProperty("items"), "userId")).Contains(ann.Id).And.Contains("usr_mcp_stranger");
        await Assert.That(together.GetProperty("timeTogether").GetProperty("joinCount").GetInt32()).IsEqualTo(1);
        await Assert.That(neither).Contains("exactly one");

        await Assert.That(app.Vrchat.Calls).IsEmpty();
    }

    [Test]
    public async Task List_and_content_tools_answer_from_the_synced_copy_without_calling_VRChat()
    {
        await using var app = await VrcxScenario.StartAsync(Enabled);
        var alice = app.Vrchat.Friend("usr_mcp_fav", "Fav");
        var pest = app.Vrchat.User("usr_mcp_pest", "Pest");
        app.Vrchat.SetMe();
        app.Vrchat.FavoriteGroup("friend", "group_0", "Best");
        app.Vrchat.Favorite("friend", alice.Id, "group_0");
        app.Vrchat.PlayerModeration("block", pest);
        app.Vrchat.Notification("friendRequest", pest);
        await app.LoginAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.CreateDbContext())
        {
            db.Worlds.Add(new World { Id = "wrld_mcp_stored", Name = "Stored World", AuthorName = "Maker", Capacity = 16, FirstSeenAt = now, UpdatedAt = now });
            db.Avatars.Add(new Avatar { Id = "avtr_mcp_stored", Name = "Stored Avatar", FirstSeenAt = now, UpdatedAt = now });
            db.Groups.Add(new Group { Id = "grp_mcp_stored", Name = "Stored Group", ShortCode = "STORE", MemberCount = 12, FirstSeenAt = now });
            await db.SaveChangesAsync();
        }
        await using var mcp = await app.ConnectMcpAsync();
        app.Vrchat.ClearRequests();

        var notifications = await CallAsync(mcp, "get_notifications");
        var favorites = await CallAsync(mcp, "get_favorites", new() { ["type"] = "friend" });
        var moderations = await CallAsync(mcp, "get_moderations");
        var world = await CallAsync(mcp, "get_world", new() { ["worldId"] = "wrld_mcp_stored" });
        var avatar = await CallAsync(mcp, "get_avatar", new() { ["avatarId"] = "avtr_mcp_stored" });
        var group = await CallAsync(mcp, "get_group", new() { ["groupId"] = "grp_mcp_stored" });
        var badType = await CallFailingAsync(mcp, "get_favorites", new() { ["type"] = "hats" });
        var unseen = await CallFailingAsync(mcp, "get_world", new() { ["worldId"] = "wrld_mcp_never" });

        var notification = notifications.GetProperty("items").EnumerateArray().Single();
        await Assert.That(notification.GetProperty("type").GetString()).IsEqualTo("friendRequest");
        await Assert.That(notification.GetProperty("senderUserId").GetString()).IsEqualTo(pest.Id);
        await Assert.That(notifications.GetProperty("account").GetProperty("id").GetString()).IsEqualTo(app.AccountId);

        var favorite = favorites.GetProperty("favorites").GetProperty("items").EnumerateArray().Single();
        await Assert.That(favorite.GetProperty("targetId").GetString()).IsEqualTo(alice.Id);
        await Assert.That(favorite.GetProperty("group").GetString()).IsEqualTo("Best");

        var moderation = moderations.GetProperty("items").EnumerateArray().Single();
        await Assert.That(moderation.GetProperty("type").GetString()).IsEqualTo("block");
        await Assert.That(moderation.GetProperty("targetUserId").GetString()).IsEqualTo(pest.Id);

        await Assert.That(world.GetProperty("name").GetString()).IsEqualTo("Stored World");
        await Assert.That(world.GetProperty("capacity").GetInt32()).IsEqualTo(16);
        await Assert.That(avatar.GetProperty("name").GetString()).IsEqualTo("Stored Avatar");
        await Assert.That(group.GetProperty("shortCode").GetString()).IsEqualTo("STORE");
        await Assert.That(badType).Contains("friend, world or avatar");
        await Assert.That(unseen).Contains("never seen");

        await Assert.That(app.Vrchat.Calls).IsEmpty();
    }

    [Test]
    public async Task Memo_writes_round_trip_and_show_over_REST()
    {
        await using var app = await VrcxScenario.StartAsync(Enabled);
        var alice = app.Vrchat.Friend("usr_mcp_memo", "Memo Friend");
        await app.LoginAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.CreateDbContext())
        {
            db.Worlds.Add(new World { Id = "wrld_mcp_memo", Name = "Memo World", FirstSeenAt = now, UpdatedAt = now });
            db.Avatars.Add(new Avatar { Id = "avtr_mcp_memo", Name = "Memo Avatar", FirstSeenAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        await using var mcp = await app.ConnectMcpAsync();
        app.Vrchat.ClearRequests();

        var userMemo = await CallAsync(mcp, "set_user_memo", new() { ["userId"] = alice.Id, ["memo"] = "Met at the club\nlikes jazz" });
        var restUser = await app.GetJsonAsync($"/1/users/{alice.Id}/memo");
        var shown = await CallAsync(mcp, "get_user", new() { ["userId"] = alice.Id });
        await CallAsync(mcp, "set_world_memo", new() { ["worldId"] = "wrld_mcp_memo", ["memo"] = "Good mirrors" });
        var restWorld = await app.GetJsonAsync("/1/worlds/wrld_mcp_memo/memo");
        await CallAsync(mcp, "set_avatar_memo", new() { ["avatarId"] = "avtr_mcp_memo", ["memo"] = "Quest ok" });
        var restAvatar = await app.GetJsonAsync("/1/avatars/avtr_mcp_memo/memo");
        var cleared = await CallAsync(mcp, "set_user_memo", new() { ["userId"] = alice.Id, ["memo"] = "" });
        var restCleared = await app.Api.GetAsync($"/1/users/{alice.Id}/memo");
        var unseen = await CallFailingAsync(mcp, "set_user_memo", new() { ["userId"] = "usr_mcp_never", ["memo"] = "x" });

        await Assert.That(userMemo.GetProperty("memo").GetString()).IsEqualTo("Met at the club\nlikes jazz");
        await Assert.That(restUser.GetProperty("memo").GetString()).IsEqualTo("Met at the club\nlikes jazz");
        await Assert.That(shown.GetProperty("memo").GetString()).IsEqualTo("Met at the club\nlikes jazz");
        await Assert.That(restWorld.GetProperty("memo").GetString()).IsEqualTo("Good mirrors");
        await Assert.That(restAvatar.GetProperty("memo").GetString()).IsEqualTo("Quest ok");
        await Assert.That(cleared.GetProperty("removed").GetBoolean()).IsTrue();
        await Assert.That(restCleared.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(unseen).Contains("never seen");
        // Memos are VRCXD's own: nothing goes to VRChat.
        await Assert.That(app.Vrchat.Calls).IsEmpty();
    }

    [Test]
    public async Task A_long_friend_log_is_paged_with_a_truncated_flag()
    {
        await using var app = await VrcxScenario.StartAsync(Enabled);
        await app.LoginAsync();
        foreach (var name in new[] { "One", "Two", "Three" })
            await app.Pipeline.FriendAddAsync(app.Vrchat.User($"usr_mcp_{name.ToLowerInvariant()}", name));
        await using var mcp = await app.ConnectMcpAsync();

        var first = await CallAsync(mcp, "get_friend_log", new() { ["limit"] = 2 });
        var rest = await CallAsync(mcp, "get_friend_log", new() { ["limit"] = 2, ["offset"] = first.GetProperty("nextOffset").GetInt32() });

        await Assert.That(first.GetProperty("items").GetArrayLength()).IsEqualTo(2);
        await Assert.That(first.GetProperty("truncated").GetBoolean()).IsTrue();
        await Assert.That(rest.GetProperty("items").GetArrayLength()).IsEqualTo(1);
        await Assert.That(rest.GetProperty("truncated").GetBoolean()).IsFalse();
        var all = Strings(first.GetProperty("items"), "userId").Concat(Strings(rest.GetProperty("items"), "userId")).ToList();
        await Assert.That(all).IsEquivalentTo(["usr_mcp_one", "usr_mcp_two", "usr_mcp_three"]);
    }
}
