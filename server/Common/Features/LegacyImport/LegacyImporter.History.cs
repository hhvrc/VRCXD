using Npgsql;
using NpgsqlTypes;

namespace VRCX.Common.Features.LegacyImport;

// Replacing this server's history with the file's (LegacyImportOptions.ReplaceHistory). What the
// original logged as it watched (where friends went, what they changed, who came and went in the
// game) has no key: two apps watching the same friend log the same move seconds apart, and
// differently when one missed it. The import matches such rows within a window, which can leave a
// pair or drop a real row. Asked to, it deletes this server's rows of each kind up to the newest
// one the file has, and the file's rows take their place. What the server recorded after the file
// was taken stays, and so does everything with a real key (notifications, memos, favorites).
public sealed partial class LegacyImporter
{
    private sealed record HistoryKind(string Name, string Table, string Column, string? Where, DateTimeOffset? Until, NpgsqlParameter? Owner = null);

    private async Task ReplaceHistoryAsync()
    {
        DateTimeOffset? Latest(params string[] tables)
        {
            DateTimeOffset? latest = null;
            foreach (var table in tables)
                foreach (var r in _src.Rows(table, "created_at"))
                    if (r.Time(0) is { } at && (latest is null || at > latest)) latest = at;
            return latest;
        }
        string[] Feeds(string feed) => _accounts.Select(a => a.Table(feed)).ToArray();

        var friendLogs = Feeds("friend_log_history");
        List<HistoryKind> kinds =
        [
            new("friends' locations and online/offline", "presence_events", "at", null, Latest([.. Feeds("feed_gps"), .. Feeds("feed_online_offline")])),
            new("status changes", "user_status_changes", "changed_at", null, Latest(Feeds("feed_status"))),
            new("bio changes", "user_bio_changes", "changed_at", null, Latest(Feeds("feed_bio"))),
            new("avatar changes", "user_avatar_changes", "changed_at", null, Latest(Feeds("feed_avatar"))),
            new("display name changes", "user_display_name_changes", "changed_at", null, Latest(friendLogs)),
            new("trust changes", "user_trust_changes", "changed_at", null, Latest(friendLogs)),
            new("game log visits", "stored_instance_visits", "joined_at", "source = 'game_log'", Latest("gamelog_location", "gamelog_join_leave")),
            new("unidentified game log visits", "unidentified_visits", "joined_at", null, Latest("gamelog_join_leave")),
            new("video plays", "video_plays", "at", null, Latest("gamelog_video_play")),
            new("portal spawns", "portal_spawns", "at", null, Latest("gamelog_portal_spawn")),
            new("resource loads", "resource_loads", "at", null, Latest("gamelog_resource_load")),
            // Only the kinds the file has: the server's own game log records others the original did not.
            new("game log events", "game_log_events", "at", "kind IN ('Event', 'External')", Latest("gamelog_event", "gamelog_external")),
        ];
        foreach (var account in _accounts)
            kinds.Add(new($"friend requests of {account.UserId}", "friend_request_events", "created_at", "owner_user_id = @owner",
                Latest(account.Table("friend_log_history")), Owner(account)));

        if (_pg is null)
        {
            foreach (var kind in kinds.Where(k => k.Until is not null))
                _report.Notes.Add($"replace history: would delete this server's {kind.Name} up to {kind.Until:u}, the newest the file has");
            return;
        }

        await _pg.BeginAsync(_ct);
        try
        {
            foreach (var kind in kinds.Where(k => k.Until is not null))
            {
                var until = new NpgsqlParameter("until", NpgsqlDbType.TimestampTz) { Value = kind.Until!.Value.UtcDateTime };
                var where = kind.Where is null ? "" : $" AND {kind.Where}";
                NpgsqlParameter[] parameters = kind.Owner is null ? [until] : [until, kind.Owner];
                var deleted = await _pg.ExecuteAsync($"DELETE FROM {kind.Table} WHERE {kind.Column} <= @until{where}", _ct, parameters);
                _report.Notes.Add($"replace history: deleted {deleted} of this server's {kind.Name} up to {kind.Until:u}, the newest the file has");
            }
            await _pg.CommitAsync(_ct);
        }
        catch
        {
            await _pg.RollbackAsync();
            throw;
        }
    }
}
