namespace VRCX.LegacyImport;

// The game log. The original kept one game log for the PC (not per account); here every row is
// global and an account's log is the rows in the instances it stayed in, so each stay is given
// to the account that played it.
public sealed partial class LegacyImporter
{
    private static readonly TimeSpan PairTolerance = TimeSpan.FromSeconds(5);

    private sealed record Stay(DateTimeOffset At, DateTimeOffset? End, string? Instance, string Raw, string? WorldName, string? GroupName)
    {
        /// <summary>When the PC left: its own end, else the next stay's start, else its start.</summary>
        public DateTimeOffset Until { get; set; }
        public string? Account { get; set; }
    }

    private sealed record JoinLeave(DateTimeOffset At, bool Joined, string? DisplayName, string Raw, string? Instance, string? UserId, long? TimeMs);

    private List<Stay> _stays = [];

    /// <summary>The stay the PC was in at <paramref name="at"/>, if any.</summary>
    private Stay? StayAt(DateTimeOffset at)
    {
        var lo = 0;
        var hi = _stays.Count - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_stays[mid].At <= at + PairTolerance)
            {
                found = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }
        if (found < 0) return null;
        var stay = _stays[found];
        return at <= stay.Until + PairTolerance ? stay : null;
    }

    private async Task GameLogAsync()
    {
        // ---- gamelog_location: the PC's own stays ----
        var locationReport = _report.Table("gamelog_location", "stored_instance_visits (the account's stays, source game_log)");
        foreach (var r in _src.Rows("gamelog_location", "created_at", "location", "world_id", "world_name", "time", "group_name"))
        {
            locationReport.Read++;
            if (r.Time(0) is not { } at)
            {
                locationReport.Skip("no parseable created_at");
                continue;
            }
            var raw = r.String(1) ?? "";
            var location = VRCX.Vrchat.VrchatLocation.Parse(raw);
            var time = r.Long(4);
            _stays.Add(new Stay(at, time > 0 ? at + TimeSpan.FromMilliseconds(time.Value) : null, location.IsRealInstance ? location.InstanceLocation : null, raw, r.String(3), r.String(5)));
        }
        _stays = _stays.OrderBy(s => s.At).ToList();
        for (var i = 0; i < _stays.Count; i++)
            _stays[i].Until = _stays[i].End ?? (i + 1 < _stays.Count ? _stays[i + 1].At : _stays[i].At);

        // ---- gamelog_join_leave: who came and went ----
        var joinLeave = new List<JoinLeave>();
        var joinReport = _report.Table("gamelog_join_leave", "stored_instance_visits (other players, source game_log) + unidentified_visits");
        foreach (var r in _src.Rows("gamelog_join_leave", "created_at", "type", "display_name", "location", "user_id", "time"))
        {
            joinReport.Read++;
            if (r.Time(0) is not { } at)
            {
                joinReport.Skip("no parseable created_at");
                continue;
            }
            var type = r.String(1);
            if (type is not ("OnPlayerJoined" or "OnPlayerLeft"))
            {
                joinReport.Skip($"unknown type {type}");
                continue;
            }
            var raw = r.String(3) ?? "";
            var location = VRCX.Vrchat.VrchatLocation.Parse(raw);
            var userId = r.String(4) ?? ResolveName(r.String(2));
            joinLeave.Add(new JoinLeave(at, type == "OnPlayerJoined", r.String(2), raw, location.IsRealInstance ? location.InstanceLocation : null, userId, r.Long(5)));
        }

        AssignStayAccounts(joinLeave);
        await StaysAsync(locationReport);
        await PlayersAsync(joinReport, joinLeave);
        await GameLogRowsAsync();
    }

    /// <summary>
    /// The original logs the PC's own player joining every instance, so a stay belongs to the
    /// account whose join line falls in it. Stays without one (a crash, or a log line that named
    /// nobody) take the account seen nearest in time, then the fallback account.
    /// </summary>
    private void AssignStayAccounts(List<JoinLeave> joinLeave)
    {
        var accountIds = _accounts.Select(a => a.UserId).ToHashSet(StringComparer.Ordinal);
        var own = joinLeave.Where(j => j.Joined && j.UserId is not null && accountIds.Contains(j.UserId)).OrderBy(j => j.At).ToList();
        int byJoin = 0, byNearest = 0, byFallback = 0;
        foreach (var stay in _stays)
        {
            var inStay = own.FirstOrDefault(j => j.At >= stay.At - PairTolerance && j.At <= stay.Until + PairTolerance && (j.Instance == stay.Instance || j.Instance is null));
            if (inStay is not null)
            {
                stay.Account = inStay.UserId;
                byJoin++;
            }
        }
        var fallback = _options.GameLogAccountId
            ?? (_accounts.Count == 1 ? _accounts[0].UserId : null)
            ?? _stays.Where(s => s.Account is not null).GroupBy(s => s.Account).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
            ?? _accounts.FirstOrDefault()?.UserId;
        var known = _stays.Where(s => s.Account is not null).ToList();
        foreach (var stay in _stays.Where(s => s.Account is null))
        {
            // Nearest stay whose account is known, within a day: the same session at the PC.
            var nearest = known.MinBy(k => Math.Abs((k.At - stay.At).Ticks));
            if (nearest is not null && Math.Abs((nearest.At - stay.At).TotalHours) <= 24)
            {
                stay.Account = nearest.Account;
                byNearest++;
            }
            else
            {
                stay.Account = fallback;
                byFallback++;
            }
        }
        if (_stays.Count > 0)
            _report.Notes.Add($"game log stays: {byJoin} owned by the account whose own join line is in them, {byNearest} by the nearest such stay within a day, {byFallback} by the fallback account");
    }

    private async Task StaysAsync(TableReport report)
    {
        var rows = new List<object?[]>();
        foreach (var stay in _stays)
        {
            if (stay.Instance is null)
            {
                report.Skip("not an instance (local test world, offline or traveling)");
                continue;
            }
            if (stay.Account is null)
            {
                report.Skip("no account to own it");
                continue;
            }
            rows.Add([stay.Account, stay.Instance, stay.At, stay.End ?? stay.At, stay.WorldName, stay.GroupName, null]);
        }
        await VisitsAsync(report, "stage_stays", rows, own: true);
    }

    private async Task PlayersAsync(TableReport report, List<JoinLeave> joinLeave)
    {
        var accountIds = _accounts.Select(a => a.UserId).ToHashSet(StringComparer.Ordinal);
        var visits = new List<object?[]>();
        var unidentified = new List<object?[]>();
        var unidentifiedReport = _report.Table("gamelog_join_leave (players named only by display name)", "unidentified_visits");

        void Emit(JoinLeave first, DateTimeOffset joined, DateTimeOffset left)
        {
            if (left < joined) left = joined;
            if (first.UserId is not null) visits.Add([first.UserId, first.Instance, joined, left, null, null, first.DisplayName]);
            else unidentified.Add([first.DisplayName ?? "", first.Instance, joined, left]);
        }

        var usable = new List<JoinLeave>();
        foreach (var j in joinLeave)
        {
            if (j.Instance is null)
            {
                report.Skip("not an instance (local test world, or no location)");
                continue;
            }
            var stay = StayAt(j.At);
            if (j.UserId is not null && accountIds.Contains(j.UserId) && (stay is null || stay.Account == j.UserId))
            {
                report.Skip("the PC's own join/leave line: the stay itself (this server does not store it twice either)");
                continue;
            }
            if (j.UserId is null && string.IsNullOrEmpty(j.DisplayName))
            {
                report.Skip("neither a user id nor a name");
                continue;
            }
            usable.Add(j);
        }

        foreach (var group in usable.GroupBy(j => (Key: j.UserId ?? "name:" + j.DisplayName, j.Instance)))
        {
            JoinLeave? open = null;
            foreach (var j in group.OrderBy(j => j.At).ThenBy(j => j.Joined ? 1 : 0))
            {
                var openUntil = open is null ? (DateTimeOffset?)null : StayAt(open.At)?.Until;
                if (open is not null && openUntil is { } until && j.At > until + PairTolerance)
                {
                    // The PC left in between without a leave line for them (a crash): it ended with the stay.
                    Emit(open, open.At, until);
                    open = null;
                }
                if (j.Joined)
                {
                    if (open is not null) Emit(open, open.At, j.At);
                    open = j;
                }
                else if (open is not null)
                {
                    Emit(open, open.At, j.At);
                    open = null;
                }
                else
                {
                    // A leave with no join before it: its time is how long they had been there.
                    Emit(j, j.TimeMs > 0 ? j.At - TimeSpan.FromMilliseconds(j.TimeMs.Value) : j.At, j.At);
                }
            }
            if (open is not null) Emit(open, open.At, StayAt(open.At)?.Until ?? open.At);
        }

        await VisitsAsync(report, "stage_players", visits, own: false);

        unidentifiedReport.Read = unidentified.Count;
        await BulkAsync(unidentifiedReport, "stage_unidentified", [Text("display_name"), Text("instance_location"), Time("joined_at"), Time("left_at")],
            Rows(unidentified, r => _refs.Instance(r[1] as string, (DateTimeOffset)r[2]!)), async pg =>
            {
                unidentifiedReport.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO unidentified_visits (display_name, instance_location, joined_at, left_at)
                    SELECT DISTINCT ON (s.display_name, s.instance_location, s.joined_at) s.display_name, s.instance_location, s.joined_at, s.left_at
                    FROM stage_unidentified s
                    WHERE NOT EXISTS (
                        SELECT 1 FROM unidentified_visits t
                        WHERE t.display_name = s.display_name AND t.instance_location = s.instance_location
                          AND t.joined_at BETWEEN s.joined_at - interval '2 seconds' AND s.joined_at + interval '2 seconds')
                    """, _ct);
            });
    }

    private IEnumerable<object?[]> Rows(List<object?[]> rows, Action<object?[]> refs)
    {
        foreach (var row in rows)
        {
            refs(row);
            yield return row;
        }
    }

    /// <summary>
    /// Stays and player visits into stored_instance_visits. A visit this server recorded itself
    /// from the same log line (same user, instance and join time) is the same stay: it is kept,
    /// and given the leave time if it never got one.
    /// </summary>
    private async Task VisitsAsync(TableReport report, string stage, List<object?[]> rows, bool own)
    {
        StageColumn[] columns = [Text("user_id"), Text("instance_location"), Time("joined_at"), Time("left_at"), Text("world_name"), Text("group_name"), Text("display_name")];
        await BulkAsync(report, stage, columns, Rows(rows, r =>
        {
            var at = (DateTimeOffset)r[2]!;
            _refs.User((string)r[0]!, r[6] as string, at);
            _refs.Instance((string)r[1]!, at, r[4] as string, r[5] as string);
        }), async pg =>
        {
            report.Updated += await pg.ExecuteAsync($"""
                UPDATE stored_instance_visits t SET left_at = s.left_at
                FROM {stage} s
                WHERE t.user_id = s.user_id AND t.source = 'game_log' AND t.instance_location = s.instance_location
                  AND t.joined_at BETWEEN s.joined_at - interval '2 seconds' AND s.joined_at + interval '2 seconds'
                  AND t.left_at IS NULL AND s.left_at IS NOT NULL
                """, _ct);
            report.Inserted += await pg.ExecuteAsync($"""
                INSERT INTO stored_instance_visits (user_id, instance_location, joined_at, left_at, source, display_name, world_name, group_name)
                SELECT DISTINCT ON (s.user_id, s.instance_location, s.joined_at)
                       s.user_id, s.instance_location, s.joined_at, s.left_at, 'game_log'::visit_source, s.display_name, s.world_name, s.group_name
                FROM {stage} s
                WHERE NOT EXISTS (
                    SELECT 1 FROM stored_instance_visits t
                    WHERE t.user_id = s.user_id AND t.source = 'game_log' AND t.instance_location = s.instance_location
                      AND t.joined_at BETWEEN s.joined_at - interval '2 seconds' AND s.joined_at + interval '2 seconds')
                """, _ct);
        });
        if (own && _pg is not null)
            _report.Notes.Add("an account's stays were matched to ones this server recorded by account, instance and join time within 2 seconds");
    }

    /// <summary>Video plays, portal spawns, Udon resource loads and the free-form event/external lines.</summary>
    private async Task GameLogRowsAsync()
    {
        // gamelog_video_play -> video_plays
        var video = _report.Table("gamelog_video_play", "video_plays");
        var videoRows = new List<object?[]>();
        foreach (var r in _src.Rows("gamelog_video_play", "created_at", "video_url", "video_name", "video_id", "location", "display_name", "user_id"))
        {
            video.Read++;
            if (r.Time(0) is not { } at || r.String(1) is not { } url)
            {
                video.Skip("no created_at or video_url");
                continue;
            }
            var name = r.String(5);
            var userId = r.String(6) ?? ResolveName(name);
            videoRows.Add([at, r.String(4), url, r.String(2), r.String(3), name, userId]);
        }
        await BulkAsync(video, "stage_video", [Time("at"), Text("location"), Text("video_url"), Text("video_name"), Text("video_id"), Text("display_name"), Text("user_id")],
            Rows(videoRows, r =>
            {
                var at = (DateTimeOffset)r[0]!;
                r[1] = _refs.Instance(r[1] as string, at);
                if (r[6] is string u) _refs.User(u, r[5] as string, at);
            }), async pg =>
            {
                video.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO video_plays (at, instance_location, user_id, display_name, video_url, video_name, video_id)
                    SELECT DISTINCT ON (s.at, s.video_url) s.at, s.location, s.user_id, s.display_name, s.video_url, s.video_name, s.video_id
                    FROM stage_video s
                    ON CONFLICT (at, video_url) DO NOTHING
                    """, _ct);
            });

        // gamelog_portal_spawn -> portal_spawns
        var portal = _report.Table("gamelog_portal_spawn", "portal_spawns");
        var portalRows = new List<object?[]>();
        foreach (var r in _src.Rows("gamelog_portal_spawn", "created_at", "display_name", "location", "user_id", "instance_id", "world_name"))
        {
            portal.Read++;
            if (r.Time(0) is not { } at)
            {
                portal.Skip("no created_at");
                continue;
            }
            portalRows.Add([at, r.String(2), r.String(3) ?? ResolveName(r.String(1)), r.String(1), r.String(4), r.String(5)]);
        }
        await BulkAsync(portal, "stage_portal", [Time("at"), Text("location"), Text("user_id"), Text("display_name"), Text("target"), Text("target_world_name")],
            Rows(portalRows, r =>
            {
                var at = (DateTimeOffset)r[0]!;
                r[1] = _refs.Instance(r[1] as string, at);
                if (r[2] is string u) _refs.User(u, r[3] as string, at);
            }), async pg =>
            {
                portal.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO portal_spawns (at, instance_location, user_id, display_name, target_instance_location, target_world_name)
                    SELECT DISTINCT ON (s.at, s.display_name) s.at, s.location, s.user_id, s.display_name, s.target, s.target_world_name
                    FROM stage_portal s
                    WHERE NOT EXISTS (
                        SELECT 1 FROM portal_spawns t
                        WHERE t.at BETWEEN s.at - interval '2 seconds' AND s.at + interval '2 seconds'
                          AND t.instance_location IS NOT DISTINCT FROM s.location
                          AND (t.display_name IS NULL OR t.display_name IS NOT DISTINCT FROM s.display_name))
                    """, _ct);
            });

        // gamelog_resource_load -> resource_loads
        var resource = _report.Table("gamelog_resource_load", "resource_loads");
        var resourceRows = new List<object?[]>();
        foreach (var r in _src.Rows("gamelog_resource_load", "created_at", "resource_url", "resource_type", "location"))
        {
            resource.Read++;
            if (r.Time(0) is not { } at || r.String(1) is not { } url)
            {
                resource.Skip("no created_at or resource_url");
                continue;
            }
            var kind = r.String(2) switch
            {
                "ImageLoad" => "image",
                "StringLoad" => "string",
                var other => other?.ToLowerInvariant() ?? "string",
            };
            resourceRows.Add([at, r.String(3), url, kind]);
        }
        await BulkAsync(resource, "stage_resource", [Time("at"), Text("location"), Text("url"), Text("kind")],
            Rows(resourceRows, r => r[1] = _refs.Instance(r[1] as string, (DateTimeOffset)r[0]!)), async pg =>
            {
                resource.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO resource_loads (at, instance_location, url, kind)
                    SELECT DISTINCT ON (s.at, s.url) s.at, s.location, s.url, s.kind FROM stage_resource s
                    ON CONFLICT (at, url) DO NOTHING
                    """, _ct);
            });

        // gamelog_event and gamelog_external -> game_log_events (Kind Event / External)
        var events = _report.Table("gamelog_event", "game_log_events (kind Event)");
        var eventRows = new List<object?[]>();
        foreach (var r in _src.Rows("gamelog_event", "created_at", "data"))
        {
            events.Read++;
            if (r.Time(0) is not { } at || r.Raw(1) is not { } data)
            {
                events.Skip("no created_at or data");
                continue;
            }
            // The original stored no location with these; the stay the PC was in says where, as this server records it.
            eventRows.Add([at, StayAt(at)?.Instance, "Event", data, null, null]);
        }
        await GameLogEventsAsync(events, eventRows);

        var external = _report.Table("gamelog_external", "game_log_events (kind External)");
        var externalRows = new List<object?[]>();
        foreach (var r in _src.Rows("gamelog_external", "created_at", "message", "display_name", "user_id", "location"))
        {
            external.Read++;
            if (r.Time(0) is not { } at || r.Raw(1) is not { } message)
            {
                external.Skip("no created_at or message");
                continue;
            }
            externalRows.Add([at, r.String(4), "External", message, r.String(3), r.String(2)]);
        }
        await GameLogEventsAsync(external, externalRows);
    }

    private Task GameLogEventsAsync(TableReport report, List<object?[]> rows) =>
        BulkAsync(report, "stage_events", [Time("at"), Text("location"), Text("kind"), Text("data"), Text("user_id"), Text("display_name")],
            Rows(rows, r =>
            {
                var at = (DateTimeOffset)r[0]!;
                r[1] = _refs.Instance(r[1] as string, at);
                if (r[4] is string u) _refs.User(u, r[5] as string, at);
            }), async pg =>
            {
                report.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO game_log_events (at, instance_location, kind, data, user_id, display_name)
                    SELECT DISTINCT ON (s.at, s.kind, s.data) s.at, s.location, s.kind, s.data, s.user_id, s.display_name
                    FROM stage_events s
                    WHERE NOT EXISTS (SELECT 1 FROM game_log_events t WHERE t.at = s.at AND t.kind = s.kind AND t.data = s.data)
                    """, _ct);
            });
}
