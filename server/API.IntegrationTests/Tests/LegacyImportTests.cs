using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Npgsql;
using VRCX.LegacyImport;
using VRCX.Testing;
using VRCX.Testing.Desktop;

namespace VRCX.API.IntegrationTests.Tests;

/// <summary>
/// The legacy importer against a synthetic VRCX.sqlite3 built from the original's own schema
/// (src/services/database and Dotnet), with a row in every table it maps, imported into the
/// scenario's database next to rows this server recorded itself.
/// </summary>
public sealed class LegacyImportTests
{
    private const string Me = "usr_aaaaaaaa-0000-4000-8000-000000000001";
    private const string Other = "usr_bbbbbbbb-0000-4000-8000-000000000002";
    private const string Alice = "usr_alice";
    private const string Bob = "usr_bob";
    private const string Carol = "usr_carol";
    private const string Dave = "usr_dave";
    private const string Erin = "usr_erin";
    private const string Here = "wrld_club:777~friends(usr_host)~region(eu)";
    private const string Old = "wrld_old:1~region(us)";

    /// <summary>Every table the importer writes, counted.</summary>
    private static readonly string[] Targets =
    [
        "accounts", "users", "worlds", "avatars", "instances", "stored_instance_visits", "unidentified_visits", "video_plays", "portal_spawns",
        "resource_loads", "game_log_events", "presence_events", "user_status_changes", "user_bio_changes", "user_avatar_changes",
        "user_display_name_changes", "user_trust_changes", "friendships", "friend_request_events", "notifications", "moderations_against",
        "user_notes", "avatar_usages", "mutual_friend_links", "mutual_graph_meta", "user_memos", "world_memos", "avatar_memos", "avatar_tags",
        "local_favorites", "local_favorite_groups", "print_favorites", "legacy_settings",
    ];

    [Test]
    public async Task Imports_every_table_next_to_what_the_server_recorded_and_a_second_run_adds_nothing()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe(Me, "Me");
        app.Vrchat.Friend(Alice, "Alice");
        await app.LoginAsync();
        var t0 = Second(app.Time.GetUtcNow().AddDays(-3));

        // This server saw the start of the same stay from the PC, and Alice coming online.
        await using (var pc = await app.ConnectDesktopAsync("pc-legacy"))
        {
            await pc.SendLogAsync(
                GameLog.Location(Here, "Club").At(t0),
                GameLog.PlayerJoined("Bob", Bob).At(t0.AddSeconds(1)));
        }
        await ExecuteAsync(app, $"""
            INSERT INTO presence_events (user_id, at, kind, location, instance_location, previous_state)
            VALUES ('{Alice}', '{Iso(t0.AddHours(2).AddSeconds(30))}', 'online', '{Here}', '{Here}', 'offline')
            """);
        var before = await CountsAsync(app);
        var alicePresence = await ScalarAsync(app, $"SELECT count(*) FROM presence_events WHERE user_id = '{Alice}'");

        using var fixture = Fixture.Create(t0);
        var report = await ImportAsync(app, fixture.Path);

        var after = await CountsAsync(app);
        // The stay and Bob's visit this server recorded are the imported ones: one row each, with the original's leave time.
        var stays = await RowsAsync(app, $"SELECT joined_at, left_at FROM stored_instance_visits WHERE user_id = '{Me}' AND source = 'game_log' ORDER BY joined_at",
            r => (r.GetFieldValue<DateTimeOffset>(0), r.IsDBNull(1) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(1)));
        await Assert.That(stays.Count).IsEqualTo(2);
        await Assert.That(stays[0].Item2).IsEqualTo(t0.AddMinutes(10));
        // A stay whose time was never written (VRChat crashed) ends where it began.
        await Assert.That(stays[1].Item2).IsEqualTo(stays[1].Item1);
        var bob = await RowsAsync(app, $"SELECT left_at FROM stored_instance_visits WHERE user_id = '{Bob}'", r => r.GetFieldValue<DateTimeOffset>(0));
        await Assert.That(bob.Count).IsEqualTo(1);
        await Assert.That(bob[0]).IsEqualTo(t0.AddSeconds(300));
        // Carol's old line had only her name; another row names her id. "Ghost" never had one.
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM stored_instance_visits WHERE user_id = '{Carol}' AND left_at = '{Iso(t0.AddMinutes(10))}'")).IsEqualTo(1);
        await Assert.That(await ScalarAsync(app, "SELECT count(*) FROM unidentified_visits WHERE display_name = 'Ghost'")).IsEqualTo(1);
        // The PC's own join line is the stay, not a visit of its own.
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM stored_instance_visits WHERE user_id = '{Me}' AND instance_location = '{Here}'")).IsEqualTo(1);

        // Alice: GPS and Offline imported; the Online this server already had is the same event.
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM presence_events WHERE user_id = '{Alice}'")).IsEqualTo(alicePresence + 2);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM presence_events WHERE user_id = '{Alice}' AND kind = 'offline' AND duration_at_previous = interval '1 hour' AND world_name = 'Club'")).IsEqualTo(1);
        // Both accounts logged Alice's rename and status: stored once.
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM user_display_name_changes WHERE user_id = '{Alice}'")).IsEqualTo(1);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM user_status_changes WHERE user_id = '{Alice}'")).IsEqualTo(1);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM user_bio_changes WHERE user_id = '{Alice}' AND bio_links = ARRAY['https://example.com']")).IsEqualTo(1);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM user_avatar_changes WHERE user_id = '{Alice}' AND avatar_id = 'avtr_cached'")).IsEqualTo(1);

        // Friendships: the one this server found at login now starts when the original first knew it.
        var alice = await RowsAsync(app, $"SELECT started_at, friend_number FROM friendships WHERE owner_user_id = '{Me}' AND friend_user_id = '{Alice}'",
            r => (r.GetFieldValue<DateTimeOffset>(0), r.GetInt32(1)));
        await Assert.That(alice.Count).IsEqualTo(1);
        await Assert.That(alice[0].Item1 < t0).IsTrue();
        await Assert.That(alice[0].Item2).IsEqualTo(7);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM friendships WHERE owner_user_id = '{Me}' AND friend_user_id = '{Dave}' AND ended_at IS NULL AND started_at = '{Iso(t0.AddDays(-1))}'")).IsEqualTo(1);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM friendships WHERE owner_user_id = '{Me}' AND friend_user_id = '{Erin}' AND ended_at = '{Iso(t0.AddDays(-1))}'")).IsEqualTo(1);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM friend_request_events WHERE owner_user_id = '{Me}'")).IsEqualTo(1);

        // Notifications: v1's flat columns in details, v2's details kept apart.
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM notifications WHERE owner_user_id = '{Me}' AND id = 'not_v1' AND details->>'worldName' = 'Club' AND hidden")).IsEqualTo(1);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM notifications WHERE owner_user_id = '{Me}' AND id = 'not_v2' AND v2_details->>'k' = 'v' AND details->>'d' = '1'")).IsEqualTo(1);

        // The other account had no row here: created without a session, with the original's settings.
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM accounts WHERE user_id = '{Other}' AND NOT session_valid")).IsEqualTo(1);
        // Local favorites belonged to the PC: each account has them.
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM local_favorites WHERE target_id = '{Dave}'")).IsEqualTo(2);
        await Assert.That(await ScalarAsync(app, "SELECT count(*) FROM local_favorite_groups WHERE name = 'Besties'")).IsEqualTo(2);
        // Saved logins are not carried over.
        await Assert.That(await ScalarAsync(app, "SELECT count(*) FROM legacy_settings WHERE key = 'config:savedcredentials'")).IsEqualTo(0);
        await Assert.That(await ScalarAsync(app, "SELECT count(*) FROM legacy_settings WHERE key = 'config:vrcx_screenshothelper'")).IsEqualTo(1);
        await Assert.That(await ScalarAsync(app, "SELECT count(*) FROM worlds WHERE id = 'wrld_cached' AND NOT is_stub AND name = 'Cached World'")).IsEqualTo(1);

        // Every mapped table got its rows.
        foreach (var table in Targets.Except(["accounts", "instances"]))
            await Assert.That(after[table]).IsGreaterThan(before[table]).Because(table);
        await Assert.That(report.Tables.Where(t => t.Source.StartsWith("cookies", StringComparison.Ordinal)).Single().Inserted ?? 0).IsEqualTo(0);

        // The second run finds everything there.
        var again = await ImportAsync(app, fixture.Path);
        var final = await CountsAsync(app);
        foreach (var table in Targets) await Assert.That(final[table]).IsEqualTo(after[table]).Because(table);
        await Assert.That(again.Tables.Sum(t => t.Inserted ?? 0)).IsEqualTo(0);
    }

    [Test]
    public async Task A_dry_run_only_reads_and_works_while_VRCX_has_the_file_open()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe(Me, "Me");
        await app.LoginAsync();
        using var fixture = Fixture.Create(Second(DateTimeOffset.UtcNow.AddDays(-3)));
        var hash = fixture.Hash();
        var before = await CountsAsync(app);

        // VRCX keeps the file open, mid-write.
        await using var vrcx = new SqliteConnection($"Data Source={fixture.Path};Pooling=False");
        await vrcx.OpenAsync();
        await using (var begin = vrcx.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE; INSERT INTO memos (user_id, edited_at, memo) VALUES ('usr_uncommitted', '2024-01-01T00:00:00.000Z', 'x');";
            await begin.ExecuteNonQueryAsync();
        }

        var report = await LegacyImporter.RunAsync(new LegacyImportOptions { SqlitePath = fixture.Path, DryRun = true });

        await Assert.That(report.DryRun).IsTrue();
        await Assert.That(report.Tables.All(t => t.Inserted is null)).IsTrue();
        await Assert.That(report.Tables.Single(t => t.Source == "gamelog_location").Candidates).IsEqualTo(2);
        await Assert.That(report.Tables.Single(t => t.Source == "memos").Read).IsEqualTo(1);
        await Assert.That(report.ToText()).Contains("would import");
        var after = await CountsAsync(app);
        foreach (var table in Targets) await Assert.That(after[table]).IsEqualTo(before[table]).Because(table);

        await using (var rollback = vrcx.CreateCommand())
        {
            rollback.CommandText = "ROLLBACK";
            await rollback.ExecuteNonQueryAsync();
        }
        await vrcx.CloseAsync();
        SqliteConnection.ClearAllPools();
        await Assert.That(fixture.Hash()).IsEqualTo(hash);
    }

    private static Task<ImportReport> ImportAsync(VrcxScenario app, string path) =>
        LegacyImporter.RunAsync(new LegacyImportOptions { SqlitePath = path, ConnectionString = app.Database.ConnectionString });

    private static DateTimeOffset Second(DateTimeOffset at) => new(at.Ticks - at.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static async Task<Dictionary<string, long>> CountsAsync(VrcxScenario app)
    {
        var counts = new Dictionary<string, long>();
        foreach (var table in Targets) counts[table] = await ScalarAsync(app, $"SELECT count(*) FROM {table}");
        return counts;
    }

    private static async Task<long> ScalarAsync(VrcxScenario app, string sql)
    {
        await using var connection = new NpgsqlConnection(app.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(VrcxScenario app, string sql)
    {
        await using var connection = new NpgsqlConnection(app.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<T>> RowsAsync<T>(VrcxScenario app, string sql, Func<NpgsqlDataReader, T> read)
    {
        await using var connection = new NpgsqlConnection(app.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync()) rows.Add(read(reader));
        return rows;
    }

    /// <summary>A VRCX.sqlite3 in a temp directory, in WAL mode as the original keeps it, with the original's tables.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory;

        private Fixture(string directory)
        {
            _directory = directory;
            Path = System.IO.Path.Combine(directory, "VRCX.sqlite3");
        }

        public string Path { get; }

        public string Hash()
        {
            using var stream = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        public static Fixture Create(DateTimeOffset t0)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrcx-legacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var fixture = new Fixture(directory);
            using var db = new SqliteConnection($"Data Source={fixture.Path};Pooling=False");
            db.Open();
            void Run(string sql, params object?[] values)
            {
                using var command = db.CreateCommand();
                command.CommandText = sql;
                for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("@p" + i, values[i] ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
            string T(TimeSpan offset) => Iso(t0 + offset);
            static TimeSpan s(double seconds) => TimeSpan.FromSeconds(seconds);

            Run("PRAGMA journal_mode=WAL");
            // Dotnet: configs and cookies.
            Run("CREATE TABLE configs (`key` TEXT PRIMARY KEY, `value` TEXT)");
            Run("CREATE TABLE `cookies` (`key` TEXT PRIMARY KEY, `value` TEXT)");
            // database.js global tables.
            foreach (var ddl in new[]
                     {
                         "gamelog_location (id INTEGER PRIMARY KEY, created_at TEXT, location TEXT, world_id TEXT, world_name TEXT, time INTEGER, group_name TEXT, UNIQUE(created_at, location))",
                         "gamelog_join_leave (id INTEGER PRIMARY KEY, created_at TEXT, type TEXT, display_name TEXT, location TEXT, user_id TEXT, time INTEGER, UNIQUE(created_at, type, display_name))",
                         "gamelog_portal_spawn (id INTEGER PRIMARY KEY, created_at TEXT, display_name TEXT, location TEXT, user_id TEXT, instance_id TEXT, world_name TEXT, UNIQUE(created_at, display_name))",
                         "gamelog_video_play (id INTEGER PRIMARY KEY, created_at TEXT, video_url TEXT, video_name TEXT, video_id TEXT, location TEXT, display_name TEXT, user_id TEXT, UNIQUE(created_at, video_url))",
                         "gamelog_resource_load (id INTEGER PRIMARY KEY, created_at TEXT, resource_url TEXT, resource_type TEXT, location TEXT, UNIQUE(created_at, resource_url))",
                         "gamelog_event (id INTEGER PRIMARY KEY, created_at TEXT, data TEXT, UNIQUE(created_at, data))",
                         "gamelog_external (id INTEGER PRIMARY KEY, created_at TEXT, message TEXT, display_name TEXT, user_id TEXT, location TEXT, UNIQUE(created_at, message))",
                         "cache_avatar (id TEXT PRIMARY KEY, added_at TEXT, author_id TEXT, author_name TEXT, created_at TEXT, description TEXT, image_url TEXT, name TEXT, release_status TEXT, thumbnail_image_url TEXT, updated_at TEXT, version INTEGER)",
                         "cache_world (id TEXT PRIMARY KEY, added_at TEXT, author_id TEXT, author_name TEXT, created_at TEXT, description TEXT, image_url TEXT, name TEXT, release_status TEXT, thumbnail_image_url TEXT, updated_at TEXT, version INTEGER)",
                         "favorite_world (id INTEGER PRIMARY KEY, created_at TEXT, world_id TEXT, group_name TEXT)",
                         "favorite_avatar (id INTEGER PRIMARY KEY, created_at TEXT, avatar_id TEXT, group_name TEXT)",
                         "favorite_friend (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, group_name TEXT)",
                         "favorite_print (id INTEGER PRIMARY KEY, print_id TEXT UNIQUE, created_at TEXT)",
                         "memos (user_id TEXT PRIMARY KEY, edited_at TEXT, memo TEXT)",
                         "world_memos (world_id TEXT PRIMARY KEY, edited_at TEXT, memo TEXT)",
                         "avatar_memos (avatar_id TEXT PRIMARY KEY, edited_at TEXT, memo TEXT)",
                         "avatar_tags (avatar_id TEXT NOT NULL, tag TEXT NOT NULL, color TEXT, PRIMARY KEY (avatar_id, tag))",
                     })
                Run("CREATE TABLE " + ddl);
            // database.js initUserTables, for both accounts.
            foreach (var prefix in new[] { LegacyDatabase.TablePrefix(Me), LegacyDatabase.TablePrefix(Other) })
            foreach (var ddl in new[]
                     {
                         "_feed_gps (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, display_name TEXT, location TEXT, world_name TEXT, previous_location TEXT, time INTEGER, group_name TEXT)",
                         "_feed_status (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, display_name TEXT, status TEXT, status_description TEXT, previous_status TEXT, previous_status_description TEXT)",
                         "_feed_bio (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, display_name TEXT, bio TEXT, previous_bio TEXT, bio_links TEXT, previous_bio_links TEXT)",
                         "_feed_avatar (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, display_name TEXT, owner_id TEXT, avatar_name TEXT, current_avatar_image_url TEXT, current_avatar_thumbnail_image_url TEXT, previous_current_avatar_image_url TEXT, previous_current_avatar_thumbnail_image_url TEXT)",
                         "_feed_online_offline (id INTEGER PRIMARY KEY, created_at TEXT, user_id TEXT, display_name TEXT, type TEXT, location TEXT, world_name TEXT, time INTEGER, group_name TEXT)",
                         "_friend_log_current (user_id TEXT PRIMARY KEY, display_name TEXT, trust_level TEXT, friend_number INTEGER)",
                         "_friend_log_history (id INTEGER PRIMARY KEY, created_at TEXT, type TEXT, user_id TEXT, display_name TEXT, previous_display_name TEXT, trust_level TEXT, previous_trust_level TEXT, friend_number INTEGER)",
                         "_notifications (id TEXT PRIMARY KEY, created_at TEXT, type TEXT, sender_user_id TEXT, sender_username TEXT, receiver_user_id TEXT, message TEXT, world_id TEXT, world_name TEXT, image_url TEXT, invite_message TEXT, request_message TEXT, response_message TEXT, expired INTEGER)",
                         "_notifications_v2 (id TEXT PRIMARY KEY, created_at TEXT, updated_at TEXT, expires_at TEXT, type TEXT, link TEXT, link_text TEXT, message TEXT, title TEXT, image_url TEXT, seen INTEGER, sender_user_id TEXT, sender_username TEXT, data TEXT, responses TEXT, details TEXT)",
                         "_moderation (user_id TEXT PRIMARY KEY, updated_at TEXT, display_name TEXT, block INTEGER, mute INTEGER)",
                         "_avatar_history (avatar_id TEXT PRIMARY KEY, created_at TEXT, time INTEGER)",
                         "_notes (user_id TEXT PRIMARY KEY, display_name TEXT, note TEXT, created_at TEXT)",
                         "_user_profile (user_id TEXT PRIMARY KEY, bio TEXT, bio_links TEXT, updated_at TEXT)",
                         "_mutual_graph_friends (friend_id TEXT PRIMARY KEY)",
                         "_mutual_graph_links (friend_id TEXT NOT NULL, mutual_id TEXT NOT NULL, PRIMARY KEY(friend_id, mutual_id))",
                         "_mutual_graph_meta (friend_id TEXT PRIMARY KEY, last_fetched_at TEXT, opted_out INTEGER DEFAULT 0)",
                         "_activity_sessions_v2 (session_id INTEGER PRIMARY KEY AUTOINCREMENT, user_id TEXT NOT NULL, start_at INTEGER NOT NULL, end_at INTEGER NOT NULL)",
                     })
                Run("CREATE TABLE " + prefix + ddl);
            var me = LegacyDatabase.TablePrefix(Me);
            var other = LegacyDatabase.TablePrefix(Other);

            Run("INSERT INTO configs VALUES ('config:lastuserloggedin', @p0)", Me);
            Run("INSERT INTO configs VALUES ('config:vrcx_screenshothelper', 'true')");
            Run("INSERT INTO configs VALUES ('config:vrcx_id', 'legacy-vrcx-id')");
            Run("INSERT INTO configs VALUES ('config:vrcx_localfavoritefriendsgroups', '[\"Besties\"]')");
            Run("INSERT INTO configs VALUES ('config:savedcredentials', '{\"secret\":true}')");
            Run("INSERT INTO cookies VALUES ('default', 'secret')");

            Run("INSERT INTO cache_avatar VALUES ('avtr_cached', @p0, @p1, 'Alice', @p0, 'd', 'https://img/avtr_cached.png', 'Cached Avatar', 'public', 'https://img/t.png', @p0, 3)", T(s(-86400)), Alice);
            Run("INSERT INTO cache_world VALUES ('wrld_cached', @p0, @p1, 'Alice', @p0, 'd', 'https://img/w.png', 'Cached World', 'public', 'https://img/wt.png', @p0, 2)", T(s(-86400)), Alice);

            // Game log: a 10 minute stay at Here, one an hour later that never got its time, and one in a local test world.
            Run("INSERT INTO gamelog_location (created_at, location, world_id, world_name, time, group_name) VALUES (@p0, @p1, 'wrld_club', 'Club', 600000, '')", T(s(0)), Here);
            Run("INSERT INTO gamelog_location (created_at, location, world_id, world_name, time, group_name) VALUES (@p0, @p1, 'wrld_old', 'Old', 0, '')", T(s(3600)), Old);
            Run("INSERT INTO gamelog_location (created_at, location, world_id, world_name, time, group_name) VALUES (@p0, 'local:test', '', 'Test', 0, '')", T(s(7200)));
            void JoinLeave(int at, string type, string name, string location, string? id, long time) =>
                Run("INSERT INTO gamelog_join_leave (created_at, type, display_name, location, user_id, time) VALUES (@p0, @p1, @p2, @p3, @p4, @p5)", T(s(at)), type, name, location, id, time);
            JoinLeave(0, "OnPlayerJoined", "Me", Here, Me, 0);
            JoinLeave(1, "OnPlayerJoined", "Bob", Here, Bob, 0);
            JoinLeave(2, "OnPlayerJoined", "Ghost", Here, null, 0);
            JoinLeave(5, "OnPlayerJoined", "Carol", Here, null, 0);
            JoinLeave(100, "OnPlayerLeft", "Ghost", Here, null, 98000);
            JoinLeave(300, "OnPlayerLeft", "Bob", Here, Bob, 299000);
            JoinLeave(3600, "OnPlayerJoined", "Me", Old, Me, 0);
            Run("INSERT INTO gamelog_video_play (created_at, video_url, video_name, video_id, location, display_name, user_id) VALUES (@p0, 'https://youtu.be/x', 'Song', 'x', @p1, 'Carol', @p2)", T(s(10)), Here, Carol);
            Run("INSERT INTO gamelog_portal_spawn (created_at, display_name, location, user_id, instance_id, world_name) VALUES (@p0, 'Bob', @p1, @p2, @p3, 'Old')", T(s(20)), Here, Bob, Old);
            Run("INSERT INTO gamelog_resource_load (created_at, resource_url, resource_type, location) VALUES (@p0, 'https://img/r.png', 'ImageLoad', @p1)", T(s(30)), Here);
            Run("INSERT INTO gamelog_event (created_at, data) VALUES (@p0, 'Something happened')", T(s(40)));
            Run("INSERT INTO gamelog_external (created_at, message, display_name, user_id, location) VALUES (@p0, 'External message', 'Bob', @p1, @p2)", T(s(50)), Bob, Here);

            // Friend log: Alice befriended before logging began (now #7), Dave a day ago, Erin came and went.
            Run($"INSERT INTO {me}_friend_log_current VALUES (@p0, 'Alice', 'Known User', 7)", Alice);
            Run($"INSERT INTO {me}_friend_log_current VALUES (@p0, 'Dave', 'User', 8)", Dave);
            void History(string table, int at, string type, string id, string? name, string? previousName, string? trust, string? previousTrust, long? number) =>
                Run($"INSERT INTO {table}_friend_log_history (created_at, type, user_id, display_name, previous_display_name, trust_level, previous_trust_level, friend_number) VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)",
                    T(s(at)), type, id, name, previousName, trust, previousTrust, number);
            History(me, -2 * 86400, "Friend", Erin, "Erin", null, null, null, 6);
            History(me, -86400, "Unfriend", Erin, "Erin", null, null, null, null);
            History(me, -86400, "Friend", Dave, "Dave", null, null, null, 8);
            History(me, 1000, "DisplayName", Alice, "Alice", "Alice Old", null, null, null);
            History(me, 1100, "TrustLevel", Alice, "Alice", null, "Known User", "User", null);
            History(me, 1200, "FriendRequest", "usr_frank", "Frank", null, null, null, null);
            // The other account logged the same rename a little later.
            Run($"INSERT INTO {other}_friend_log_current VALUES (@p0, 'Alice', 'Known User', 1)", Alice);
            History(other, 1010, "DisplayName", Alice, "Alice", "Alice Old", null, null, null);

            // Feeds.
            Run($"INSERT INTO {me}_feed_gps (created_at, user_id, display_name, location, world_name, previous_location, time, group_name) VALUES (@p0, @p1, 'Alice', @p2, 'Club', @p3, 60000, '')", T(s(3 * 3600)), Alice, Here, Old);
            Run($"INSERT INTO {me}_feed_online_offline (created_at, user_id, display_name, type, location, world_name, time, group_name) VALUES (@p0, @p1, 'Alice', 'Online', @p2, 'Club', '', '')", T(s(2 * 3600)), Alice, Here);
            Run($"INSERT INTO {me}_feed_online_offline (created_at, user_id, display_name, type, location, world_name, time, group_name) VALUES (@p0, @p1, 'Alice', 'Offline', @p2, 'Club', 3600000, '')", T(s(4 * 3600)), Alice, Here);
            foreach (var table in new[] { me, other })
                Run($"INSERT INTO {table}_feed_status (created_at, user_id, display_name, status, status_description, previous_status, previous_status_description) VALUES (@p0, @p1, 'Alice', 'busy', 'working', 'active', '')", T(s(5000)), Alice);
            Run($"INSERT INTO {me}_feed_bio (created_at, user_id, display_name, bio, previous_bio, bio_links, previous_bio_links) VALUES (@p0, @p1, 'Alice', 'new bio', 'old bio', '[\"https://example.com\"]', '[]')", T(s(6000)), Alice);
            Run($"INSERT INTO {me}_feed_avatar (created_at, user_id, display_name, owner_id, avatar_name, current_avatar_image_url, current_avatar_thumbnail_image_url, previous_current_avatar_image_url, previous_current_avatar_thumbnail_image_url) VALUES (@p0, @p1, 'Alice', @p1, 'Cached Avatar', 'https://img/avtr_cached.png', 'https://img/t.png', 'https://img/prev.png', 'https://img/prevt.png')", T(s(7000)), Alice);

            // Notifications.
            Run($"INSERT INTO {me}_notifications VALUES ('not_v1', @p0, 'invite', @p1, 'Alice', @p2, 'come', 'wrld_club', 'Club', '', 'hi', '', '', 1)", T(s(8000)), Alice, Me);
            Run($"INSERT INTO {me}_notifications_v2 VALUES ('not_v2', @p0, @p0, @p0, 'group.announcement', 'group:grp_x', 'Open', 'msg', 'title', '', 1, @p1, 'Alice', '{{\"d\":\"1\"}}', '[]', '{{\"k\":\"v\"}}')", T(s(8100)), Alice);

            // The rest of the account's tables.
            Run($"INSERT INTO {me}_moderation VALUES (@p0, @p1, 'Bob', 1, 0)", Bob, T(s(9000)));
            Run($"INSERT INTO {me}_notes VALUES (@p0, 'Bob', 'met at the club', @p1)", Bob, T(s(9100)));
            Run($"INSERT INTO {me}_avatar_history VALUES ('avtr_cached', @p0, 120000)", T(s(9200)));
            Run($"INSERT INTO {other}_avatar_history VALUES ('avtr_cached', @p0, 60000)", T(s(9300)));
            Run($"INSERT INTO {me}_user_profile VALUES (@p0, 'cached bio', '[]', @p1)", Bob, T(s(9400)));
            Run($"INSERT INTO {me}_mutual_graph_friends VALUES (@p0)", Alice);
            Run($"INSERT INTO {me}_mutual_graph_friends VALUES (@p0)", Dave);
            Run($"INSERT INTO {me}_mutual_graph_links VALUES (@p0, @p1)", Alice, Dave);
            Run($"INSERT INTO {me}_mutual_graph_meta VALUES (@p0, @p1, 0)", Alice, T(s(9500)));
            Run($"INSERT INTO {me}_activity_sessions_v2 (user_id, start_at, end_at) VALUES (@p0, 1, 2)", Alice);

            // PC-wide memos and favorites.
            Run("INSERT INTO memos VALUES (@p0, @p1, 'a friend')", Alice, T(s(10000)));
            Run("INSERT INTO world_memos VALUES ('wrld_club', @p0, 'loud')", T(s(10000)));
            Run("INSERT INTO avatar_memos VALUES ('avtr_cached', @p0, 'cute')", T(s(10000)));
            Run("INSERT INTO avatar_tags VALUES ('avtr_cached', 'fluffy', '#fff')");
            Run("INSERT INTO favorite_friend (created_at, user_id, group_name) VALUES (@p0, @p1, 'Besties')", T(s(10000)), Dave);
            Run("INSERT INTO favorite_world (created_at, world_id, group_name) VALUES (@p0, 'wrld_club', 'Worlds')", T(s(10000)));
            Run("INSERT INTO favorite_avatar (created_at, avatar_id, group_name) VALUES (@p0, 'avtr_cached', 'Avatars')", T(s(10000)));
            Run("INSERT INTO favorite_print (print_id, created_at) VALUES ('prnt_1', @p0)", T(s(10000)));
            return fixture;
        }
    }
}
