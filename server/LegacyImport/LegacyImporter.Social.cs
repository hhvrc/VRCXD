using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using VRCX.Common.VrcxDb;

namespace VRCX.LegacyImport;

// Friends, the friend log, notifications and the other per-account tables.
public sealed partial class LegacyImporter
{
    private sealed record Change(string UserId, DateTimeOffset At, string Previous, string Next, string? DisplayName, string Source);

    private sealed class FriendInterval
    {
        public required string FriendId;
        public required DateTimeOffset Start;
        public DateTimeOffset? End;
        public int Number;
        public string? DisplayName;
        public string? EndedDisplayName;
    }

    private readonly List<Change> _nameChanges = [];
    private readonly List<Change> _trustChanges = [];

    /// <summary>When the account's tables start and end: VRCX began recording, and last recorded.</summary>
    private (DateTimeOffset First, DateTimeOffset Last) AccountSpan(LegacyAccount account)
    {
        DateTimeOffset? first = null, last = null;
        foreach (var (table, column) in new[]
                 {
                     ("friend_log_history", "created_at"), ("feed_gps", "created_at"), ("feed_online_offline", "created_at"), ("feed_status", "created_at"),
                     ("feed_bio", "created_at"), ("feed_avatar", "created_at"), ("notifications", "created_at"), ("notifications_v2", "created_at"),
                 })
        {
            var (min, max) = _src.TimeRange(account.Table(table), column);
            if (min is { } a && (first is null || a < first)) first = a;
            if (max is { } b && (last is null || b > last)) last = b;
        }
        return (first ?? _now, last ?? _now);
    }

    /// <summary>
    /// friend_log_history + friend_log_current -> friendships (Friend opens one, Unfriend ends it),
    /// friend_request_events, and the name and trust changes (stored once per user, after every
    /// account is read). Friends the original found already befriended when it started logging have
    /// no Friend row: they start at one shared instant just before the account's first record,
    /// which the friend log treats as the starting point, as it does this server's first sync.
    /// </summary>
    private async Task FriendLogAsync(LegacyAccount account)
    {
        var history = _report.Table(account.Table("friend_log_history"), "friendships, friend_request_events, user_display_name_changes, user_trust_changes");
        var currentReport = _report.Table(account.Table("friend_log_current"), "friendships (open), users (stub names)");
        var current = new Dictionary<string, (string? Name, string? Trust, int Number)>(StringComparer.Ordinal);
        foreach (var r in _src.Rows(account.Table("friend_log_current"), "user_id", "display_name", "trust_level", "friend_number"))
        {
            currentReport.Read++;
            if (r.String(0) is not { } id)
            {
                currentReport.Skip("no user_id");
                continue;
            }
            current[id] = (r.String(1), r.String(2), (int)(r.Long(3) ?? 0));
        }

        var (first, last) = AccountSpan(account);
        var baseline = first.AddSeconds(-1);
        var events = new List<(DateTimeOffset At, long Order, bool Friend, string UserId, string? Name, int Number)>();
        var requests = new List<object?[]>();
        long order = 0;
        foreach (var r in _src.Rows(account.Table("friend_log_history"), "created_at", "type", "user_id", "display_name", "previous_display_name", "trust_level", "previous_trust_level", "friend_number"))
        {
            history.Read++;
            order++;
            if (r.Time(0) is not { } at || r.String(2) is not { } userId)
            {
                history.Skip("no created_at or user_id");
                continue;
            }
            switch (r.String(1))
            {
                case "Friend":
                    events.Add((at, order, true, userId, r.String(3), (int)(r.Long(7) ?? 0)));
                    break;
                case "Unfriend":
                    events.Add((at, order, false, userId, r.String(3), 0));
                    break;
                case "DisplayName":
                    if (r.String(4) is { } previous && r.String(3) is { } next && previous != next) _nameChanges.Add(new Change(userId, at, previous, next, null, account.UserId));
                    else history.Skip("DisplayName row without both names");
                    break;
                case "TrustLevel":
                    if (r.String(6) is { } previousTrust && r.String(5) is { } trust && previousTrust != trust) _trustChanges.Add(new Change(userId, at, previousTrust, trust, r.String(3), account.UserId));
                    else history.Skip("TrustLevel row without both levels");
                    break;
                case "FriendRequest" or "CancelFriendRequest" or "CancelFriendRequst":
                    requests.Add([userId, r.String(3) ?? "", r.String(1) == "CancelFriendRequst" ? "CancelFriendRequest" : r.String(1), at]);
                    break;
                default:
                    history.Skip($"unknown type {r.String(1)}");
                    break;
            }
        }

        // Friend/Unfriend rows -> intervals.
        var intervals = new List<FriendInterval>();
        foreach (var group in events.GroupBy(e => e.UserId))
        {
            FriendInterval? open = null;
            foreach (var e in group.OrderBy(e => e.At).ThenBy(e => e.Order))
            {
                if (e.Friend)
                {
                    if (open is not null)
                    {
                        history.Skip("Friend row while already friends (the same friendship logged twice)");
                        continue;
                    }
                    open = new FriendInterval { FriendId = e.UserId, Start = e.At, Number = e.Number, DisplayName = e.Name };
                }
                else if (open is not null)
                {
                    open.End = e.At;
                    open.EndedDisplayName = e.Name;
                    intervals.Add(open);
                    open = null;
                }
                else
                {
                    // Befriended before the original started logging, unfriended since.
                    intervals.Add(new FriendInterval { FriendId = e.UserId, Start = baseline, End = e.At, EndedDisplayName = e.Name });
                }
            }
            if (open is not null)
            {
                if (current.TryGetValue(group.Key, out var c) && c.Number > 0 && open.Number == 0) open.Number = c.Number;
                intervals.Add(open);
            }
        }
        var openFriends = intervals.Where(i => i.End is null).Select(i => i.FriendId).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, c) in current)
        {
            if (openFriends.Contains(id)) continue;
            intervals.Add(new FriendInterval { FriendId = id, Start = baseline, Number = c.Number });
        }
        var notCurrent = intervals.Count(i => i.End is null && !current.ContainsKey(i.FriendId));
        if (notCurrent > 0) _report.Notes.Add($"{account.Table("friend_log_history")}: {notCurrent} friendships have a Friend row but are not in friend_log_current and no Unfriend row; they stay open until this server's next friend sync ends them");

        var friendships = _report.Table(account.Table("friend_log_history") + " + _current (Friend / Unfriend)", "friendships");
        friendships.Read = events.Count + current.Count;
        friendships.Candidates = intervals.Count;
        foreach (var i in intervals) _refs.User(i.FriendId, i.DisplayName ?? i.EndedDisplayName ?? current.GetValueOrDefault(i.FriendId).Name, i.Start);
        await InTransactionAsync(friendships, async pg =>
        {
            var existing = await pg.Db.Friendships.Where(f => f.OwnerUserId == account.UserId).ToListAsync(_ct);
            foreach (var i in intervals)
            {
                var mine = existing.Where(f => f.FriendUserId == i.FriendId).ToList();
                var same = mine.FirstOrDefault(f => f.StartedAt == i.Start);
                if (same is not null)
                {
                    if (same.DisplayName is null && i.DisplayName is not null) same.DisplayName = i.DisplayName;
                    if (same.EndedDisplayName is null && i.EndedDisplayName is not null) same.EndedDisplayName = i.EndedDisplayName;
                    continue;
                }
                if (i.End is null)
                {
                    // The friendship this server found on its own first sync: the original knows when it really began.
                    var later = mine.Where(f => f.StartedAt >= i.Start).MinBy(f => f.StartedAt);
                    if (later is not null)
                    {
                        later.StartedAt = i.Start;
                        if (i.Number > 0) later.FriendNumber = i.Number;
                        later.DisplayName ??= i.DisplayName;
                        friendships.Updated++;
                        continue;
                    }
                    if (mine.Any(f => f.EndedAt is null))
                    {
                        friendships.Skip("an open friendship this server recorded as starting earlier");
                        continue;
                    }
                }
                var row = new Friendship
                {
                    OwnerUserId = account.UserId, FriendUserId = i.FriendId, FriendNumber = i.Number, StartedAt = i.Start, EndedAt = i.End,
                    LastConfirmedAt = i.End ?? last, DisplayName = i.DisplayName, EndedDisplayName = i.EndedDisplayName,
                };
                pg.Db.Friendships.Add(row);
                existing.Add(row);
                friendships.Inserted++;
            }
            await pg.Db.SaveChangesAsync(_ct);
        });

        var requestReport = _report.Table(account.Table("friend_log_history") + " (FriendRequest / CancelFriendRequest)", "friend_request_events");
        await BulkAsync(requestReport, "stage_requests", [Text("target"), Text("display_name"), Text("type"), Time("at")], requests, async pg =>
        {
            requestReport.Inserted += await pg.ExecuteAsync("""
                INSERT INTO friend_request_events (owner_user_id, target_user_id, display_name, type, created_at)
                SELECT DISTINCT ON (s.target, s.type, s.at) @owner, s.target, s.display_name, s.type, s.at FROM stage_requests s
                WHERE NOT EXISTS (SELECT 1 FROM friend_request_events t WHERE t.owner_user_id = @owner AND t.target_user_id = s.target AND t.type = s.type AND t.created_at = s.at)
                """, _ct, Owner(account));
        });
    }

    private static NpgsqlParameter Owner(LegacyAccount account) => new("owner", NpgsqlDbType.Text) { Value = account.UserId };

    /// <summary>
    /// Display name and trust changes are facts about the user, so the copies every account's
    /// friend log made of the same change are stored once: a change repeating the transition just
    /// before or after it (in the file or already in the table) is the same change.
    /// </summary>
    private async Task NameAndTrustChangesAsync()
    {
        await ChangesAsync(_report.Table("*_friend_log_history (DisplayName)", "user_display_name_changes"), _nameChanges, "user_display_name_changes", "previous_display_name", "display_name",
            c => new UserDisplayNameChange { UserId = c.UserId, ChangedAt = c.At, PreviousDisplayName = c.Previous, DisplayName = c.Next });
        await ChangesAsync(_report.Table("*_friend_log_history (TrustLevel)", "user_trust_changes"), _trustChanges, "user_trust_changes", "previous_trust_level", "trust_level",
            c => new UserTrustChange { UserId = c.UserId, ChangedAt = c.At, PreviousTrustLevel = c.Previous, TrustLevel = c.Next, DisplayName = c.DisplayName });
    }

    private async Task ChangesAsync(TableReport report, List<Change> changes, string table, string previousColumn, string nextColumn, Func<Change, object> create)
    {
        report.Read = changes.Count;
        var userIds = changes.Select(c => c.UserId).Distinct().ToArray();
        List<Change> existing = [];
        if (_pg is not null && userIds.Length > 0)
            existing = await _pg.QueryAsync($"SELECT user_id, changed_at, {previousColumn}, {nextColumn} FROM {table} WHERE user_id = ANY(@ids)",
                r => new Change(r.GetString(0), r.GetFieldValue<DateTimeOffset>(1), r.GetString(2), r.GetString(3), null, ""), _ct,
                new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = userIds });

        var keep = new List<Change>();
        foreach (var user in changes.Concat(existing).GroupBy(c => c.UserId))
        {
            var sequence = user.OrderBy(c => c.At).ThenBy(c => c.Source.Length == 0 ? 0 : 1).ToList();
            for (var i = 0; i < sequence.Count; i++)
            {
                var c = sequence[i];
                if (c.Source.Length == 0) continue;
                bool Same(Change o) => o.Previous == c.Previous && o.Next == c.Next;
                // The previous transition we keep, and the next one already stored.
                var before = sequence.Take(i).LastOrDefault(o => o.Source.Length == 0 || keep.Contains(o));
                var after = sequence.Skip(i + 1).FirstOrDefault(o => o.Source.Length == 0);
                if (before is not null && Same(before) || after is not null && Same(after) && !sequence.Skip(i + 1).TakeWhile(o => o != after).Any(o => !Same(o)))
                {
                    report.Skip("the same change logged again (another account's friend log, or already stored)");
                    continue;
                }
                keep.Add(c);
            }
        }
        report.Candidates = keep.Count;
        foreach (var c in keep) _refs.User(c.UserId, c.DisplayName, c.At);
        await InTransactionAsync(report, async pg =>
        {
            foreach (var c in keep) pg.Db.Add(create(c));
            await pg.Db.SaveChangesAsync(_ct);
            report.Inserted = keep.Count;
        });
    }

    /// <summary>
    /// notifications (v1) and notifications_v2 -> notifications. v1's flat columns become the
    /// details object VRChat sends; an expired v1 is hidden (VRChat no longer has it), and it counts
    /// as seen, being history. v2's data and responses keep their place; its separate details go
    /// to v2_details. A notification this server already has keeps its own copy.
    /// </summary>
    private async Task NotificationsAsync(LegacyAccount account)
    {
        StageColumn[] columns =
        [
            Text("id"), Long("version"), Text("type"), Text("sender_user_id"), Text("sender_username"), Text("receiver_user_id"), Text("title"), Text("message"),
            Text("image_url"), Text("link"), Text("link_text"), Json("details"), Json("responses"), Json("v2_details"), Bool("seen"), Bool("hidden"),
            Time("created_at"), Time("updated_at"), Time("expires_at"),
        ];
        const string insert = """
            INSERT INTO notifications (id, owner_user_id, version, type, sender_user_id, sender_username, receiver_user_id, title, message, image_url, link, link_text,
                                       details, responses, v2_details, seen, hidden, log_hidden, created_at, updated_at, expires_at, fetched_at)
            SELECT DISTINCT ON (s.id) s.id, @owner, s.version, s.type, s.sender_user_id, s.sender_username, s.receiver_user_id, s.title, coalesce(s.message, ''), s.image_url,
                   s.link, s.link_text, s.details, s.responses, s.v2_details, s.seen, s.hidden, false, s.created_at, s.updated_at, s.expires_at, s.created_at
            FROM stage_notifications s
            ON CONFLICT (owner_user_id, id) DO NOTHING
            """;

        var v1 = _report.Table(account.Table("notifications"), "notifications (version 1)");
        var v1Rows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("notifications"), "id", "created_at", "type", "sender_user_id", "sender_username", "receiver_user_id", "message",
                     "world_id", "world_name", "image_url", "invite_message", "request_message", "response_message", "expired"))
        {
            v1.Read++;
            if (r.String(0) is not { } id || r.Time(1) is not { } at || r.String(2) is not { } type)
            {
                v1.Skip("no id, created_at or type");
                continue;
            }
            var details = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["worldId"] = r.Raw(7), ["worldName"] = r.Raw(8), ["imageUrl"] = r.Raw(9),
                ["inviteMessage"] = r.Raw(10), ["requestMessage"] = r.Raw(11), ["responseMessage"] = r.Raw(12),
            }.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToDictionary());
            v1Rows.Add([id, 1L, type, r.String(3), r.Raw(4), r.String(5), null, r.Raw(6), r.String(9), null, null, details, null, null, true, r.Bool(13), at, null, null]);
        }
        await BulkAsync(v1, "stage_notifications", columns, Rows(v1Rows, NoteSender), async pg => v1.Inserted += await pg.ExecuteAsync(insert, _ct, Owner(account)));

        var v2 = _report.Table(account.Table("notifications_v2"), "notifications (version 2)");
        var v2Rows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("notifications_v2"), "id", "created_at", "updated_at", "expires_at", "type", "link", "link_text", "message", "title",
                     "image_url", "seen", "sender_user_id", "sender_username", "data", "responses", "details"))
        {
            v2.Read++;
            if (r.String(0) is not { } id || r.Time(1) is not { } at || r.String(4) is not { } type)
            {
                v2.Skip("no id, created_at or type");
                continue;
            }
            v2Rows.Add([id, 2L, type, r.String(11), r.Raw(12), null, r.Raw(8), r.Raw(7), r.String(9), r.String(5), r.String(6),
                JsonText(r.Raw(13)), JsonText(r.Raw(14)), JsonText(r.Raw(15)), r.Bool(10), false, at, r.Time(2), r.Time(3)]);
        }
        await BulkAsync(v2, "stage_notifications", columns, Rows(v2Rows, NoteSender), async pg => v2.Inserted += await pg.ExecuteAsync(insert, _ct, Owner(account)));
    }

    private void NoteSender(object?[] row)
    {
        if (row[3] is string sender) _refs.User(sender, row[4] as string, (DateTimeOffset)row[16]!);
    }

    /// <summary>moderation, notes, avatar_history, the mutual graph and user_profile.</summary>
    private async Task AccountTablesAsync(LegacyAccount account)
    {
        // moderation: who blocked or muted the account (Photon), not the account's own moderations.
        var moderation = _report.Table(account.Table("moderation"), "moderations_against");
        var moderationRows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("moderation"), "user_id", "updated_at", "display_name", "block", "mute"))
        {
            moderation.Read++;
            if (r.String(0) is not { } userId)
            {
                moderation.Skip("no user_id");
                continue;
            }
            moderationRows.Add([userId, r.Time(1) ?? _now, r.String(2), r.Bool(3), r.Bool(4)]);
        }
        await BulkAsync(moderation, "stage_moderation", [Text("user_id"), Time("at"), Text("display_name"), Bool("block"), Bool("mute")],
            Rows(moderationRows, r => _refs.User((string)r[0]!, r[2] as string, (DateTimeOffset)r[1]!)), async pg =>
            {
                moderation.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO moderations_against (owner_user_id, user_id, display_name, block, mute, updated_at)
                    SELECT DISTINCT ON (s.user_id) @owner, s.user_id, s.display_name, s.block, s.mute, s.at FROM stage_moderation s
                    ON CONFLICT (owner_user_id, user_id) DO NOTHING
                    """, _ct, Owner(account));
            });

        // notes: the original's copy of VRChat's user notes (no note id). The next notes sync replaces them with VRChat's own rows.
        var notes = _report.Table(account.Table("notes"), "user_notes");
        var noteRows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("notes"), "user_id", "display_name", "note", "created_at"))
        {
            notes.Read++;
            if (r.String(0) is not { } userId)
            {
                notes.Skip("no user_id");
                continue;
            }
            noteRows.Add([$"legacy:{account.UserId}:{userId}", userId, r.Raw(2) ?? "", r.Time(3), r.String(1)]);
        }
        await BulkAsync(notes, "stage_notes", [Text("id"), Text("target"), Text("note"), Time("created_at"), Text("display_name")],
            Rows(noteRows, r => _refs.User((string)r[1]!, r[4] as string, r[3] as DateTimeOffset? ?? _now)), async pg =>
            {
                notes.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO user_notes (id, owner_user_id, target_user_id, note, created_at, fetched_at)
                    SELECT DISTINCT ON (s.target) s.id, @owner, s.target, s.note, s.created_at, coalesce(s.created_at, now()) FROM stage_notes s
                    WHERE NOT EXISTS (SELECT 1 FROM user_notes t WHERE t.owner_user_id = @owner AND t.target_user_id = s.target)
                    ON CONFLICT (id) DO NOTHING
                    """, _ct, Owner(account));
            });

        // avatar_history: avatars the account wore, when last and for how long -> avatar_usages.
        var history = _report.Table(account.Table("avatar_history"), "avatar_usages");
        var historyRows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("avatar_history"), "avatar_id", "created_at", "time"))
        {
            history.Read++;
            if (r.String(0) is not { } avatarId || r.Time(1) is not { } at)
            {
                history.Skip("no avatar_id or created_at");
                continue;
            }
            historyRows.Add([avatarId, at, Milliseconds(r.Long(2)) ?? TimeSpan.Zero]);
        }
        await BulkAsync(history, "stage_avatar_history", [Text("avatar_id"), Time("at"), Interval("time")],
            Rows(historyRows, r => _refs.Avatar((string)r[0]!, (DateTimeOffset)r[1]!)), async pg =>
            {
                history.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO avatar_usages (owner_user_id, avatar_id, last_used_at, time_spent)
                    SELECT DISTINCT ON (s.avatar_id) @owner, s.avatar_id, s.at, s.time FROM stage_avatar_history s
                    ON CONFLICT (owner_user_id, avatar_id) DO NOTHING
                    """, _ct, Owner(account));
                // Worn under both apps: the later use, and the longer of the two times (adding them would count a re-import twice).
                history.Updated += await pg.ExecuteAsync("""
                    UPDATE avatar_usages t SET last_used_at = GREATEST(t.last_used_at, s.at), time_spent = GREATEST(t.time_spent, s.time)
                    FROM stage_avatar_history s
                    WHERE t.owner_user_id = @owner AND t.avatar_id = s.avatar_id AND (t.last_used_at < s.at OR t.time_spent < s.time)
                    """, _ct, Owner(account));
            });

        await MutualGraphAsync(account);
        await UserProfileAsync(account);
    }

    /// <summary>
    /// mutual_graph_links -> mutual_friend_links and mutual_graph_meta -> mutual_graph_meta. The
    /// original's mutual_graph_friends (the friends fetched) is what links and meta already say:
    /// a friend with links or a fetch time. A friend in it with neither had zero mutuals at an
    /// unrecorded time, which this server shows the same way (no mutuals).
    /// </summary>
    private async Task MutualGraphAsync(LegacyAccount account)
    {
        var meta = new Dictionary<string, (DateTimeOffset At, bool OptedOut)>(StringComparer.Ordinal);
        var metaReport = _report.Table(account.Table("mutual_graph_meta"), "mutual_graph_meta");
        foreach (var r in _src.Rows(account.Table("mutual_graph_meta"), "friend_id", "last_fetched_at", "opted_out"))
        {
            metaReport.Read++;
            if (r.String(0) is not { } friendId || r.Time(1) is not { } at)
            {
                metaReport.Skip("no friend_id or last_fetched_at");
                continue;
            }
            meta[friendId] = (at, r.Bool(2));
        }
        var (_, last) = AccountSpan(account);

        var linkReport = _report.Table(account.Table("mutual_graph_links"), "mutual_friend_links");
        var linkRows = new List<object?[]>();
        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in _src.Rows(account.Table("mutual_graph_links"), "friend_id", "mutual_id"))
        {
            linkReport.Read++;
            if (r.String(0) is not { } friendId || r.String(1) is not { } mutualId)
            {
                linkReport.Skip("no friend_id or mutual_id");
                continue;
            }
            linked.Add(friendId);
            linkRows.Add([friendId, mutualId, meta.TryGetValue(friendId, out var m) ? m.At : last]);
        }
        await BulkAsync(linkReport, "stage_links", [Text("friend_id"), Text("mutual_id"), Time("at")],
            Rows(linkRows, r =>
            {
                _refs.User((string)r[0]!, null, (DateTimeOffset)r[2]!);
                _refs.User((string)r[1]!, null, (DateTimeOffset)r[2]!);
            }), async pg =>
            {
                linkReport.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO mutual_friend_links (owner_user_id, friend_user_id, mutual_user_id, fetched_at)
                    SELECT DISTINCT ON (s.friend_id, s.mutual_id) @owner, s.friend_id, s.mutual_id, s.at FROM stage_links s
                    ON CONFLICT (owner_user_id, friend_user_id, mutual_user_id) DO NOTHING
                    """, _ct, Owner(account));
            });

        await BulkAsync(metaReport, "stage_meta", [Text("friend_id"), Time("at"), Bool("opted_out")],
            Rows(meta.Select(kv => new object?[] { kv.Key, kv.Value.At, kv.Value.OptedOut }).ToList(), r => _refs.User((string)r[0]!, null, (DateTimeOffset)r[1]!)), async pg =>
            {
                metaReport.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO mutual_graph_meta (owner_user_id, friend_user_id, last_fetched_at, opted_out)
                    SELECT @owner, s.friend_id, s.at, s.opted_out FROM stage_meta s
                    ON CONFLICT (owner_user_id, friend_user_id) DO NOTHING
                    """, _ct, Owner(account));
            });

        var friendsReport = _report.Table(account.Table("mutual_graph_friends"), "(represented by mutual_friend_links / mutual_graph_meta)");
        foreach (var r in _src.Rows(account.Table("mutual_graph_friends"), "friend_id"))
        {
            friendsReport.Read++;
            var id = r.String(0);
            if (id is not null && (linked.Contains(id) || meta.ContainsKey(id))) friendsReport.Candidates++;
            else friendsReport.Skip("fetched with no mutuals at a time the original did not record: nothing to store beyond the friendship itself");
        }
        if (_pg is not null) friendsReport.Inserted = 0;
    }

    /// <summary>user_profile: the original's cache of profile bios -> users.bio, where this server has not read a newer one.</summary>
    private async Task UserProfileAsync(LegacyAccount account)
    {
        var report = _report.Table(account.Table("user_profile"), "users (bio, bio_links, profile_fetched_at)");
        var rows = new List<object?[]>();
        foreach (var r in _src.Rows(account.Table("user_profile"), "user_id", "bio", "bio_links", "updated_at"))
        {
            report.Read++;
            if (r.String(0) is not { } userId || r.Time(3) is not { } at)
            {
                report.Skip("no user_id or updated_at");
                continue;
            }
            rows.Add([userId, r.Raw(1), StringArray(r.Raw(2)), at]);
        }
        await BulkAsync(report, "stage_profile", [Text("user_id"), Text("bio"), TextArray("bio_links"), Time("at")],
            Rows(rows, r => _refs.User((string)r[0]!, null, (DateTimeOffset)r[3]!)), async pg =>
            {
                report.Updated += await pg.ExecuteAsync("""
                    UPDATE users u SET bio = s.bio, bio_links = s.bio_links, profile_fetched_at = s.at
                    FROM stage_profile s
                    WHERE u.id = s.user_id AND (u.profile_fetched_at IS NULL OR u.profile_fetched_at < s.at)
                    """, _ct);
            });
    }
}
