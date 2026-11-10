using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;

namespace VRCX.LegacyImport;

/// <summary>
/// The users, worlds, groups, avatars and instances the imported rows point at. Rows only name
/// them by id (and a name at the time); they are created as stubs, like VrchatStore.Ensure*Async
/// does, before the rows that reference them. Existing rows are left alone except that a stub
/// still named after its id gets the name the original knew, and first_seen_at moves back to the
/// earliest sighting.
/// </summary>
public sealed class References
{
    private sealed class Seen
    {
        public string? Name;
        public DateTimeOffset At;
    }

    private readonly Dictionary<string, Seen> _users = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Seen> _worlds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Seen> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Seen> _avatars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (VrchatLocation Location, DateTimeOffset At)> _instances = new(StringComparer.Ordinal);
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    /// <summary>The latest name each user id had anywhere in the source, so a stub gets the newest name.</summary>
    public IReadOnlyDictionary<string, string> LatestUserNames { get; set; } = new Dictionary<string, string>();

    private static void Note(Dictionary<string, Seen> map, HashSet<string> done, string kind, string id, string? name, DateTimeOffset at)
    {
        if (done.Contains(kind + id)) return;
        if (!map.TryGetValue(id, out var seen)) map[id] = seen = new Seen { At = at };
        if (at < seen.At) seen.At = at;
        if (!string.IsNullOrEmpty(name)) seen.Name ??= name;
    }

    public string User(string id, string? name, DateTimeOffset at)
    {
        Note(_users, _done, "u:", id, LatestUserNames.GetValueOrDefault(id) ?? name, at);
        return id;
    }

    public string World(string id, string? name, DateTimeOffset at)
    {
        Note(_worlds, _done, "w:", id, name, at);
        return id;
    }

    public string Avatar(string id, DateTimeOffset at)
    {
        Note(_avatars, _done, "a:", id, null, at);
        return id;
    }

    /// <summary>The instances row a location tag names (creating its world, owner and group stubs), or null when it is not an instance.</summary>
    public string? Instance(string? tag, DateTimeOffset at, string? worldName = null, string? groupName = null)
    {
        var location = VrchatLocation.Parse(tag);
        if (!location.IsRealInstance) return null;
        World(location.WorldId, worldName, at);
        if (location.OwnerUserId is { Length: > 0 } owner) User(owner, null, at);
        if (location.GroupId is { Length: > 0 } group) Note(_groups, _done, "g:", group, groupName, at);
        var key = location.InstanceLocation;
        if (!_done.Contains("i:" + key))
        {
            if (!_instances.TryGetValue(key, out var seen) || at < seen.At) _instances[key] = (location, at);
        }
        return key;
    }

    /// <summary>Creates what is missing and fills stub names, inside the caller's transaction.</summary>
    public async Task FlushAsync(PgTarget pg, CancellationToken ct)
    {
        var db = pg.Db;
        // A user referenced only as an instance owner or a world author still needs a row.
        foreach (var (location, at) in _instances.Values)
            if (location.OwnerUserId is { Length: > 0 } owner) User(owner, null, at);

        await FlushAsync(pg, "users", "display_name", _users, ids => db.Users.AddRange(ids.Select(x => new User
        {
            Id = x.Id, DisplayName = x.Name ?? x.Id, IsStub = true, FirstSeenAt = x.At, UpdatedAt = x.At,
        })), ct);
        await FlushAsync(pg, "worlds", "name", _worlds, ids => db.Worlds.AddRange(ids.Select(x => new World
        {
            Id = x.Id, Name = x.Name ?? x.Id, IsStub = true, FirstSeenAt = x.At,
        })), ct);
        await FlushAsync(pg, "groups", "name", _groups, ids => db.Groups.AddRange(ids.Select(x => new Group
        {
            Id = x.Id, Name = x.Name ?? x.Id, IsStub = true, FirstSeenAt = x.At,
        })), ct);
        await FlushAsync(pg, "avatars", "name", _avatars, ids => db.Avatars.AddRange(ids.Select(x => new Avatar
        {
            Id = x.Id, Name = x.Name ?? x.Id, IsStub = true, FirstSeenAt = x.At,
        })), ct);

        if (_instances.Count > 0)
        {
            var keys = _instances.Keys.ToArray();
            var existing = (await pg.QueryAsync("SELECT location FROM instances WHERE location = ANY(@ids)", r => r.GetString(0), ct,
                new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys })).ToHashSet(StringComparer.Ordinal);
            foreach (var (key, (location, at)) in _instances)
            {
                if (existing.Contains(key)) continue;
                db.Instances.Add(new Instance
                {
                    Location = key,
                    WorldId = location.WorldId,
                    InstanceId = location.InstanceId,
                    InstanceName = location.InstanceName,
                    Type = VrcxEnums.ParseInstanceAccessType(location.AccessType),
                    Region = location.Region,
                    OwnerUserId = location.OwnerUserId is { Length: > 0 } o ? o : null,
                    GroupId = location.GroupId is { Length: > 0 } g ? g : null,
                    GroupAccessType = location.GroupAccessType,
                    CanRequestInvite = location.CanRequestInvite,
                    AgeGate = location.AgeGate,
                    FirstSeenAt = at,
                });
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            await pg.ExecuteAsync("""
                UPDATE instances i SET first_seen_at = s.at
                FROM unnest(@ids, @ats) AS s(id, at)
                WHERE i.location = s.id AND i.first_seen_at > s.at
                """, ct,
                new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = keys },
                new NpgsqlParameter("ats", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = _instances.Values.Select(v => v.At).ToArray() });
            foreach (var key in keys) _done.Add("i:" + key);
            _instances.Clear();
        }
    }

    private async Task FlushAsync(PgTarget pg, string table, string nameColumn, Dictionary<string, Seen> map, Action<IEnumerable<(string Id, string? Name, DateTimeOffset At)>> add, CancellationToken ct)
    {
        if (map.Count == 0) return;
        var prefix = table switch { "users" => "u:", "worlds" => "w:", "groups" => "g:", _ => "a:" };
        var ids = map.Keys.ToArray();
        var existing = (await pg.QueryAsync($"SELECT id FROM {table} WHERE id = ANY(@ids)", r => r.GetString(0), ct,
            new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = ids })).ToHashSet(StringComparer.Ordinal);
        add(map.Where(kv => !existing.Contains(kv.Key)).Select(kv => (kv.Key, kv.Value.Name, kv.Value.At)));
        await pg.Db.SaveChangesAsync(ct);
        pg.Db.ChangeTracker.Clear();

        await pg.ExecuteAsync($"""
            UPDATE {table} t SET
                {nameColumn} = CASE WHEN t.is_stub AND t.{nameColumn} = t.id AND s.name IS NOT NULL THEN s.name ELSE t.{nameColumn} END,
                first_seen_at = LEAST(t.first_seen_at, s.at)
            FROM unnest(@ids, @names, @ats) AS s(id, name, at)
            WHERE t.id = s.id AND (t.first_seen_at > s.at OR (t.is_stub AND t.{nameColumn} = t.id AND s.name IS NOT NULL))
            """, ct,
            new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = ids },
            new NpgsqlParameter("names", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = map.Values.Select(v => v.Name).ToArray() },
            new NpgsqlParameter("ats", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = map.Values.Select(v => v.At).ToArray() });
        foreach (var id in ids) _done.Add(prefix + id);
        map.Clear();
    }

    /// <summary>For the dry run: forget what was noted.</summary>
    public void Clear()
    {
        _users.Clear();
        _worlds.Clear();
        _groups.Clear();
        _avatars.Clear();
        _instances.Clear();
    }
}
