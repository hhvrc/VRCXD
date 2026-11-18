using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.Live;
using VRCX.Common.Live.Topics;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Features.Notifications;

/// <summary>
/// The "notifications" topic: an account's newest notifications as GET notifications returns them
/// with includeHidden (the rows come from the same <see cref="NotificationQueries.Notifications"/>),
/// streamed as whole-row upserts and removals. <see cref="NotificationWriter"/> says which changed.
/// <para>
/// The state is the newest <see cref="Cap"/> rows, the first page REST returns: an account that
/// has run for years has more than a snapshot should carry, and what the bell and the pop-ups show
/// is recent. A row that is older than that is not sent, also not when it changes; the
/// Notifications page reads older pages over REST. A row already sent stays in the client's copy
/// when newer ones push it past the cap, until it changes or the client subscribes again.
/// </para>
/// <para>
/// A row shows its sender's current name. A sender changing their name does not send their rows
/// again; they carry the new name from the next time they change, or after a new subscription.
/// </para>
/// </summary>
public sealed class NotificationsTopic(IDbContextFactory<VrcxContext> dbFactory, ILogger<NotificationsTopic> logger)
    : AccountRowsTopic<NotificationEntry>(logger)
{
    public const string TopicName = "notifications";

    /// <summary>How many of the newest notifications the topic carries: the largest page GET notifications serves.</summary>
    public const int Cap = 500;

    public override string Name => TopicName;

    protected override string KeyOf(NotificationEntry row) => row.Id;

    protected override async Task<List<NotificationEntry>> ReadAsync(string accountId, IReadOnlyCollection<string>? keys, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var newest = new NotificationQueries(db).Notifications(accountId, includeHidden: true);
        if (keys is null) return (await newest.Take(Cap).ToListAsync(ct)).Select(Entry).ToList();

        // Just the changed rows, and of those only the ones among the newest Cap: the time of the
        // oldest of those is the floor. Fewer than Cap rows means there is no floor.
        var floor = await newest.Skip(Cap - 1).Select(n => (DateTimeOffset?)n.CreatedAt).FirstOrDefaultAsync(ct);
        var changed = newest.Where(n => keys.Contains(n.Id));
        if (floor is { } oldest) changed = changed.Where(n => n.CreatedAt >= oldest);
        return (await changed.ToListAsync(ct)).Select(Entry).ToList();
    }

    protected override TopicSnapshot ToSnapshot(IReadOnlyCollection<NotificationEntry> rows)
    {
        var snapshot = new NotificationsSnapshot();
        snapshot.Notifications.AddRange(Ordered(rows));
        return new TopicSnapshot { Notifications = snapshot };
    }

    protected override TopicDelta ToDelta(IReadOnlyCollection<NotificationEntry> upserts, IReadOnlyCollection<string> removedKeys)
    {
        var delta = new NotificationsDelta();
        delta.Upserts.AddRange(Ordered(upserts));
        delta.RemovedIds.AddRange(removedKeys);
        return new TopicDelta { Notifications = delta };
    }

    /// <summary>Newest first, then by id: the order GET notifications lists them in.</summary>
    private static IEnumerable<NotificationEntry> Ordered(IEnumerable<NotificationEntry> rows) =>
        rows.OrderByDescending(r => r.CreatedAt.ToDateTimeOffset()).ThenBy(r => r.Id, StringComparer.Ordinal);

    /// <summary>The wire shape of one GET notifications row.</summary>
    public static NotificationEntry Entry(NotificationResponse n)
    {
        var entry = new NotificationEntry
        {
            Id = n.Id,
            Version = n.Version,
            Type = n.Type,
            SenderUserId = n.SenderUserId ?? "",
            SenderDisplayName = n.SenderDisplayName ?? "",
            Title = n.Title ?? "",
            Message = n.Message,
            ImageUrl = n.ImageUrl ?? "",
            Link = n.Link ?? "",
            LinkText = n.LinkText ?? "",
            DetailsJson = n.Details?.RootElement.GetRawText() ?? "",
            ResponsesJson = n.Responses?.RootElement.GetRawText() ?? "",
            Seen = n.Seen,
            Hidden = n.Hidden,
            CreatedAt = Timestamp.FromDateTimeOffset(n.CreatedAt),
        };
        if (n.ExpiresAt is { } expires) entry.ExpiresAt = Timestamp.FromDateTimeOffset(expires);
        return entry;
    }
}
