using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Presence;

/// <summary>Works out current presence from the permanent log in Postgres.</summary>
public sealed class PresenceEventReader(IDbContextFactory<VrcxContext> dbFactory)
{
    // PreviousLocation already skips "traveling", so the newest event that changed location is the arrival.
    // One newest-first walk of the (user_id, at) index per user, rather than sorting the whole log: the
    // log only grows, and this runs at every start.
    private const string Sql = """
        SELECT u.id AS "UserId", l.kind::text AS "Kind", l.at AS "At", l.location AS "Location",
               l.traveling_to_location AS "TravelingToLocation", l.instance_location AS "InstanceLocation",
               l.platform AS "Platform", a.at AS "ArrivedAt", s.at AS "StateSince"
        FROM users u
        CROSS JOIN LATERAL (
            SELECT * FROM presence_events p WHERE p.user_id = u.id ORDER BY p.at DESC, p.id DESC LIMIT 1
        ) l
        LEFT JOIN LATERAL (
            SELECT p.at FROM presence_events p
            WHERE p.user_id = u.id AND p.previous_location IS DISTINCT FROM p.location
            ORDER BY p.at DESC, p.id DESC LIMIT 1
        ) a ON true
        LEFT JOIN LATERAL (
            SELECT p.at FROM presence_events p
            WHERE p.user_id = u.id AND p.previous_state IS DISTINCT FROM
                (CASE p.kind WHEN 'offline' THEN 'offline' WHEN 'active' THEN 'active' ELSE 'online' END)::user_state
            ORDER BY p.at DESC, p.id DESC LIMIT 1
        ) s ON true
        WHERE @ids IS NULL OR u.id = ANY(@ids)
        """;

    private sealed class Row
    {
        public required string UserId { get; init; }
        public required string Kind { get; init; }
        public required DateTimeOffset At { get; init; }
        public string? Location { get; init; }
        public string? TravelingToLocation { get; init; }
        public string? InstanceLocation { get; init; }
        public string? Platform { get; init; }
        public DateTimeOffset? ArrivedAt { get; init; }
        public DateTimeOffset? StateSince { get; init; }
    }

    /// <summary>The presence the log implies, for the given users or everyone when null.</summary>
    public async Task<Dictionary<string, UserPresence>> ReadAsync(IReadOnlyCollection<string>? userIds, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Everyone at once (the start-up rebuild) can outlast EF's default 30 seconds on a long log.
        if (userIds is null) db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        var ids = new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = userIds is null ? DBNull.Value : userIds.ToArray() };
        var rows = await db.Database.SqlQueryRaw<Row>(Sql, ids).ToListAsync(ct);
        return rows.ToDictionary(r => r.UserId, r => new UserPresence
        {
            State = r.Kind switch { "offline" => UserState.Offline, "active" => UserState.Active, _ => UserState.Online },
            Location = r.Location,
            TravelingToLocation = r.TravelingToLocation,
            CurrentInstanceLocation = r.InstanceLocation,
            PresenceChangedAt = r.At,
            LocationSince = r.ArrivedAt ?? r.At,
            StateSince = r.StateSince ?? r.At,
            Platform = r.Platform,
        }, StringComparer.Ordinal);
    }
}
