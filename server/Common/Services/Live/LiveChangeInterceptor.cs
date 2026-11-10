using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Live;

/// <summary>
/// Pushes every saved change to cached users, worlds, groups and avatars to the browsers, whichever
/// path made it: the pipeline, a sync, a page's refresh, or the scheduled profile and stub refreshes.
/// The events are read from the change tracker before the save and published once it commits.
/// </summary>
public sealed class LiveChangeInterceptor(ILiveEventBus bus) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<ServerEvent>> _pending = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Publish(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Publish(eventData.Context);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is { } context) _pending.Remove(context);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context is not VrcxContext db) return;
        _pending.AddOrUpdate(db, Changes(db));
    }

    private void Publish(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var events)) return;
        _pending.Remove(context);
        foreach (var e in events) bus.Publish(e);
    }

    /// <summary>A UserChanged per user with new history rows, and a ContentChanged per world, group or avatar written.</summary>
    public static List<ServerEvent> Changes(VrcxContext db)
    {
        var users = new Dictionary<string, HashSet<string>>();
        var content = new List<ServerEvent>();
        var seen = new HashSet<(string, string)>();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                var (userId, what) = entry.Entity switch
                {
                    UserDisplayNameChange c => (c.UserId, "display_name"),
                    UserTrustChange c => (c.UserId, "trust"),
                    UserStatusChange c => (c.UserId, "status"),
                    UserBioChange c => (c.UserId, "bio"),
                    UserAvatarChange c => (c.UserId, "avatar"),
                    _ => ((string?)null, ""),
                };
                if (userId is not null)
                {
                    if (!users.TryGetValue(userId, out var set)) users[userId] = set = [];
                    set.Add(what);
                    continue;
                }
            }
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            // A refetch that only moves bookkeeping timestamps is not a change anyone sees, and
            // announcing it would make a page that reloads from VRChat on this event loop forever.
            if (entry.State == EntityState.Modified && !entry.Properties.Any(p => p.IsModified && !p.Metadata.Name.EndsWith("FetchedAt") && p.Metadata.Name != "LastSeenAt")) continue;
            var item = entry.Entity switch
            {
                World w => ("world", w.Id, w.Name),
                Group g => ("group", g.Id, g.Name),
                Avatar a => ("avatar", a.Id, a.Name),
                _ => default((string, string, string)?),
            };
            if (item is { } i && seen.Add((i.Item1, i.Item2))) content.Add(LiveEvents.Content(i.Item1, i.Item2, i.Item3));
        }
        var events = new List<ServerEvent>(users.Count + content.Count);
        foreach (var (userId, changed) in users)
        {
            var name = db.ChangeTracker.Entries<User>().FirstOrDefault(u => u.Entity.Id == userId)?.Entity.DisplayName ?? "";
            events.Add(LiveEvents.UserChanged(userId, name, changed));
        }
        events.AddRange(content);
        return events;
    }
}
