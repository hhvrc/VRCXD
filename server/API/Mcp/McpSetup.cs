using ModelContextProtocol.Protocol;

namespace VRCX.API.Mcp;

/// <summary>
/// The Model Context Protocol endpoint: streamable HTTP at /1/mcp, stateless (every POST stands
/// alone, so nothing is kept between calls), behind the same API token as the rest of /1/.
/// Version 1 reads what the server has stored and writes only VRCXD's own memos; no tool calls
/// VRChat, so an assistant cannot spend the shared VRChat rate limit or change anything there.
/// </summary>
public static class McpSetup
{
    public const string Path = "/1/mcp";

    /// <summary>Every tool class. Each tool's annotations are pinned by the integration tests.</summary>
    public static readonly Type[] ToolTypes =
    [
        typeof(AccountTools), typeof(FriendTools), typeof(UserTools), typeof(FeedTools),
        typeof(GameLogTools), typeof(ListTools), typeof(ContentTools), typeof(MemoTools),
    ];

    public const string Instructions = """
        VRCXD is a self-hosted VRChat companion. It records what it observes about VRChat accounts
        logged into it (friends' presence and moves, profile changes, the friend log) and what the
        agent on the player's PC logs from the game (instances, players met, videos). These tools
        read that record; none of them contacts VRChat, and the only writes are VRCXD's own memos.

        Accounts: the server can hold several VRChat accounts. Account-scoped tools take an optional
        `account` (usr_ id or display name); leave it out only when list_accounts shows one account.
        Every result names the account it used.

        Which tool:
        - Who is online / where is X now: get_online_friends, or get_user for one person.
        - Names to ids: find_user first; the other user tools take usr_ ids.
        - Where was X / when did X come online / status or avatar changes: search_feed (friends),
          get_user_history (anyone the server saw).
        - What did I do / who was I with: get_game_sessions, get_game_log; who was in one instance:
          get_instance_players; past instances of a user, world or group: get_previous_instances.
        - Right now in the game: get_current_instance.
        - Time spent with someone, friendship dates, notes, memos: get_user.
        - Friend list changes: get_friend_log. Shared friends: get_mutual_friends.
        - Popular with friends: get_hot_worlds. Lists VRChat keeps: get_notifications,
          get_favorites, get_moderations. Stored content: get_world, get_avatar, get_group.
        - Notes for later: set_user_memo, set_world_memo, set_avatar_memo (replace the whole memo).

        Caveat: history only covers what the server observed while it was running and logged in,
        and the game log only what the agent shipped while it ran. Not observed is not the same as
        did not happen: when nothing is recorded, say it was not observed rather than that it did
        not happen. Profiles are stored copies and can be stale.

        Lists are paged: 50 rows unless `limit` says otherwise, at most 200; when `truncated` is
        true, call again with `offset` = `nextOffset` (or the cursor the tool names).
        """;

    public static IServiceCollection AddVrcxMcp(this IServiceCollection services)
    {
        services.AddScoped<McpAccountResolver>();
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "vrcxd", Version = typeof(McpSetup).Assembly.GetName().Version?.ToString() ?? "0" };
                options.ServerInstructions = Instructions;
            })
            .WithHttpTransport(http => http.Stateless = true)
            // Named: positionally, Type[] binds to the generic WithTools<T>(T target) and registers nothing.
            .WithTools(toolTypes: ToolTypes);
        return services;
    }

    /// <summary>Header auth only (VrcxToken or Authorization: Bearer): ApiTokenAuthentication takes ?token= only on the websocket and images.</summary>
    public static void MapVrcxMcp(this WebApplication app) =>
        app.MapMcp(Path).RequireAuthorization();
}
