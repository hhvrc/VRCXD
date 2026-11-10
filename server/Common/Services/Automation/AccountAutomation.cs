using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Live;
using VRCX.Common.Services.Presence;
using VRCX.Common.Services.Sessions;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VrchatNotification = VRCX.Vrchat.Models.Notification;

namespace VRCX.Common.Services.Automation;

/// <summary>Which desktop apps hold a live socket right now, the server's stand-in for "the game is running here".</summary>
public sealed class GameClientRegistry
{
    private readonly ConcurrentDictionary<string, int> _connections = new();

    public void Connected(string clientId) => _connections.AddOrUpdate(clientId, 1, (_, n) => n + 1);

    public void Disconnected(string clientId)
    {
        if (_connections.AddOrUpdate(clientId, 0, (_, n) => n - 1) <= 0) _connections.TryRemove(clientId, out _);
    }

    public bool IsConnected(string clientId) => _connections.ContainsKey(clientId);
}

/// <summary>
/// The original's Auto Status and Invites: updateAutoStateChange changes the status depending on
/// who shares the instance, and the notification handler answers invite requests from favorites.
/// Both run here so they keep working with no client open. Local favorite groups count as well as
/// VRChat's, by the original's group keys ("friend:group_N", "local:Name").
/// </summary>
public sealed class AccountAutomation(
    IDbContextFactory<VrcxContext> dbFactory,
    IAccountSessionManager sessions,
    VrchatStore store,
    ILiveEventBus bus,
    GameClientRegistry clients,
    IPresenceCache presence,
    IGameClientState clientState,
    ILogger<AccountAutomation> logger)
{
    /// <summary>Applies the account's auto status rules. Returns the status it changed to, or null when nothing changed.</summary>
    public async Task<string?> UpdateStatusAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
        if (settings is not { AutoStateChangeEnabled: true }) return null;

        var here = await GameLocationAsync(db, accountId, ct);
        var location = VrchatLocation.Parse(here);
        if (here is null || !location.IsRealInstance) return null;
        if (settings.AutoStateChangeInstanceTypes.Length > 0 && !settings.AutoStateChangeInstanceTypes.Contains(InstanceTypeKey(location))) return null;

        var players = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.InstanceLocation == here && v.Source == VisitSource.GameLog && v.LeftAt == null)
            .Select(v => v.UserId).Distinct().ToListAsync(ct);
        if (players.Count == 0) return null;

        var withCompany = players.Count > 1;
        if (settings.AutoStateChangeNoFriends)
        {
            var friends = await db.Friendships.AsNoTracking()
                .Where(f => f.OwnerUserId == accountId && f.EndedAt == null && players.Contains(f.FriendUserId))
                .Select(f => f.FriendUserId).ToListAsync(ct);
            withCompany = settings.AutoStateChangeGroups.Length > 0
                ? await FavoriteFriends.InGroups(db, accountId, settings.AutoStateChangeGroups).AnyAsync(id => friends.Contains(id), ct)
                : friends.Count > 0;
        }

        var target = withCompany ? settings.AutoStateChangeCompanyStatus : settings.AutoStateChangeAloneStatus;
        var stored = await db.Users.AsNoTracking().Where(u => u.Id == accountId).Select(u => u.Status).FirstOrDefaultAsync(ct);
        if (stored == target) return null;
        if (sessions.Get(accountId) is not { } client) return null;

        // The stored status can lag, so VRChat has the last word before anything is written.
        var live = await client.GetCurrentUserAsync(ct);
        if (live.Status == target)
        {
            await store.UpsertCurrentUserAsync(db, live, ct);
            await db.SaveChangesAsync(ct);
            return null;
        }

        var changes = new Dictionary<string, string> { ["status"] = target };
        if (withCompany && settings.AutoStateChangeCompanyDescEnabled) changes["statusDescription"] = settings.AutoStateChangeCompanyDesc;
        else if (!withCompany && settings.AutoStateChangeAloneDescEnabled) changes["statusDescription"] = settings.AutoStateChangeAloneDesc;

        var updated = await client.UpdateCurrentUserAsync(accountId, changes, ct);
        var user = await store.UpsertCurrentUserAsync(db, updated, ct);
        await db.SaveChangesAsync(ct);
        bus.Publish(LiveEvents.UserChanged(user, changes.Keys));
        logger.LogInformation("Status of {Account} automatically changed to {Status}", accountId, target);
        return target;
    }

    /// <summary>Answers an invite request when the account's rules allow it. Returns whether an invite was sent.</summary>
    public async Task<bool> AnswerInviteRequestAsync(string accountId, VrchatNotification request, CancellationToken ct)
    {
        if (request.Type != "requestInvite" || request.SenderUserId is not { } senderId) return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
        if (settings is null || settings.AutoAcceptInviteRequests == AutoAcceptMode.Off) return false;

        var self = await db.Users.AsNoTracking().Where(u => u.Id == accountId).Select(u => new { u.Status }).FirstOrDefaultAsync(ct);
        if (self is null) return false;
        var here = await presence.GetAsync(accountId, ct);
        if (settings.AutoAcceptInviteStatuses.Length > 0 && !settings.AutoAcceptInviteStatuses.Contains(self.Status)) return false;

        // The game log knows best; without it, the location VRChat reports.
        var gameLocation = await GameLocationAsync(db, accountId, ct);
        var apiLocation = VrchatLocation.Parse(here.Location);
        var location = VrchatLocation.Parse(gameLocation ?? (apiLocation.IsTraveling ? here.TravelingToLocation : here.Location));
        if (!location.IsRealInstance) return false;

        // As the original: All Favorites is any friend group, VRChat's or local; Selected Favorites the picked ones.
        IQueryable<string> favorites;
        if (settings.AutoAcceptInviteRequests == AutoAcceptMode.SelectedFavorites)
        {
            if (settings.AutoAcceptInviteGroups.Length == 0) return false;
            favorites = FavoriteFriends.InGroups(db, accountId, settings.AutoAcceptInviteGroups);
        }
        else favorites = FavoriteFriends.All(db, accountId);
        if (!await favorites.AnyAsync(id => id == senderId, ct)) return false;

        var closedAt = await db.Instances.AsNoTracking().Where(i => i.Location == location.InstanceLocation).Select(i => i.ClosedAt).FirstOrDefaultAsync(ct);
        if (!CanInvite(location, accountId, gameLocation, closedAt)) return false;
        if (sessions.Get(accountId) is not { } client) return false;

        var worldName = await db.Worlds.AsNoTracking().Where(w => w.Id == location.WorldId).Select(w => w.Name).FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(worldName)) worldName = (await client.GetWorldAsync(location.WorldId, ct))?.Name;
        if (string.IsNullOrEmpty(worldName)) return false;

        await client.SendInviteAsync(senderId, location.InstanceLocation, worldName, ct);
        await client.HideNotificationAsync(request.Id, ct);
        await store.MarkNotificationAsync(db, accountId, request.Id, seen: null, hidden: true, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Auto invite sent to {Sender} for {Account}", request.SenderUsername, accountId);
        return true;
    }

    /// <summary>
    /// The original's handleAutoDeclineFriendRequest: with the setting on, a friend request from someone
    /// the game log has never seen in an instance is hidden. Returns whether it was.
    /// </summary>
    public async Task<bool> DeclineFriendRequestAsync(string accountId, VrchatNotification request, CancellationToken ct)
    {
        if (request.Type != "friendRequest") return false;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
        if (settings is not { AutoDeclineFriendRequests: true }) return false;

        // The original's getJoinCount: game log joins by the sender's id or display name.
        var senderId = request.SenderUserId;
        var senderName = request.SenderUsername;
        // Only this account's game log: joins in instances the account was in itself.
        var mine = db.InstanceVisits.Where(v => v.UserId == accountId && v.Source == VisitSource.GameLog).Select(v => v.InstanceLocation);
        var seen = await db.InstanceVisits.AsNoTracking()
            .Where(v => v.Source == VisitSource.GameLog && mine.Contains(v.InstanceLocation))
            .AnyAsync(v => (senderId != null && v.UserId == senderId) || (senderName != null && v.User.DisplayName == senderName), ct);
        if (seen) return false;
        if (sessions.Get(accountId) is not { } client) return false;

        await client.HideNotificationAsync(request.Id, ct);
        await store.MarkNotificationAsync(db, accountId, request.Id, seen: null, hidden: true, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Auto declined friend request from {Sender} for {Account}", senderName, accountId);
        return true;
    }

    /// <summary>
    /// The original's tryDeleteOldPrints: with Auto Delete Old Prints on, prints beyond the account's
    /// limit less two (room for new ones) are deleted, oldest first, skipping favorites. Runs when the
    /// account connects and when VRChat says a print was made. Returns the deleted ids.
    /// </summary>
    public async Task<IReadOnlyList<string>> DeleteOldPrintsAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.Accounts.AsNoTracking().Where(a => a.UserId == accountId).Select(a => a.Settings).FirstOrDefaultAsync(ct);
        if (settings is not { AutoDeleteOldPrints: true }) return [];
        if (sessions.Get(accountId) is not { } client) return [];

        var prints = (await client.GetPrintsAsync(accountId, ct)).OrderByDescending(p => p.Timestamp).ToList();
        var limit = (PermissionMax(await client.GetPermissionsAsync(ct), "permission-user-prints") ?? 64) - 2;
        var deleteCount = prints.Count - limit;
        if (deleteCount <= 0) return [];

        var favorites = (await db.PrintFavorites.AsNoTracking().Where(f => f.OwnerUserId == accountId).Select(f => f.PrintId).ToListAsync(ct)).ToHashSet();
        var ids = new List<string>();
        for (var i = prints.Count - 1; i >= 0 && ids.Count < deleteCount; i--)
            if (!favorites.Contains(prints[i].Id)) ids.Add(prints[i].Id);
        if (ids.Count < deleteCount)
            logger.LogInformation("Unable to automatically delete enough old prints for {Account} because {Count} print(s) are protected by favorites", accountId, deleteCount - ids.Count);

        var deleted = new List<string>();
        foreach (var id in ids)
        {
            await client.DeletePrintAsync(id, ct);
            deleted.Add(id);
            logger.LogInformation("Old print automatically deleted for {Account}: {Print}", accountId, id);
        }
        return deleted;
    }

    /// <summary>A permission's "max" from GET auth/permissions, as the original's getPermissions reads it.</summary>
    public static int? PermissionMax(System.Text.Json.JsonElement permissions, string name)
    {
        if (permissions.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
        foreach (var permission in permissions.EnumerateArray())
        {
            if (permission.TryGetProperty("name", out var n) && n.ValueEquals(name)
                && permission.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Object
                && data.TryGetProperty("max", out var max) && max.TryGetInt32(out var value) && value > 0)
                return value;
        }
        return null;
    }

    /// <summary>The instance this account's connected desktop app has the game in, if any.</summary>
    public async Task<string?> GameLocationAsync(string accountId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await GameLocationAsync(db, accountId, ct);
    }

    private async Task<string?> GameLocationAsync(VrcxContext db, string accountId, CancellationToken ct)
    {
        var clientIds = await db.GameLogClients.AsNoTracking()
            .Where(c => c.AccountId == accountId)
            .OrderByDescending(c => c.LastSeenAt)
            .Select(c => c.ClientId)
            .ToListAsync(ct);
        foreach (var clientId in clientIds.Where(clients.IsConnected))
            if ((await clientState.GetAsync(clientId, ct))?.CurrentInstanceLocation is { } location) return location;
        return null;
    }

    /// <summary>The key the instance type filter stores, as in the original's dialog.</summary>
    public static string InstanceTypeKey(VrchatLocation location) => location.AccessTypeName switch
    {
        "Invite" => "invite",
        "Invite+" => "invite+",
        "Friends" => "friends",
        "Friends+" => "friends+",
        "Group Public" => "groupPublic",
        "Group+" => "groupPlus",
        "Group" => "groupOnly",
        _ => "public",
    };

    /// <summary>The original's checkCanInvite, for an instance the account is in.</summary>
    public static bool CanInvite(VrchatLocation location, string accountId, string? gameLocation, DateTimeOffset? closedAt)
    {
        if (closedAt is not null) return false;
        if (location.AccessTypeName == "Public" || location.AccessType == "group" || location.OwnerUserId == accountId) return true;
        if (location.AccessTypeName is "Invite" or "Friends") return false;
        return gameLocation == location.InstanceLocation;
    }
}
