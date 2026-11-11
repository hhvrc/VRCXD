using Microsoft.EntityFrameworkCore;
using VRCX.Common.Live;
using VRCX.Common.Services.Live;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat.Pipeline;

namespace VRCX.Common.Services.Ingestion;

public interface IPipelineIngestor
{
    /// <summary>Turns one decoded pipeline event for one account into rows, in its own unit of work, then publishes what changed.</summary>
    Task HandleAsync(string accountId, PipelineEvent e, CancellationToken ct);
}

/// <inheritdoc />
public sealed class PipelineIngestor(IDbContextFactory<VrcxContext> dbFactory, VrchatStore store, ILiveEventBus bus, PresenceDebouncer debouncer, ILogger<PipelineIngestor> logger, TimeProvider? time = null, VRCX.Common.Services.Live.DesktopClients? desktops = null, VRCX.Common.Services.Live.InstanceQueues? queues = null) : IPipelineIngestor
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task HandleAsync(string accountId, PipelineEvent e, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var published = new List<ServerEvent>();

        switch (e)
        {
            case FriendEvent friend:
                await HandleFriendAsync(db, accountId, friend, now, published, ct);
                break;

            case CurrentUserEvent self:
                if (self.Type == "user-update" && self.Payload.User is { } me)
                {
                    var meRow = await store.UpsertCurrentUserAsync(db, me, now, ct);
                    if (await db.Accounts.FindAsync([accountId], ct) is { } accountRow) await store.RecordAvatarWearAsync(db, accountRow, me.CurrentAvatar, now, ct);
                }
                else if (self.Type == "user-location")
                {
                    var user = await store.EnsureUserAsync(db, accountId, null, now, ct);
                    var presence = await store.ApplyPresenceAsync(db, user, PresenceKind.Location, self.Payload.Location, self.Payload.TravelingToLocation, null, now, ct);
                    if (presence is not null) published.Add(LiveEvents.Presence(user, presence));
                    if (desktops is not null) await QuestGameLogAsync(db, user, self.Payload.Location, now, published, ct);
                    if (queues is not null)
                    {
                        var here = VRCX.Vrchat.VrchatLocation.Parse(self.Payload.Location ?? "");
                        foreach (var dropped in queues.RemoveAllBut(accountId, here.IsRealInstance ? here.InstanceLocation : null))
                            published.Add(LiveEvents.InstanceQueue(accountId, dropped, left: true, removedByMove: true));
                    }
                }
                break;

            case NotificationEvent n:
                {
                    var row = await store.UpsertNotificationAsync(db, accountId, n.Notification, now, ct);
                    published.Add(LiveEvents.Notification(row, n.Notification.SenderUsername));
                    break;
                }
            case NotificationV2Event n2:
                {
                    var row = await store.UpsertNotificationAsync(db, accountId, n2.Notification, now, ct);
                    published.Add(LiveEvents.Notification(row, n2.Notification.SenderUsername));
                    break;
                }
            case NotificationV2UpdateEvent update:
                {
                    bool? seen = null;
                    if (update.Payload.Updates is { } updates && updates.TryGetProperty("seen", out var seenElement) && seenElement.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                        seen = seenElement.GetBoolean();
                    await store.MarkNotificationAsync(db, accountId, update.Payload.Id, seen, null, ct);
                    break;
                }
            case NotificationV2DeleteEvent delete:
                foreach (var id in delete.Payload.Ids) await store.MarkNotificationAsync(db, accountId, id, null, true, ct);
                break;
            case NotificationIdEvent byId when byId.NotificationId.Length > 0:
                await store.MarkNotificationAsync(db, accountId, byId.NotificationId,
                    seen: byId.Type is "see-notification" ? true : null,
                    hidden: byId.Type is "hide-notification" or "clear-notification" ? true : null, ct);
                break;
            case NotificationResponseEvent response when response.Payload.NotificationId is { } responded:
                await store.MarkNotificationAsync(db, accountId, responded, true, true, ct);
                break;

            case GroupEvent group:
                await HandleGroupAsync(db, accountId, group, now, ct);
                break;

            case InstanceEvent queue when queues is not null && queue.Type is "instance-queue-joined" or "instance-queue-position" && queue.Payload.InstanceLocation is { Length: > 0 } queueLocation:
                {
                    // The original's instanceQueueUpdate: the place and size, with the names VRCX knows.
                    var (worldName, groupName) = await LocationNamesAsync(db, queueLocation, ct);
                    var queued = new VRCX.Common.Services.Live.QueuedInstance(queueLocation, queue.Payload.Position ?? 0, queue.Payload.QueueSize ?? 0, worldName, groupName, now);
                    queues.Set(accountId, queued);
                    published.Add(LiveEvents.InstanceQueue(accountId, queued, left: false, removedByMove: false));
                    break;
                }
            case InstanceEvent queueLeft when queues is not null && queueLeft.Type is "instance-queue-left" && queueLeft.Payload.InstanceLocation is { Length: > 0 } leftLocation:
                if (queues.Remove(accountId, leftLocation) is { } leftQueue) published.Add(LiveEvents.InstanceQueue(accountId, leftQueue, left: true, removedByMove: false));
                break;

            case InstanceEvent instance when instance.Type is "instance-queue-ready" or "instance-closed" && instance.Payload.InstanceLocation is { Length: > 0 } location:
                {
                    if (instance.Type == "instance-queue-ready" && queues?.Remove(accountId, location) is { } readyQueue)
                        published.Add(LiveEvents.InstanceQueue(accountId, readyQueue, left: true, removedByMove: false));
                    var row = await InstanceNoticeAsync(db, accountId, instance.Type, location, now, ct);
                    db.Notifications.Add(row);
                    published.Add(LiveEvents.Notification(row, null));
                    break;
                }

            case EconomyUpdateEvent economy:
                published.Add(LiveEvents.Credits(accountId, economy.Payload.WalletBalance));
                break;

            case ContentRefreshEvent refresh when !string.IsNullOrEmpty(refresh.Payload.ContentType):
                published.Add(LiveEvents.ContentRefresh(accountId, refresh.Payload.ContentType, refresh.Payload.ActionType ?? ""));
                break;

            case InstanceEvent or ContentRefreshEvent:
                logger.LogDebug("Pipeline {Type} for {Account} (not stored)", e.Type, accountId);
                break;

            default:
                logger.LogDebug("Pipeline {Type} for {Account}: {Content}", e.Type, accountId, e.Content);
                break;
        }

        await db.SaveChangesAsync(ct);
        foreach (var serverEvent in published) bus.Publish(serverEvent);
    }

    /// <summary>
    /// The original's runSetCurrentUserLocationFlow "gameLog support for Quest": with no PC game running,
    /// the account's own location from VRChat is what the game log records. A real instance starts a
    /// stay there (unless one is already open), anything else ends the open one.
    /// </summary>
    private async Task QuestGameLogAsync(VrcxContext db, User me, string? tag, DateTimeOffset now, List<ServerEvent> published, CancellationToken ct)
    {
        var clientIds = await db.GameLogClients.AsNoTracking().Where(c => c.AccountId == me.Id).Select(c => c.ClientId).ToListAsync(ct);
        if (clientIds.Any(id => desktops!.Get(id)?.GameRunning == true)) return;

        var location = VRCX.Vrchat.VrchatLocation.Parse(tag ?? "");
        var open = await db.InstanceVisits.Where(v => v.UserId == me.Id && v.LeftAt == null && v.Source == VisitSource.GameLog).ToListAsync(ct);
        if (location.IsRealInstance && open.Any(v => v.InstanceLocation == location.InstanceLocation)) return;
        foreach (var visit in open)
        {
            visit.LeftAt = now;
            published.Add(LiveEvents.Visit(me.Id, me.DisplayName, visit.InstanceLocation, VRCX.Vrchat.VrchatLocation.Parse(visit.InstanceLocation).WorldId, joined: false, VisitSource.GameLog));
        }
        if (!location.IsRealInstance) return;

        var instance = await store.EnsureInstanceAsync(db, location, now, ct);
        db.InstanceVisits.Add(new InstanceVisit { UserId = me.Id, InstanceLocation = instance.Location, JoinedAt = now, Source = VisitSource.GameLog });
        published.Add(LiveEvents.Visit(me.Id, me.DisplayName, instance.Location, instance.WorldId, joined: true, VisitSource.GameLog));
    }

    /// <summary>
    /// The original's instanceQueueReady and instance-closed handler: a notification VRChat never
    /// stores, made locally with the world and group names VRCX knows.
    /// </summary>
    private static async Task<(string WorldName, string GroupName)> LocationNamesAsync(VrcxContext db, string location, CancellationToken ct)
    {
        var parsed = VRCX.Vrchat.VrchatLocation.Parse(location);
        var worldName = parsed.WorldId.Length > 0 ? await db.Worlds.Where(w => w.Id == parsed.WorldId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "" : "";
        var groupName = parsed.GroupId is { } groupId ? await db.Groups.Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync(ct) ?? "" : "";
        return (worldName, groupName);
    }

    private static async Task<Notification> InstanceNoticeAsync(VrcxContext db, string accountId, string type, string location, DateTimeOffset now, CancellationToken ct)
    {
        var parsed = VRCX.Vrchat.VrchatLocation.Parse(location);
        var worldName = parsed.WorldId.Length > 0 ? await db.Worlds.Where(w => w.Id == parsed.WorldId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "" : "";
        var group = parsed.GroupId is { } groupId ? await db.Groups.Where(g => g.Id == groupId).Select(g => new { g.Name, g.IconUrl }).FirstOrDefaultAsync(ct) : null;
        var groupName = group?.Name ?? "";
        var details = new Dictionary<string, string> { ["location"] = location, ["worldId"] = parsed.WorldId, ["worldName"] = worldName, ["groupName"] = groupName };
        if (parsed.GroupId is { } id) details["groupId"] = id;
        var queueReady = type == "instance-queue-ready";
        return new Notification
        {
            Id = $"vrcx_{(queueReady ? "queueready" : "instanceclosed")}_{Guid.CreateVersion7():N}",
            OwnerUserId = accountId,
            Type = queueReady ? "group.queueReady" : "instance.closed",
            Message = queueReady ? $"Instance ready to join {DisplayLocation(parsed, worldName, groupName)}" : "Instance Closed",
            ImageUrl = queueReady ? group?.IconUrl : null,
            Link = queueReady && parsed.GroupId is { } linkGroup ? $"group:{linkGroup}" : null,
            Details = System.Text.Json.JsonSerializer.SerializeToDocument(details),
            CreatedAt = now,
            FetchedAt = now,
        };
    }

    /// <summary>The original's displayLocation.</summary>
    internal static string DisplayLocation(VRCX.Vrchat.VrchatLocation l, string worldName, string groupName)
    {
        if (l.IsOffline) return "Offline";
        if (l.IsPrivate) return "Private";
        if (l.IsTraveling) return "Traveling";
        if (l.WorldId.Length == 0) return worldName;
        if (groupName.Length > 0) return $"{worldName} {l.AccessTypeName}({groupName})";
        return l.InstanceId.Length > 0 ? $"{worldName} {l.AccessTypeName}" : worldName;
    }

    private async Task HandleFriendAsync(VrcxContext db, string accountId, FriendEvent e, DateTimeOffset now, List<ServerEvent> published, CancellationToken ct)
    {
        var p = e.Payload;
        var user = p.User is not null
            ? await store.UpsertUserAsync(db, p.User, now, ct)
            : await store.EnsureUserAsync(db, p.UserId, null, now, ct);
        if (p.World is not null) await store.UpsertWorldAsync(db, p.World, now, ct);
        // The original's checkNote. A note not stored yet arrives with the next notes sync.
        if (p.User is not null) await store.ApplySeenNoteAsync(db, accountId, user.Id, p.User.Note, now, ct);

        PresenceEvent? presence = null;
        switch (e.Type)
        {
            case "friend-online":
                await debouncer.CancelAsync(user.Id, ct);
                presence = await store.ApplyPresenceAsync(db, user, PresenceKind.Online, p.Location ?? p.User?.Location, p.TravelingToLocation ?? p.User?.TravelingToLocation, p.Platform, now, ct);
                break;
            case "friend-active":
            case "friend-offline":
                {
                    var kind = e.Type == "friend-active" ? PresenceKind.Active : PresenceKind.Offline;
                    // The original holds an online -> offline/active transition for 170 s and drops it if the
                    // friend comes back in time: VRChat flaps when a client reconnects.
                    var current = await store.GetPresenceAsync(db, user.Id, ct);
                    if (current.State != UserState.Online || current.PresenceChangedAt is null || !await debouncer.ScheduleAsync(user.Id, kind, p.Platform, now, ct))
                        presence = await store.ApplyPresenceAsync(db, user, kind, null, null, p.Platform, now, ct);
                    break;
                }
            case "friend-location":
                await debouncer.CancelAsync(user.Id, ct);
                presence = await store.ApplyPresenceAsync(db, user, PresenceKind.Location, p.Location ?? p.User?.Location, p.TravelingToLocation ?? p.User?.TravelingToLocation, p.Platform, now, ct);
                break;
            case "friend-add":
                {
                    var friendship = await store.AddFriendAsync(db, accountId, p.UserId, now, ct);
                    if (friendship is not null) published.Add(LiveEvents.Friendship(accountId, p.UserId, user.DisplayName, true, friendship.FriendNumber));
                    break;
                }
            case "friend-delete":
                {
                    var friendship = await store.RemoveFriendAsync(db, accountId, p.UserId, now, ct);
                    if (friendship is not null) published.Add(LiveEvents.Friendship(accountId, p.UserId, user.DisplayName, false, friendship.FriendNumber));
                    break;
                }
            // friend-update: the user upsert above is the whole job.
        }
        if (presence is not null)
        {
            published.Add(LiveEvents.Presence(user, presence));
            if (presence.InstanceLocation is not null || presence.PreviousLocation is not null)
                published.AddRange(VisitEvents(db, user, presence));
        }
    }

    private async Task HandleGroupAsync(VrcxContext db, string accountId, GroupEvent e, DateTimeOffset now, CancellationToken ct)
    {
        var p = e.Payload;
        switch (e.Type)
        {
            case "group-joined" when p.GroupId is not null:
                await store.UpsertMembershipAsync(db, p.GroupId, accountId, new VrchatStore.MembershipFields { JoinedAt = now }, now, ct);
                break;
            case "group-left" when p.GroupId is not null:
                {
                    var membership = await db.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == p.GroupId && m.UserId == accountId, ct);
                    if (membership is not null) membership.LeftAt = now;
                    break;
                }
            case "group-member-updated" when p.Member is not null:
                await store.UpsertGroupMemberAsync(db, p.Member, now, ct);
                break;
            case "group-role-updated" when p.Role is { GroupId: not null }:
                await store.UpsertGroupRoleAsync(db, p.Role.GroupId, p.Role, now, ct);
                break;
        }
    }

    /// <summary>A UserChanged event listing which change tables got a new row for this user in this unit of work.</summary>
    /// <summary>Visit open/close events for the pipeline-sourced visits this unit of work touched for the user.</summary>
    private static IEnumerable<ServerEvent> VisitEvents(VrcxContext db, User user, PresenceEvent presence)
    {
        if (!db.Presence.TryGetPreviousInstance(user.Id, out var left) || left == presence.InstanceLocation) yield break;
        if (left is not null)
            yield return LiveEvents.Visit(user.Id, user.DisplayName, left, Vrchat.VrchatLocation.Parse(left).WorldId, joined: false, VisitSource.Pipeline);
        if (presence.InstanceLocation is { } entered)
            yield return LiveEvents.Visit(user.Id, user.DisplayName, entered, Vrchat.VrchatLocation.Parse(entered).WorldId, joined: true, VisitSource.Pipeline);
    }
}
