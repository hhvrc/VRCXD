using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace VRCX.LegacyImport;

/// <summary>
/// The original VRCX database, opened read-only. The whole import reads from one snapshot (a read
/// transaction), so a VRCX that is running and writing meanwhile neither disturbs nor is disturbed:
/// in WAL mode a reader never blocks the writer, and Mode=ReadOnly plus query_only mean nothing
/// here can change the file.
/// </summary>
public sealed partial class LegacyDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _snapshot;
    private readonly Dictionary<string, HashSet<string>> _columns = new(StringComparer.OrdinalIgnoreCase);

    private LegacyDatabase(SqliteConnection connection)
    {
        _connection = connection;
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA query_only = 1";
            pragma.ExecuteNonQuery();
        }
        _snapshot = connection.BeginTransaction(deferred: true);

        var tables = new List<string>();
        using (var command = Command("SELECT name FROM sqlite_master WHERE type = 'table'"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) tables.Add(reader.GetString(0));
        foreach (var table in tables)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var command = Command($"SELECT name FROM pragma_table_info('{table.Replace("'", "''")}')");
            using var reader = command.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(0));
            _columns[table] = columns;
        }
    }

    public static LegacyDatabase Open(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No VRCX database at that path", path);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 30,
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return new LegacyDatabase(connection);
    }

    public IReadOnlyCollection<string> Tables => _columns.Keys;

    public bool HasTable(string table) => _columns.ContainsKey(table);

    public bool HasColumn(string table, string column) => _columns.TryGetValue(table, out var columns) && columns.Contains(column);

    public long Count(string table)
    {
        if (!HasTable(table)) return 0;
        using var command = Command($"SELECT count(*) FROM {Quote(table)}");
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>The earliest and latest parseable timestamp in a text column, or nulls.</summary>
    public (DateTimeOffset? Min, DateTimeOffset? Max) TimeRange(string table, string column)
    {
        if (!HasColumn(table, column)) return (null, null);
        using var command = Command($"SELECT min({Quote(column)}), max({Quote(column)}) FROM {Quote(table)} WHERE {Quote(column)} IS NOT NULL AND {Quote(column)} <> ''");
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return (null, null);
        return (LegacyTime.Parse(reader.IsDBNull(0) ? null : reader.GetValue(0).ToString()), LegacyTime.Parse(reader.IsDBNull(1) ? null : reader.GetValue(1).ToString()));
    }

    /// <summary>
    /// Every row of a table with the named columns. A column an older VRCX never
    /// added reads as null, and a missing table reads as no rows.
    /// </summary>
    public IEnumerable<LegacyRow> Rows(string table, params string[] columns)
    {
        if (!HasTable(table)) yield break;
        var select = string.Join(", ", columns.Select(c => HasColumn(table, c) ? Quote(c) : "NULL"));
        using var command = Command($"SELECT {select} FROM {Quote(table)}");
        using var reader = command.ExecuteReader();
        var row = new LegacyRow(reader);
        while (reader.Read()) yield return row;
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _snapshot;
        return command;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public void Dispose()
    {
        _snapshot.Dispose();
        _connection.Dispose();
    }

    // ---- accounts ----------------------------------------------------------

    /// <summary>The per-account tables' suffixes (index.js initUserTables), longest first so the prefix split is unambiguous.</summary>
    public static readonly string[] AccountTableSuffixes =
    [
        "_activity_bucket_cache_v2", "_activity_sessions_v2", "_activity_sync_state_v2",
        "_feed_online_offline", "_friend_log_current", "_friend_log_history", "_mutual_graph_friends",
        "_mutual_graph_links", "_mutual_graph_meta", "_notifications_v2", "_notifications", "_avatar_history",
        "_user_profile", "_feed_status", "_feed_avatar", "_moderation", "_feed_bio", "_feed_gps", "_notes",
    ];

    /// <summary>
    /// The accounts the file has tables for, as (table prefix, user id). VRCX names the tables after
    /// the id with "-" and "_" removed (and "_" in front when it starts with a digit), so the id is
    /// recovered from the config keys that carry it, or from the usr_ uuid shape.
    /// </summary>
    public IReadOnlyList<LegacyAccount> Accounts()
    {
        var prefixes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var table in Tables)
        foreach (var suffix in AccountTableSuffixes)
        {
            if (table.Length > suffix.Length && table.EndsWith(suffix, StringComparison.Ordinal))
            {
                prefixes.Add(table[..^suffix.Length]);
                break;
            }
        }

        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in Rows("configs", "key", "value"))
        {
            var key = row.String(0) ?? "";
            foreach (Match m in UserIdPattern().Matches(key)) known.TryAdd(TablePrefix(m.Value), m.Value);
            if (key == "config:lastuserloggedin" && row.String(1) is { } last) known.TryAdd(TablePrefix(last), last);
        }

        var accounts = new List<LegacyAccount>();
        foreach (var prefix in prefixes)
        {
            if (known.TryGetValue(prefix, out var id)) accounts.Add(new LegacyAccount(prefix, id));
            else if (UuidPrefixPattern().Match(prefix) is { Success: true } m)
            {
                var hex = m.Groups[1].Value;
                accounts.Add(new LegacyAccount(prefix, $"usr_{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}"));
            }
            else accounts.Add(new LegacyAccount(prefix, prefix.TrimStart('_')));
        }
        return accounts;
    }

    /// <summary>database.js initUserTables: the table prefix for a user id.</summary>
    public static string TablePrefix(string userId)
    {
        var prefix = userId.Replace("-", "").Replace("_", "");
        return prefix.Length > 0 && char.IsAsciiDigit(prefix[0]) ? "_" + prefix : prefix;
    }

    [GeneratedRegex(@"usr_[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex UserIdPattern();

    [GeneratedRegex("^usr([0-9a-f]{32})$")]
    private static partial Regex UuidPrefixPattern();
}

public sealed record LegacyAccount(string Prefix, string UserId)
{
    public string Table(string suffix) => Prefix + "_" + suffix;
}

/// <summary>One row of a reader, read leniently: SQLite columns hold whatever was bound, so "" and NULL mean no value and numbers may arrive as text.</summary>
public sealed class LegacyRow(SqliteDataReader reader)
{
    public bool IsNull(int i) => reader.IsDBNull(i);

    /// <summary>The value as text, or null when NULL.</summary>
    public string? Raw(int i) => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);

    /// <summary>The value as text; null when NULL or empty.</summary>
    public string? String(int i) => Raw(i) is { Length: > 0 } s ? s : null;

    public long? Long(int i)
    {
        if (reader.IsDBNull(i)) return null;
        var value = reader.GetValue(i);
        return value switch
        {
            long l => l,
            double d => (long)d,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => (long)d,
            _ => null,
        };
    }

    public bool Bool(int i) => Long(i) is { } l ? l != 0 : Raw(i) is "true" or "True";

    public DateTimeOffset? Time(int i) => LegacyTime.Parse(Raw(i));
}

public static class LegacyTime
{
    /// <summary>VRCX stores Date.toJSON() strings ("2024-05-01T12:00:00.000Z"); anything else that parses is taken as UTC.</summary>
    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed.ToUniversalTime();
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms > 0)
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return null;
    }
}
