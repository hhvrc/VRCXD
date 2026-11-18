using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VRCX.Common.Features.LegacyImport;
using VRCX.Common.Live;
using VRCX.Testing;
using VRCX.Testing.Live;
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

    // The Settings page's way in: the file goes up in parts (a proxy such as Cloudflare refuses a
    // body over 100 MB, and these files are often 700 MB), a dry run reports, the import brings it in.
    [Test]
    public async Task An_upload_in_parts_is_checked_then_imported_then_deleted_and_importing_again_adds_nothing()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe(Me, "Me");
        await app.LoginAsync();
        using var fixture = Fixture.Create(Second(app.Time.GetUtcNow().AddDays(-3)));
        var file = await File.ReadAllBytesAsync(fixture.Path);
        var before = await CountsAsync(app);

        var uploadId = await UploadAsync(app, file, parts: 3);
        var uploaded = await StatusAsync(app);
        await Assert.That(uploaded.GetProperty("state").GetString()).IsEqualTo("uploaded");
        await Assert.That(uploaded.GetProperty("received").GetInt64()).IsEqualTo(file.LongLength);

        var checkedStatus = await RunAsync(app, $"/1/legacy-import/{uploadId}/dry-run");
        await Assert.That(checkedStatus.GetProperty("state").GetString()).IsEqualTo("checked");
        await Assert.That(checkedStatus.GetProperty("report").GetProperty("dryRun").GetBoolean()).IsTrue();
        await Assert.That(Table(checkedStatus, "gamelog_location").GetProperty("candidates").GetInt64()).IsEqualTo(2);
        foreach (var (table, count) in await CountsAsync(app)) await Assert.That(count).IsEqualTo(before[table]).Because(table);

        var imported = await RunAsync(app, $"/1/legacy-import/{uploadId}/import");
        await Assert.That(imported.GetProperty("state").GetString()).IsEqualTo("imported");
        await Assert.That(imported.GetProperty("report").GetProperty("dryRun").GetBoolean()).IsFalse();
        var progress = imported.GetProperty("progress");
        await Assert.That(progress.GetProperty("step").GetInt32()).IsEqualTo(progress.GetProperty("steps").GetInt32());
        var after = await CountsAsync(app);
        foreach (var table in Targets.Except(["accounts", "instances"])) await Assert.That(after[table]).IsGreaterThan(before[table]).Because(table);
        await Assert.That(Directory.EnumerateFiles(UploadDirectory(app)).Any()).IsFalse();
        // The file is gone, so it cannot be imported a second time without a new upload.
        var again = await app.Api.PostAsync($"/1/legacy-import/{uploadId}/import", null);
        await Assert.That(again.StatusCode).IsEqualTo(HttpStatusCode.Conflict);

        var second = await UploadAsync(app, file, parts: 1);
        var reimported = await RunAsync(app, $"/1/legacy-import/{second}/import");
        await Assert.That(reimported.GetProperty("state").GetString()).IsEqualTo("imported");
        await Assert.That(reimported.GetProperty("report").GetProperty("tables").EnumerateArray().Sum(t => t.TryGetProperty("inserted", out var i) ? i.GetInt64() : 0)).IsEqualTo(0);
        foreach (var (table, count) in await CountsAsync(app)) await Assert.That(count).IsEqualTo(after[table]).Because(table);
    }

    [Test]
    public async Task A_part_that_does_not_follow_is_refused_with_where_the_upload_continues()
    {
        await using var app = await VrcxScenario.StartAsync();
        using var fixture = Fixture.Create(Second(app.Time.GetUtcNow().AddDays(-3)));
        var file = await File.ReadAllBytesAsync(fixture.Path);
        var uploadId = await StartAsync(app, file.LongLength);
        await Assert.That((await PutAsync(app, uploadId, 0, file.AsMemory(0, 100))).StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Sent again, as after a lost answer: the server already has it and says where to go on.
        var repeated = await PutAsync(app, uploadId, 0, file.AsMemory(0, 100));
        await Assert.That(repeated.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That((await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()).IsEqualTo("The upload continues at byte 100");

        var early = await app.Api.PostAsync($"/1/legacy-import/{uploadId}/dry-run", null);
        await Assert.That(early.StatusCode).IsEqualTo(HttpStatusCode.Conflict);

        await Assert.That((await PutAsync(app, uploadId, 100, file.AsMemory(100))).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await StatusAsync(app)).GetProperty("state").GetString()).IsEqualTo("uploaded");
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(UploadDirectory(app), "VRCX.sqlite3"))).IsEquivalentTo(file);

        var unknown = await PutAsync(app, "not-an-upload", 0, file.AsMemory(0, 10));
        await Assert.That(unknown.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_file_that_is_not_an_SQLite_database_or_is_longer_than_said_is_refused()
    {
        await using var app = await VrcxScenario.StartAsync();
        var text = "this is not a database, just a text file"u8.ToArray();
        var uploadId = await StartAsync(app, text.Length);
        await Assert.That((await PutAsync(app, uploadId, 0, text)).StatusCode).IsEqualTo(HttpStatusCode.UnsupportedMediaType);
        await Assert.That((await StatusAsync(app)).GetProperty("received").GetInt64()).IsEqualTo(0);

        var header = "SQLite format 3\0"u8.ToArray();
        uploadId = await StartAsync(app, header.Length - 1);
        await Assert.That((await PutAsync(app, uploadId, 0, header)).StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        var empty = await app.Api.PostAsJsonAsync("/1/legacy-import", new { size = 0 });
        await Assert.That(empty.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var huge = await app.Api.PostAsJsonAsync("/1/legacy-import", new { size = long.MaxValue });
        await Assert.That(huge.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
    }

    [Test]
    public async Task A_broken_file_fails_with_the_reason_and_is_kept_until_discarded()
    {
        await using var app = await VrcxScenario.StartAsync();
        // An SQLite header with nothing a database needs after it.
        var broken = "SQLite format 3\0"u8.ToArray().Concat(new byte[4096]).ToArray();
        var uploadId = await UploadAsync(app, broken, parts: 1);

        var failed = await RunAsync(app, $"/1/legacy-import/{uploadId}/dry-run");
        await Assert.That(failed.GetProperty("state").GetString()).IsEqualTo("failed");
        await Assert.That(failed.GetProperty("error").GetString()).IsNotNull().And.IsNotEmpty();
        await Assert.That(File.Exists(Path.Combine(UploadDirectory(app), "VRCX.sqlite3"))).IsTrue();

        var discarded = await app.Api.DeleteAsync("/1/legacy-import");
        await Assert.That(discarded.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(File.Exists(Path.Combine(UploadDirectory(app), "VRCX.sqlite3"))).IsFalse();
        await Assert.That((await StatusAsync(app)).GetProperty("state").GetString()).IsEqualTo("none");
    }

    // A 779 MB file's Check took the API past its 1.5 GB memory limit: the presence step collected
    // both feeds (1.5 million rows) in a list before counting them. The steps stream the rows now,
    // so what a run holds is the ids it has seen, not the rows. Alone, so the heap is this run's.
    [Test, NotInParallel]
    public async Task A_check_of_feeds_with_hundreds_of_thousands_of_rows_does_not_hold_them_in_memory()
    {
        const int rowsPerFeed = 250_000;
        using var fixture = Fixture.Create(Second(DateTimeOffset.UtcNow.AddDays(-30)));
        var prefix = LegacyDatabase.TablePrefix(Me);
        var t0 = DateTimeOffset.UtcNow.AddDays(-20);
        await using (var db = new SqliteConnection($"Data Source={fixture.Path};Pooling=False"))
        {
            await db.OpenAsync();
            await using var transaction = db.BeginTransaction();
            await using var gps = db.CreateCommand();
            gps.CommandText = $"INSERT INTO {prefix}_feed_gps (created_at, user_id, display_name, location, world_name, previous_location, time, group_name) VALUES (@at, @user, @name, @location, 'A World Name', @previous, 60000, '')";
            await using var online = db.CreateCommand();
            online.CommandText = $"INSERT INTO {prefix}_feed_online_offline (created_at, user_id, display_name, type, location, world_name, time, group_name) VALUES (@at, @user, @name, @type, @location, 'A World Name', 3600000, '')";
            foreach (var command in new[] { gps, online })
                foreach (var name in new[] { "@at", "@user", "@name", "@location", command == gps ? "@previous" : "@type" })
                    command.Parameters.Add(new SqliteParameter(name, ""));
            for (var i = 0; i < rowsPerFeed; i++)
            {
                // 500 friends in 2000 instances: few distinct ids, many rows, as a real feed.
                var user = $"usr_{i % 500:x8}-0000-4000-8000-000000000000";
                var location = $"wrld_{i % 2000:x8}-1111-4000-8000-000000000000:{i % 2000}~private({user})~region(eu)";
                foreach (var command in new[] { gps, online })
                {
                    command.Parameters["@at"].Value = Iso(t0.AddSeconds(i));
                    command.Parameters["@user"].Value = user;
                    command.Parameters["@name"].Value = $"Friend {i % 500}";
                    command.Parameters["@location"].Value = location;
                    command.Parameters[4].Value = command == gps ? location : (i % 2 == 0 ? "Online" : "Offline");
                    await command.ExecuteNonQueryAsync();
                }
            }
            await transaction.CommitAsync();
        }

        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        long peak = 0;
        using var stop = new CancellationTokenSource();
        // A full collection now and then: what is still referenced, not garbage waiting for one.
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: true) - baseline);
                try { await Task.Delay(25, stop.Token); } catch (OperationCanceledException) { }
            }
        });
        var report = await LegacyImporter.RunAsync(new LegacyImportOptions { SqlitePath = fixture.Path, DryRun = true });
        await stop.CancelAsync();
        await sampler;

        await Assert.That(report.Tables.Single(t => t.Source == $"{prefix}_feed_gps").Candidates).IsEqualTo(rowsPerFeed + 1);
        await Assert.That(report.Tables.Single(t => t.Source == $"{prefix}_feed_online_offline").Candidates).IsEqualTo(rowsPerFeed + 2);
        // Collected, these rows took about 300 MB.
        await Assert.That(peak).IsLessThan(64L * 1024 * 1024);
    }

    // After a restart GET said "none" while the 779 MB file was still on disk, and the page could
    // neither go on with it nor delete it. A new process now takes up the upload from the file.
    [Test]
    public async Task An_upload_half_sent_before_a_restart_goes_on_where_the_file_ends_and_a_file_without_its_upload_is_deleted()
    {
        await using var app = await VrcxScenario.StartAsync();
        using var fixture = Fixture.Create(Second(app.Time.GetUtcNow().AddDays(-3)));
        var file = await File.ReadAllBytesAsync(fixture.Path);
        var half = file.Length / 2;
        var uploadId = await StartAsync(app, file.Length);
        await VrcxScenario.EnsureSuccessAsync(await PutAsync(app, uploadId, 0, file[..half]), "first half");

        // The process that starts next: the same directory, nothing in memory.
        var restarted = ActivatorUtilities.CreateInstance<LegacyImportRuns>(app.Services);
        var status = restarted.Status();
        await Assert.That(status.UploadId).IsEqualTo(uploadId);
        await Assert.That(status.State).IsEqualTo(LegacyImportState.Uploading);
        await Assert.That(status.Size).IsEqualTo(file.LongLength);
        await Assert.That(status.Received).IsEqualTo(half);

        var appended = await restarted.AppendAsync(uploadId, half, new MemoryStream(file[half..]), CancellationToken.None);
        await Assert.That(appended is LegacyImportStatus { State: LegacyImportState.Uploaded }).IsTrue();
        await Assert.That(ActivatorUtilities.CreateInstance<LegacyImportRuns>(app.Services).Status().State).IsEqualTo(LegacyImportState.Uploaded);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(UploadDirectory(app), "VRCX.sqlite3"))).IsEquivalentTo(file, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        // A file whose record is gone (an older server wrote no record) is not an upload anyone can finish.
        File.Delete(Path.Combine(UploadDirectory(app), "VRCX.sqlite3.upload.json"));
        await Assert.That(ActivatorUtilities.CreateInstance<LegacyImportRuns>(app.Services).Status().State).IsEqualTo(LegacyImportState.None);
        await Assert.That(Directory.EnumerateFileSystemEntries(UploadDirectory(app))).IsEmpty();
    }

    // Two apps watching the same friends log the same moves seconds apart, or one misses them; the
    // import's window match then keeps both copies. Replacing history deletes this server's rows of
    // those kinds up to the file's newest, so the file's copy is the only one, and keeps the rest.
    [Test]
    public async Task Replacing_history_deletes_the_servers_feeds_and_game_log_up_to_the_files_newest_row_and_keeps_what_came_after()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe(Me, "Me");
        app.Vrchat.Friend(Alice, "Alice");
        await app.LoginAsync();
        var t0 = Second(app.Time.GetUtcNow().AddDays(-3));

        // The server's own copy of the same evening: Dave, whom the original never logged, and
        // Alice coming online at a moment the original did not see.
        await using (var pc = await app.ConnectDesktopAsync("pc-legacy"))
            await pc.SendLogAsync(GameLog.Location(Here, "Club").At(t0), GameLog.PlayerJoined("Dave", "usr_dave").At(t0.AddSeconds(30)));
        await ExecuteAsync(app, $"""
            INSERT INTO presence_events (user_id, at, kind, location, instance_location, previous_state) VALUES
              ('{Alice}', '{Iso(t0.AddHours(2).AddMinutes(45))}', 'online', '{Here}', '{Here}', 'offline'),
              ('{Alice}', '{Iso(t0.AddHours(6))}', 'online', '{Here}', '{Here}', 'offline')
            """);
        using var fixture = Fixture.Create(t0);
        var uploadId = await UploadAsync(app, await File.ReadAllBytesAsync(fixture.Path), parts: 1);

        // The check says what would go and deletes nothing.
        var check = await RunAsync(app, $"/1/legacy-import/{uploadId}/dry-run?replaceHistory=true");
        var notes = check.GetProperty("report").GetProperty("notes").EnumerateArray().Select(n => n.GetString()!).ToList();
        await Assert.That(notes).Contains(n => n.StartsWith("replace history: would delete this server's friends' locations", StringComparison.Ordinal));
        await Assert.That(await ScalarAsync(app, "SELECT count(*) FROM stored_instance_visits WHERE user_id = 'usr_dave'")).IsEqualTo(1);

        var imported = await RunAsync(app, $"/1/legacy-import/{uploadId}/import?replaceHistory=true");
        await Assert.That(imported.GetProperty("state").GetString()).IsEqualTo("imported");
        await Assert.That(await ScalarAsync(app, "SELECT count(*) FROM stored_instance_visits WHERE user_id = 'usr_dave'")).IsEqualTo(0);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM presence_events WHERE user_id = '{Alice}' AND at = '{Iso(t0.AddHours(2).AddMinutes(45))}'")).IsEqualTo(0);
        // After the file's newest presence row: the file says nothing about it, so it stays.
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM presence_events WHERE user_id = '{Alice}' AND at = '{Iso(t0.AddHours(6))}'")).IsEqualTo(1);
        // The file's own rows are all there: the feed's three and the stays it logged.
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM presence_events WHERE user_id = '{Alice}' AND at <= '{Iso(t0.AddHours(4))}'")).IsEqualTo(3);
        await Assert.That(await ScalarAsync(app, $"SELECT count(*) FROM stored_instance_visits WHERE user_id = '{Me}' AND source = 'game_log'")).IsEqualTo(2);
        // What has a real key is not history to replace.
        await Assert.That(await ScalarAsync(app, "SELECT count(*) FROM user_memos")).IsGreaterThan(0);
        var deleted = imported.GetProperty("report").GetProperty("notes").EnumerateArray().Select(n => n.GetString()!)
            .Single(n => n.StartsWith("replace history: deleted", StringComparison.Ordinal) && n.Contains("friends' locations"));
        await Assert.That(deleted).StartsWith("replace history: deleted 1 ");
    }

    // A real file's import failed in the presence step: a friend's GPS row named a world after the
    // Bee Movie script (4,999 characters), and the world stub's name did not fit the btree index on
    // worlds.name. The index is gone; the stub keeps the whole name.
    [Test]
    public async Task A_world_named_in_5000_characters_of_prose_is_imported_with_its_whole_name()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe(Me, "Me");
        await app.LoginAsync();
        var t0 = Second(app.Time.GetUtcNow().AddDays(-3));
        using var fixture = Fixture.Create(t0);
        var name = LongNameTests.Prose(5_000);
        const string bee = "wrld_1d55d0b7-0000-4000-8000-00000000b33e:1~region(us)";
        await using (var db = new SqliteConnection($"Data Source={fixture.Path};Pooling=False"))
        {
            await db.OpenAsync();
            await using var insert = db.CreateCommand();
            insert.CommandText = $"INSERT INTO {LegacyDatabase.TablePrefix(Me)}_feed_gps (created_at, user_id, display_name, location, world_name, previous_location, time, group_name) VALUES (@at, @user, 'Alice', @location, @name, '', 0, '')";
            insert.Parameters.AddWithValue("@at", Iso(t0.AddHours(5)));
            insert.Parameters.AddWithValue("@user", Alice);
            insert.Parameters.AddWithValue("@location", bee);
            insert.Parameters.AddWithValue("@name", name);
            await insert.ExecuteNonQueryAsync();
        }

        await ImportAsync(app, fixture.Path);

        var stored = await RowsAsync(app, "SELECT name FROM worlds WHERE id = 'wrld_1d55d0b7-0000-4000-8000-00000000b33e'", r => r.GetString(0));
        await Assert.That(stored.Single()).IsEqualTo(name);
    }

    // The Settings page follows an upload, a check and an import on the "legacy_import" topic: the
    // snapshot is the status GET /1/legacy-import returns, and every change after it is a delta
    // carrying the whole status again, numbered without gaps.
    [Test]
    public async Task The_topic_streams_the_upload_and_the_check_step_by_step_as_rest_reports_them()
    {
        await using var app = await VrcxScenario.StartAsync();
        using var fixture = Fixture.Create(Second(app.Time.GetUtcNow().AddDays(-3)));
        var file = await File.ReadAllBytesAsync(fixture.Path);
        await using var live = await app.ConnectLiveAsync();
        var hello = live.Received.First(m => m.MessageCase == ServerMessage.MessageOneofCase.Hello).Hello;
        await Assert.That(hello.Topics).Contains(LegacyImportTopic.TopicName);

        // Not an account's: subscribed without one.
        var snapshot = (await live.SubscribeSnapshotAsync(1, LegacyImportTopic.TopicName, null)).LegacyImport;
        await Assert.That(snapshot.State).IsEqualTo("none");
        await Assert.That(snapshot.UploadId).IsEqualTo("");

        var uploadId = await UploadAsync(app, file, parts: 2);
        var uploaded = await live.DeltaAsync(1, d => d.LegacyImport.State == "uploaded");
        var received = live.Deltas(1).Where(d => d.LegacyImport.State == "uploading").Select(d => d.LegacyImport.Received).ToList();
        await Assert.That(received.First()).IsEqualTo(0);
        await Assert.That(received.Last()).IsEqualTo((file.Length + 1) / 2);
        await Assert.That(uploaded.LegacyImport.UploadId).IsEqualTo(uploadId);
        await Assert.That(uploaded.LegacyImport.Received).IsEqualTo(file.LongLength);

        await RunAsync(app, $"/1/legacy-import/{uploadId}/dry-run");
        var done = await live.DeltaAsync(1, d => d.LegacyImport.State == "checked");
        var steps = live.Deltas(1).Where(d => d.LegacyImport.State == "checking" && d.LegacyImport.Step > 0).Select(d => d.LegacyImport.Step).ToList();
        await Assert.That(steps).IsEquivalentTo(Enumerable.Range(1, (int)done.LegacyImport.Steps).Select(s => (uint)s), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(done.LegacyImport.StepName).IsEqualTo("settings");

        // The delta's report is REST's.
        var rest = await StatusAsync(app);
        var report = done.LegacyImport.Report;
        await Assert.That(report.DryRun).IsTrue();
        await Assert.That(report.Tables.Select(t => (t.Source, t.Target, t.Candidates, t.HasInserted)))
            .IsEquivalentTo(rest.GetProperty("report").GetProperty("tables").EnumerateArray()
                .Select(t => (t.GetProperty("source").GetString()!, t.GetProperty("target").GetString()!, t.GetProperty("candidates").GetUInt64(), t.TryGetProperty("inserted", out _))),
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(done.LegacyImport.FinishedAt.ToDateTimeOffset()).IsEqualTo(rest.GetProperty("finishedAt").GetDateTimeOffset());

        var revs = live.Deltas(1).Select(d => d.Rev).ToList();
        await Assert.That(revs).IsEquivalentTo(Enumerable.Range(2, revs.Count).Select(r => (ulong)r), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_page_opened_during_an_import_starts_from_where_it_is_and_one_that_left_hears_nothing()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe(Me, "Me");
        await app.LoginAsync();
        using var fixture = Fixture.Create(Second(app.Time.GetUtcNow().AddDays(-3)));
        var uploadId = await UploadAsync(app, await File.ReadAllBytesAsync(fixture.Path), parts: 1);

        await using var leaving = await app.ConnectLiveAsync();
        await leaving.SubscribeSnapshotAsync(1, LegacyImportTopic.TopicName, null);
        await leaving.UnsubscribeAsync(1);
        await using var watching = await app.ConnectLiveAsync();
        var before = (await watching.SubscribeSnapshotAsync(7, LegacyImportTopic.TopicName, null)).LegacyImport;
        await Assert.That(before.State).IsEqualTo("uploaded");

        await RunAsync(app, $"/1/legacy-import/{uploadId}/import");
        var imported = (await watching.DeltaAsync(7, d => d.LegacyImport.State == "imported")).LegacyImport;
        await Assert.That(imported.Report.DryRun).IsFalse();
        await Assert.That(imported.Report.Tables.Sum(t => (long)t.Inserted)).IsGreaterThan(0);
        await leaving.AssertNoDeltaAsync(1, TimeSpan.FromMilliseconds(200));

        // A page that comes back (a reconnect is a new connection) is sent the status as it is now.
        await using var back = await app.ConnectLiveAsync();
        var now = (await back.SubscribeSnapshotAsync(1, LegacyImportTopic.TopicName, null)).LegacyImport;
        await Assert.That(now.State).IsEqualTo("imported");
        await Assert.That(now.Step).IsEqualTo(now.Steps);
        await Assert.That(now.Report.Tables.Count).IsEqualTo(imported.Report.Tables.Count);

        // Discarding is a change too.
        await VrcxScenario.EnsureSuccessAsync(await app.Api.DeleteAsync("/1/legacy-import"), "discard");
        await Assert.That((await back.DeltaAsync(1, d => d.LegacyImport.State == "none")).LegacyImport.UploadId).IsEqualTo("");
    }

    private static string UploadDirectory(VrcxScenario app) => Path.GetFullPath(app.Services.GetRequiredService<VRCX.Common.Options.LegacyImportUploadOptions>().Dir);

    private static async Task<string> StartAsync(VrcxScenario app, long size)
    {
        var response = await app.Api.PostAsJsonAsync("/1/legacy-import", new { size });
        await VrcxScenario.EnsureSuccessAsync(response, "start upload");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("uploadId").GetString()!;
    }

    private static Task<HttpResponseMessage> PutAsync(VrcxScenario app, string uploadId, long offset, ReadOnlyMemory<byte> part)
    {
        var content = new ReadOnlyMemoryContent(part);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return app.Api.PutAsync($"/1/legacy-import/{uploadId}?offset={offset}", content);
    }

    private static async Task<string> UploadAsync(VrcxScenario app, byte[] file, int parts)
    {
        var uploadId = await StartAsync(app, file.LongLength);
        var size = (file.Length + parts - 1) / parts;
        for (var offset = 0; offset < file.Length; offset += size)
            await VrcxScenario.EnsureSuccessAsync(await PutAsync(app, uploadId, offset, file.AsMemory(offset, Math.Min(size, file.Length - offset))), "upload part");
        return uploadId;
    }

    private static async Task<JsonElement> RunAsync(VrcxScenario app, string path)
    {
        await VrcxScenario.EnsureSuccessAsync(await app.Api.PostAsync(path, null), path);
        await app.Services.GetRequiredService<LegacyImportRuns>().Running;
        return await StatusAsync(app);
    }

    private static Task<JsonElement> StatusAsync(VrcxScenario app) => app.GetJsonAsync("/1/legacy-import");

    private static JsonElement Table(JsonElement status, string source) =>
        status.GetProperty("report").GetProperty("tables").EnumerateArray().Single(t => t.GetProperty("source").GetString() == source);

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
