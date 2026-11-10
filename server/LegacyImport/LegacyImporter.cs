using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using VRCX.Common.VrcxDb;

namespace VRCX.LegacyImport;

public sealed record LegacyImportOptions
{
    /// <summary>Path of VRCX.sqlite3. Opened read-only.</summary>
    public required string SqlitePath { get; init; }
    /// <summary>Postgres connection string. Ignored (and may be null) in a dry run.</summary>
    public string? ConnectionString { get; init; }
    /// <summary>Only read the source and report what would be imported. Postgres is never contacted.</summary>
    public bool DryRun { get; init; }
    /// <summary>
    /// Who owns a game log stay when the log does not say. The original kept one game log for every
    /// account; each stay is given to the account whose own join line is in it, else the account
    /// seen nearest in time, else this one (default: the account with the most stays).
    /// </summary>
    public string? GameLogAccountId { get; init; }
    /// <summary>
    /// How far apart an imported feed row and one this server recorded itself may be and still be
    /// the same event (both apps watching the same friend). Exact re-imports always match.
    /// </summary>
    public TimeSpan MatchWindow { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Carries the original VRCX's SQLite database into this server's Postgres, table by table, see
/// README.md for the mapping. Idempotent: every row is matched against what is already there by
/// its natural key (or, for rows this server may have recorded on its own, by the same event
/// within <see cref="LegacyImportOptions.MatchWindow"/>), so running it twice, or again after a
/// failure, adds nothing twice.
/// </summary>
public sealed partial class LegacyImporter
{
    private static readonly StageColumn[] NoColumns = [];

    private readonly LegacyImportOptions _options;
    private readonly LegacyDatabase _src;
    private readonly PgTarget? _pg;
    private readonly ImportReport _report;
    private readonly References _refs = new();
    private readonly CancellationToken _ct;
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private IReadOnlyList<LegacyAccount> _accounts = [];
    private readonly HashSet<string> _createdAccounts = new(StringComparer.Ordinal);

    // display name -> every user id seen with it; user id -> its latest name
    private readonly Dictionary<string, HashSet<string>> _idsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Name, DateTimeOffset At)> _latestName = new(StringComparer.Ordinal);

    private LegacyImporter(LegacyImportOptions options, LegacyDatabase src, PgTarget? pg, CancellationToken ct)
    {
        _options = options;
        _src = src;
        _pg = pg;
        _ct = ct;
        _report = new ImportReport { DryRun = pg is null };
    }

    public static async Task<ImportReport> RunAsync(LegacyImportOptions options, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        using var src = LegacyDatabase.Open(options.SqlitePath);
        PgTarget? pg = null;
        if (!options.DryRun)
        {
            if (string.IsNullOrEmpty(options.ConnectionString)) throw new InvalidOperationException("A Postgres connection string is needed unless this is a dry run");
            await using (var migrations = new MigrationVrcxContext(options.ConnectionString, false, null))
            {
                var pending = (await migrations.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count > 0)
                    throw new InvalidOperationException($"The database is not migrated ({pending.Count} pending, first {pending[0]}); start the server once so it applies them, then import");
            }
            pg = await PgTarget.OpenAsync(options.ConnectionString, ct);
        }
        try
        {
            var importer = new LegacyImporter(options, src, pg, ct);
            await importer.ImportAsync();
            importer._report.Elapsed = watch.Elapsed;
            return importer._report;
        }
        finally
        {
            if (pg is not null) await pg.DisposeAsync();
        }
    }

    private async Task ImportAsync()
    {
        _accounts = _src.Accounts();
        CollectNames();
        _refs.LatestUserNames = _latestName.ToDictionary(kv => kv.Key, kv => kv.Value.Name, StringComparer.Ordinal);

        await AccountsAsync();
        await CachedEntitiesAsync();
        await GameLogAsync();
        foreach (var account in _accounts)
        {
            await FriendLogAsync(account);
            await FeedsAsync(account);
            await NotificationsAsync(account);
            await AccountTablesAsync(account);
        }
        await NameAndTrustChangesAsync();
        await LocalDataAsync();
        await SettingsAsync();
        SkippedTables();
    }

    // ---- names -----------------------------------------------------------------

    private void SeeName(string? userId, string? name, DateTimeOffset? at)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(name) || !userId.StartsWith("usr_", StringComparison.Ordinal)) return;
        if (!_idsByName.TryGetValue(name, out var ids)) _idsByName[name] = ids = new HashSet<string>(StringComparer.Ordinal);
        ids.Add(userId);
        var when = at ?? DateTimeOffset.MinValue;
        if (!_latestName.TryGetValue(userId, out var latest) || when >= latest.At) _latestName[userId] = (name, when);
    }

    /// <summary>Every (user id, display name) pair the file knows, to name stubs and to match the old log lines that carried only a name.</summary>
    private void CollectNames()
    {
        foreach (var r in _src.Rows("gamelog_join_leave", "user_id", "display_name", "created_at")) SeeName(r.String(0), r.String(1), r.Time(2));
        foreach (var r in _src.Rows("gamelog_video_play", "user_id", "display_name", "created_at")) SeeName(r.String(0), r.String(1), r.Time(2));
        foreach (var r in _src.Rows("gamelog_portal_spawn", "user_id", "display_name", "created_at")) SeeName(r.String(0), r.String(1), r.Time(2));
        foreach (var r in _src.Rows("gamelog_external", "user_id", "display_name", "created_at")) SeeName(r.String(0), r.String(1), r.Time(2));
        foreach (var r in _src.Rows("cache_avatar", "author_id", "author_name", "updated_at")) SeeName(r.String(0), r.String(1), r.Time(2));
        foreach (var r in _src.Rows("cache_world", "author_id", "author_name", "updated_at")) SeeName(r.String(0), r.String(1), r.Time(2));
        foreach (var account in _accounts)
        {
            foreach (var t in new[] { "feed_gps", "feed_online_offline", "feed_status", "feed_bio", "feed_avatar" })
                foreach (var r in _src.Rows(account.Table(t), "user_id", "display_name", "created_at")) SeeName(r.String(0), r.String(1), r.Time(2));
            foreach (var r in _src.Rows(account.Table("friend_log_history"), "user_id", "display_name", "created_at", "previous_display_name"))
            {
                // The previous name was theirs just before; the original logs the change when it notices it.
                SeeName(r.String(0), r.String(3), r.Time(2)?.AddTicks(-1));
                SeeName(r.String(0), r.String(1), r.Time(2));
            }
            foreach (var r in _src.Rows(account.Table("friend_log_current"), "user_id", "display_name")) SeeName(r.String(0), r.String(1), null);
            foreach (var r in _src.Rows(account.Table("notifications"), "sender_user_id", "sender_username", "created_at")) SeeName(r.String(0), r.String(1), r.Time(2));
            foreach (var r in _src.Rows(account.Table("notifications_v2"), "sender_user_id", "sender_username", "created_at")) SeeName(r.String(0), r.String(1), r.Time(2));
            foreach (var r in _src.Rows(account.Table("moderation"), "user_id", "display_name", "updated_at")) SeeName(r.String(0), r.String(1), r.Time(2));
            foreach (var r in _src.Rows(account.Table("notes"), "user_id", "display_name", "created_at")) SeeName(r.String(0), r.String(1), r.Time(2));
        }
    }

    /// <summary>The one user id this display name ever belonged to in the file, or null when none or several.</summary>
    private string? ResolveName(string? name) =>
        name is not null && _idsByName.TryGetValue(name, out var ids) && ids.Count == 1 ? ids.First() : null;

    // ---- the shared step -------------------------------------------------------

    /// <summary>
    /// Stages the rows, creates what they reference, then runs <paramref name="apply"/> (the
    /// INSERT ... SELECT and any UPDATE) in one transaction. In a dry run the rows are only counted.
    /// </summary>
    private async Task BulkAsync(TableReport report, string stage, StageColumn[] columns, IEnumerable<object?[]> rows, Func<PgTarget, Task> apply)
    {
        if (_pg is null)
        {
            foreach (var _ in rows) report.Candidates++;
            _refs.Clear();
            return;
        }
        await _pg.BeginAsync(_ct);
        try
        {
            report.Candidates += await _pg.StageAsync(stage, columns, rows, _ct);
            await _refs.FlushAsync(_pg, _ct);
            report.Inserted ??= 0;
            await apply(_pg);
            await _pg.CommitAsync(_ct);
        }
        catch
        {
            await _pg.RollbackAsync();
            throw;
        }
    }

    /// <summary>Like BulkAsync for steps that work through EF on rows already in memory.</summary>
    private async Task InTransactionAsync(TableReport report, Func<PgTarget, Task> apply)
    {
        if (_pg is null)
        {
            _refs.Clear();
            return;
        }
        await _pg.BeginAsync(_ct);
        try
        {
            await _refs.FlushAsync(_pg, _ct);
            report.Inserted ??= 0;
            await apply(_pg);
            await _pg.CommitAsync(_ct);
        }
        catch
        {
            await _pg.RollbackAsync();
            throw;
        }
    }

    private NpgsqlParameter Window(string name = "w") => new(name, NpgsqlDbType.Interval) { Value = _options.MatchWindow };

    private static StageColumn Text(string name) => new(name, NpgsqlDbType.Text);
    private static StageColumn Time(string name) => new(name, NpgsqlDbType.TimestampTz);
    private static StageColumn Interval(string name) => new(name, NpgsqlDbType.Interval);
    private static StageColumn Bool(string name) => new(name, NpgsqlDbType.Boolean);
    private static StageColumn Long(string name) => new(name, NpgsqlDbType.Bigint);
    private static StageColumn Json(string name) => new(name, NpgsqlDbType.Jsonb);
    private static StageColumn TextArray(string name) => new(name, NpgsqlDbType.Array | NpgsqlDbType.Text);

    /// <summary>A text column that should hold JSON, as jsonb text: null when empty, and a JSON string when it does not parse, so nothing is dropped.</summary>
    private static string? JsonText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var _ = JsonDocument.Parse(raw);
            return raw;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(raw);
        }
    }

    /// <summary>A JSON array of strings (bio links), or [] when absent or not one.</summary>
    private static string[] StringArray(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [raw];
            return doc.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText()).ToArray();
        }
        catch (JsonException)
        {
            return [raw];
        }
    }

    private static TimeSpan? Milliseconds(long? ms) => ms is { } v ? TimeSpan.FromMilliseconds(Math.Max(0, v)) : null;

    // ---- accounts --------------------------------------------------------------

    /// <summary>Every account the file has tables for gets an accounts row (and users row); one this server never logged in is marked as having no session.</summary>
    private async Task AccountsAsync()
    {
        var report = _report.Table("(per-account table prefixes)", "accounts");
        report.Read = _accounts.Count;
        report.Candidates = _accounts.Count;
        if (_pg is null) return;

        await InTransactionAsync(report, async pg =>
        {
            foreach (var account in _accounts) _refs.User(account.UserId, null, _now);
            await _refs.FlushAsync(pg, _ct);
            var existing = (await pg.Db.Accounts.AsNoTracking().Select(a => a.UserId).ToListAsync(_ct)).ToHashSet(StringComparer.Ordinal);
            foreach (var account in _accounts.Where(a => !existing.Contains(a.UserId)))
            {
                pg.Db.Accounts.Add(new Account { UserId = account.UserId, SessionValid = false, LoggedInAt = _now, Settings = AccountSettingsFromConfigs() });
                _createdAccounts.Add(account.UserId);
            }
            await pg.Db.SaveChangesAsync(_ct);
            report.Inserted = _createdAccounts.Count;
        });
    }

    /// <summary>Tables that are deliberately not imported, with the reason, so the report accounts for every table in the file.</summary>
    private void SkippedTables()
    {
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gamelog_location", "gamelog_join_leave", "gamelog_video_play", "gamelog_resource_load", "gamelog_event",
            "gamelog_external", "gamelog_portal_spawn", "cache_avatar", "cache_world", "favorite_world", "favorite_avatar",
            "favorite_friend", "favorite_print", "memos", "world_memos", "avatar_memos", "avatar_tags", "configs",
        };
        foreach (var account in _accounts)
        foreach (var suffix in new[] { "feed_gps", "feed_status", "feed_bio", "feed_avatar", "feed_online_offline", "friend_log_current", "friend_log_history",
                     "notifications", "notifications_v2", "moderation", "avatar_history", "notes", "mutual_graph_friends", "mutual_graph_links", "mutual_graph_meta", "user_profile" })
            handled.Add(account.Table(suffix));

        foreach (var table in _src.Tables.Order(StringComparer.Ordinal))
        {
            if (handled.Contains(table)) continue;
            var reason = table switch
            {
                "cookies" => "the original's VRChat session cookie jar: a login secret, and this server keeps its own session per account",
                "sqlite_sequence" or "sqlite_stat1" or "sqlite_stat4" => "SQLite internals",
                _ when table.EndsWith("_activity_sessions_v2", StringComparison.Ordinal) => "Activity tab cache, rebuilt by the original from feed_online_offline and gamelog_location (both imported); this server computes it from presence_events and instance_visits",
                _ when table.EndsWith("_activity_bucket_cache_v2", StringComparison.Ordinal) => "Activity tab cache of the sessions above (derived)",
                _ when table.EndsWith("_activity_sync_state_v2", StringComparison.Ordinal) => "bookkeeping for the Activity cache (derived)",
                _ => null,
            };
            var report = _report.Table(table, "(not imported)");
            report.Read = _src.Count(table);
            if (reason is null)
            {
                report.Skip("rows of a table this importer does not know");
                _report.Notes.Add($"{table} is not known to the importer and was left out");
            }
            else report.Skip(reason, report.Read);
        }
    }
}
