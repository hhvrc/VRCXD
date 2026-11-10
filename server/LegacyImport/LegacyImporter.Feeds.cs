using VRCX.Common.Services.Presence;

namespace VRCX.LegacyImport;

// The friend feeds: where friends went (GPS, online/offline) and what they changed (status, bio,
// avatar). The original kept a copy per account; here they are facts about the friend, stored once.
public sealed partial class LegacyImporter
{
    private async Task FeedsAsync(LegacyAccount account)
    {
        await PresenceAsync(account);
        await StatusAsync(account);
        await BioAsync(account);
        await AvatarFeedAsync(account);
    }

    /// <summary>
    /// feed_gps and feed_online_offline -> presence_events, the way VrchatStore.ApplyPresenceAsync
    /// writes them: GPS is a location event from previous_location (time = how long they were
    /// there), Online an online event at the location, Offline an offline event leaving the
    /// location (time = how long they were there). The original's world and group names are kept
    /// with the row; for Offline they name the place left.
    /// </summary>
    private async Task PresenceAsync(LegacyAccount account)
    {
        var gps = _report.Table(account.Table("feed_gps"), "presence_events (kind location)");
        var onlineOffline = _report.Table(account.Table("feed_online_offline"), "presence_events (kind online / offline)");
        var rows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("feed_gps"), "created_at", "user_id", "display_name", "location", "world_name", "previous_location", "time", "group_name"))
        {
            gps.Read++;
            if (r.Time(0) is not { } at || r.String(1) is not { } userId)
            {
                gps.Skip("no created_at or user_id");
                continue;
            }
            var location = r.Raw(3) ?? "";
            rows.Add([userId, at, "location", location, r.String(5), location, "online", Milliseconds(r.Long(6)), r.String(2), r.String(4), r.String(7)]);
        }
        var gpsCount = rows.Count;
        foreach (var r in _src.Rows(account.Table("feed_online_offline"), "created_at", "user_id", "display_name", "type", "location", "world_name", "time", "group_name"))
        {
            onlineOffline.Read++;
            if (r.Time(0) is not { } at || r.String(1) is not { } userId)
            {
                onlineOffline.Skip("no created_at or user_id");
                continue;
            }
            var location = r.Raw(4) ?? "";
            switch (r.String(3))
            {
                case "Online":
                    rows.Add([userId, at, "online", location, null, location, "offline", null, r.String(2), r.String(5), r.String(7)]);
                    break;
                case "Offline":
                    rows.Add([userId, at, "offline", "offline", location, null, "online", Milliseconds(r.Long(6)), r.String(2), r.String(5), r.String(7)]);
                    break;
                default:
                    onlineOffline.Skip($"unknown type {r.String(3)}");
                    break;
            }
        }

        StageColumn[] columns =
        [
            Text("user_id"), Time("at"), Text("kind"), Text("location"), Text("previous_location"), Text("instance_location"), Text("previous_state"),
            Interval("duration"), Text("display_name"), Text("world_name"), Text("group_name"),
        ];
        var counts = new TableReport("", "");
        await BulkAsync(counts, "stage_presence", columns, Rows(rows, r =>
        {
            var at = (DateTimeOffset)r[1]!;
            _refs.User((string)r[0]!, r[8] as string, at);
            r[5] = r[5] is string tag ? _refs.Instance(tag, at, r[9] as string, r[10] as string) : null;
        }), async pg =>
        {
            var months = await pg.QueryAsync("SELECT DISTINCT date_trunc('month', at, 'UTC') FROM stage_presence", rd => rd.GetFieldValue<DateTimeOffset>(0), _ct);
            foreach (var month in months) await PresencePartitionService.EnsureMonthAsync(pg.Db, month, _ct);
            counts.Inserted += await pg.ExecuteAsync("""
                INSERT INTO presence_events (user_id, at, kind, location, previous_location, instance_location, previous_state, duration_at_previous, display_name, world_name, group_name)
                SELECT DISTINCT ON (s.user_id, s.at, s.kind)
                       s.user_id, s.at, s.kind::presence_kind, s.location, s.previous_location, s.instance_location, s.previous_state::user_state, s.duration,
                       s.display_name, s.world_name, s.group_name
                FROM stage_presence s
                WHERE NOT EXISTS (
                    SELECT 1 FROM presence_events t
                    WHERE t.user_id = s.user_id AND t.at BETWEEN s.at - @w AND s.at + @w
                      AND (t.kind = s.kind::presence_kind OR (s.kind = 'offline' AND t.kind = 'active'))
                      AND (s.kind <> 'location' OR t.location IS NOT DISTINCT FROM s.location))
                """, _ct, Window());
        });
        // One staging table carried both feeds; the insert count is shared out in proportion.
        gps.Candidates += gpsCount;
        onlineOffline.Candidates += rows.Count - gpsCount;
        if (counts.Inserted is { } inserted)
        {
            var gpsShare = rows.Count == 0 ? 0 : (long)Math.Round(inserted * (double)gpsCount / rows.Count);
            gps.Inserted = (gps.Inserted ?? 0) + gpsShare;
            onlineOffline.Inserted = (onlineOffline.Inserted ?? 0) + inserted - gpsShare;
            if (inserted != rows.Count && inserted != 0)
                _report.Notes.Add($"{account.Table("feed_gps")}/{account.Table("feed_online_offline")}: inserted is one count for both tables ({inserted}), split in proportion");
        }
    }

    /// <summary>feed_status -> user_status_changes.</summary>
    private async Task StatusAsync(LegacyAccount account)
    {
        var report = _report.Table(account.Table("feed_status"), "user_status_changes");
        var rows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("feed_status"), "created_at", "user_id", "display_name", "status", "status_description", "previous_status", "previous_status_description"))
        {
            report.Read++;
            if (r.Time(0) is not { } at || r.String(1) is not { } userId)
            {
                report.Skip("no created_at or user_id");
                continue;
            }
            rows.Add([userId, at, r.Raw(3) ?? "", r.Raw(4), r.Raw(5) ?? "", r.Raw(6), r.String(2)]);
        }
        await BulkAsync(report, "stage_status",
            [Text("user_id"), Time("at"), Text("status"), Text("status_description"), Text("previous_status"), Text("previous_status_description"), Text("display_name")],
            Rows(rows, r => _refs.User((string)r[0]!, r[6] as string, (DateTimeOffset)r[1]!)), async pg =>
            {
                report.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO user_status_changes (user_id, changed_at, status, status_description, previous_status, previous_status_description, display_name)
                    SELECT DISTINCT ON (s.user_id, s.at) s.user_id, s.at, s.status, s.status_description, s.previous_status, s.previous_status_description, s.display_name
                    FROM stage_status s
                    WHERE NOT EXISTS (
                        SELECT 1 FROM user_status_changes t
                        WHERE t.user_id = s.user_id AND t.changed_at BETWEEN s.at - @w AND s.at + @w
                          AND t.status = s.status AND coalesce(t.status_description, '') = coalesce(s.status_description, ''))
                    """, _ct, Window());
            });
    }

    /// <summary>feed_bio -> user_bio_changes (bio_links, JSON in the original, become text[]).</summary>
    private async Task BioAsync(LegacyAccount account)
    {
        var report = _report.Table(account.Table("feed_bio"), "user_bio_changes");
        var rows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("feed_bio"), "created_at", "user_id", "display_name", "bio", "previous_bio", "bio_links", "previous_bio_links"))
        {
            report.Read++;
            if (r.Time(0) is not { } at || r.String(1) is not { } userId)
            {
                report.Skip("no created_at or user_id");
                continue;
            }
            rows.Add([userId, at, r.Raw(3), StringArray(r.Raw(5)), r.Raw(4), StringArray(r.Raw(6)), r.String(2)]);
        }
        await BulkAsync(report, "stage_bio",
            [Text("user_id"), Time("at"), Text("bio"), TextArray("bio_links"), Text("previous_bio"), TextArray("previous_bio_links"), Text("display_name")],
            Rows(rows, r => _refs.User((string)r[0]!, r[6] as string, (DateTimeOffset)r[1]!)), async pg =>
            {
                report.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO user_bio_changes (user_id, changed_at, bio, bio_links, previous_bio, previous_bio_links, display_name)
                    SELECT DISTINCT ON (s.user_id, s.at) s.user_id, s.at, s.bio, s.bio_links, s.previous_bio, s.previous_bio_links, s.display_name
                    FROM stage_bio s
                    WHERE NOT EXISTS (
                        SELECT 1 FROM user_bio_changes t
                        WHERE t.user_id = s.user_id AND t.changed_at BETWEEN s.at - @w AND s.at + @w
                          AND coalesce(t.bio, '') = coalesce(s.bio, ''))
                    """, _ct, Window());
            });
    }

    /// <summary>feed_avatar -> user_avatar_changes; the avatar id is filled when the image belongs to a known avatar, as the server does.</summary>
    private async Task AvatarFeedAsync(LegacyAccount account)
    {
        var report = _report.Table(account.Table("feed_avatar"), "user_avatar_changes");
        var rows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("feed_avatar"), "created_at", "user_id", "display_name", "owner_id", "avatar_name",
                     "current_avatar_image_url", "current_avatar_thumbnail_image_url", "previous_current_avatar_image_url", "previous_current_avatar_thumbnail_image_url"))
        {
            report.Read++;
            if (r.Time(0) is not { } at || r.String(1) is not { } userId)
            {
                report.Skip("no created_at or user_id");
                continue;
            }
            rows.Add([userId, at, r.Raw(5), r.Raw(6), r.Raw(7), r.Raw(8), r.Raw(4), r.Raw(3), r.String(2)]);
        }
        await BulkAsync(report, "stage_avatar",
            [Text("user_id"), Time("at"), Text("image_url"), Text("thumbnail_image_url"), Text("previous_image_url"), Text("previous_thumbnail_image_url"),
             Text("avatar_name"), Text("avatar_owner_id"), Text("display_name")],
            Rows(rows, r => _refs.User((string)r[0]!, r[8] as string, (DateTimeOffset)r[1]!)), async pg =>
            {
                report.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO user_avatar_changes (user_id, changed_at, image_url, thumbnail_image_url, previous_image_url, previous_thumbnail_image_url,
                                                     avatar_name, avatar_owner_id, avatar_id, display_name)
                    SELECT DISTINCT ON (s.user_id, s.at) s.user_id, s.at, s.image_url, s.thumbnail_image_url, s.previous_image_url, s.previous_thumbnail_image_url,
                           s.avatar_name, s.avatar_owner_id, (SELECT a.id FROM avatars a WHERE a.image_url = s.image_url LIMIT 1), s.display_name
                    FROM stage_avatar s
                    WHERE NOT EXISTS (
                        SELECT 1 FROM user_avatar_changes t
                        WHERE t.user_id = s.user_id AND t.changed_at BETWEEN s.at - @w AND s.at + @w
                          AND t.image_url IS NOT DISTINCT FROM s.image_url)
                    """, _ct, Window());
            });
    }
}
