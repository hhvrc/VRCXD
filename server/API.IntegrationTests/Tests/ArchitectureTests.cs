using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;
using VRCX.API.Controller.Friends;
using VRCX.API.Controller.GameLog;
using VRCX.Common.Services.Sessions;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.API.IntegrationTests.Tests;

/// <summary>
/// The ratchet for the target architecture (AGENTS.md): controllers and MCP tools call
/// Common/Features and do not take the database, a VRChat client or the session manager
/// themselves. What did so before its feature moved is listed in <see cref="NotYetMoved"/>; the
/// list may only shrink, and nothing new may join it.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Type[] Banned = [typeof(VrcxContext), typeof(VrchatApiClient), typeof(IAccountSessionManager)];

    /// <summary>
    /// Today's violations, as "Type: what it takes". Remove a line when its feature moves. Never add
    /// one to make a change pass: move the code into the feature instead. The pure VRChat proxy
    /// controllers stay as they are, so their lines stay too.
    /// </summary>
    private static readonly string[] NotYetMoved =
    [
        "AccountsController: IAccountSessionManager",
        "AccountsController: VrcxContext",
    ];

    private static List<string> Violations() =>
        typeof(FriendsController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && (typeof(ControllerBase).IsAssignableFrom(t) || t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters())
                .Concat(t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters()))
                .Select(p => p.ParameterType)
                .Where(Banned.Contains)
                .Select(p => $"{t.Name}: {p.Name}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

    [Test]
    public async Task No_controller_or_mcp_tool_newly_takes_the_database_or_a_vrchat_session()
    {
        // Failed by hand, so the message names every new violation rather than the first few.
        var added = Violations().Except(NotYetMoved).ToList();
        if (added.Count > 0) Assert.Fail("Takes what only Common/Features may: " + string.Join(", ", added));
        await Assert.That(added).IsEmpty();
    }

    [Test]
    public async Task The_allow_list_holds_only_what_still_violates()
    {
        var moved = NotYetMoved.Except(Violations()).ToList();
        if (moved.Count > 0) Assert.Fail("No longer violates; remove from NotYetMoved: " + string.Join(", ", moved));
        await Assert.That(moved).IsEmpty();
    }

    [Test]
    [Arguments(typeof(FriendsController))]
    [Arguments(typeof(GameLogController))]
    [Arguments(typeof(VRCX.API.Controller.Memos.MemosController))]
    [Arguments(typeof(VRCX.API.Controller.Feed.FeedController))]
    [Arguments(typeof(VRCX.API.Controller.Notifications.NotificationsController))]
    [Arguments(typeof(VRCX.API.Controller.Moderation.ModerationController))]
    [Arguments(typeof(VRCX.API.Controller.Favorites.FavoritesController))]
    [Arguments(typeof(VRCX.API.Controller.Charts.ChartsController))]
    [Arguments(typeof(VRCX.API.Controller.UserActivity.UserActivityController))]
    [Arguments(typeof(VRCX.API.Controller.QuickSearch.QuickSearchController))]
    [Arguments(typeof(VRCX.API.Controller.Avatars.AvatarHistoryController))]
    [Arguments(typeof(VRCX.API.Controller.Groups.AccountGroupsController))]
    [Arguments(typeof(VRCX.API.Controller.Worlds.AccountWorldsController))]
    [Arguments(typeof(VRCX.API.Controller.Avatars.AccountAvatarsController))]
    [Arguments(typeof(VRCX.API.Controller.Instances.AccountInstancesController))]
    [Arguments(typeof(VRCX.API.Controller.Search.AccountSearchController))]
    [Arguments(typeof(VRCX.API.Controller.Tools.AccountToolsController))]
    [Arguments(typeof(VRCX.API.Controller.Profile.AccountProfileController))]
    [Arguments(typeof(VRCX.API.Controller.Users.AccountUsersController))]
    [Arguments(typeof(VRCX.API.Controller.Desktop.AccountDesktopController))]
    [Arguments(typeof(VRCX.API.Controller.Settings.AccountSettingsController))]
    public async Task A_moved_feature_takes_none_of_them(Type controller)
    {
        await Assert.That(Violations().Where(v => v.StartsWith(controller.Name + ":", StringComparison.Ordinal)).ToList()).IsEmpty();
    }
}
