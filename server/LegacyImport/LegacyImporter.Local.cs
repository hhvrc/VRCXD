using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VRCX.Common.VrcxDb;

namespace VRCX.LegacyImport;

// What the original kept for the PC rather than per account: memos, local favorites, the avatar
// and world caches, and the configs table.
public sealed partial class LegacyImporter
{
    private Dictionary<string, string>? _configs;

    private Dictionary<string, string> Configs()
    {
        if (_configs is not null) return _configs;
        _configs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _src.Rows("configs", "key", "value"))
            if (r.String(0) is { } key) _configs[key] = r.Raw(1) ?? "";
        return _configs;
    }

    private string? Config(string name) => Configs().GetValueOrDefault("config:" + name.ToLowerInvariant());

    private bool ConfigBool(string name, bool fallback) => Config(name) switch
    {
        "true" or "True" => true,
        "false" or "False" => false,
        _ => fallback,
    };

    /// <summary>A JSON object of strings, as the original saved its maps.</summary>
    private Dictionary<string, string>? ConfigMap(string name)
    {
        var raw = Config(name);
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText())
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string[] ConfigList(string name, string[] fallback)
    {
        var raw = Config(name);
        if (string.IsNullOrEmpty(raw)) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText()).ToArray()
                : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    /// <summary>The original's settings that this server acts on (AccountSettings), for an account the import creates.</summary>
    private AccountSettings AccountSettingsFromConfigs()
    {
        var d = new AccountSettings();
        return d with
        {
            ScreenshotHelper = ConfigBool("VRCX_screenshotHelper", d.ScreenshotHelper),
            ScreenshotHelperModifyFilename = ConfigBool("VRCX_screenshotHelperModifyFilename", d.ScreenshotHelperModifyFilename),
            ScreenshotHelperCopyToClipboard = ConfigBool("VRCX_screenshotHelperCopyToClipboard", d.ScreenshotHelperCopyToClipboard),
            YoutubeApi = ConfigBool("VRCX_youtubeAPI", d.YoutubeApi),
            YoutubeApiKey = Config("VRCX_youtubeAPIKey") ?? d.YoutubeApiKey,
            VrcQuitFix = ConfigBool("VRCX_vrcQuitFix", d.VrcQuitFix),
            RelaunchVrchatAfterCrash = ConfigBool("VRCX_relaunchVRChatAfterCrash", d.RelaunchVrchatAfterCrash),
            VrcRegistryAutoBackup = ConfigBool("VRCX_vrcRegistryAutoBackup", d.VrcRegistryAutoBackup),
            VrcRegistryAskRestore = ConfigBool("VRCX_vrcRegistryAskRestore", d.VrcRegistryAskRestore),
            AutoSweepVrchatCache = ConfigBool("VRCX_autoSweepVRChatCache", d.AutoSweepVrchatCache),
            OpenVr = ConfigBool("openVR", d.OpenVr),
            OpenVrAlways = ConfigBool("openVRAlways", d.OpenVrAlways),
            OverlayWrist = ConfigBool("VRCX_overlayWrist", d.OverlayWrist),
            OverlayButton = ConfigBool("VRCX_overlaybutton", d.OverlayButton),
            OverlayHand = int.TryParse(Config("VRCX_overlayHand"), out var hand) && hand is >= 0 and <= 2 ? hand : d.OverlayHand,
            SaveInstancePrints = ConfigBool("VRCX_saveInstancePrints", d.SaveInstancePrints),
            CropInstancePrints = ConfigBool("VRCX_cropInstancePrints", d.CropInstancePrints),
            SaveInstanceStickers = ConfigBool("VRCX_saveInstanceStickers", d.SaveInstanceStickers),
            SaveInstanceEmoji = ConfigBool("VRCX_saveInstanceEmoji", d.SaveInstanceEmoji),
            DiscordActive = ConfigBool("discordActive", d.DiscordActive),
            DiscordInstance = ConfigBool("discordInstance", d.DiscordInstance),
            DiscordHideInvite = ConfigBool("discordHideInvite", d.DiscordHideInvite),
            DiscordJoinButton = ConfigBool("discordJoinButton", d.DiscordJoinButton),
            DiscordHideImage = ConfigBool("discordHideImage", d.DiscordHideImage),
            DiscordShowPlatform = ConfigBool("discordShowPlatform", d.DiscordShowPlatform),
            DiscordWorldIntegration = ConfigBool("discordWorldIntegration", d.DiscordWorldIntegration),
            DiscordWorldNameAsDiscordStatus = ConfigBool("discordWorldNameAsDiscordStatus", d.DiscordWorldNameAsDiscordStatus),
            DiscordMediaImage = ConfigBool("discordMediaImage", d.DiscordMediaImage),
            PhotonLobbyTimeoutThreshold = int.TryParse(Config("VRCX_photonLobbyTimeoutThreshold"), out var photonTimeout) ? photonTimeout : d.PhotonLobbyTimeoutThreshold,
            ChatboxUserBlacklist = ConfigMap("VRCX_chatboxUserBlacklist") ?? d.ChatboxUserBlacklist,
            ChatboxBlacklist = ConfigList("VRCX_chatboxBlacklist", d.ChatboxBlacklist),
            PhotonEventTypeFilter = ConfigList("VRCX_photonEventTypeFilter", d.PhotonEventTypeFilter),
            PhotonEventOverlay = ConfigBool("VRCX_PhotonEventOverlay", d.PhotonEventOverlay),
            PhotonEventOverlayFilter = Config("VRCX_PhotonEventOverlayFilter") ?? d.PhotonEventOverlayFilter,
            PhotonEventTypeOverlayFilter = ConfigList("VRCX_photonEventTypeOverlayFilter", d.PhotonEventTypeOverlayFilter),
            PhotonOverlayMessageTimeout = int.TryParse(Config("VRCX_photonOverlayMessageTimeout"), out var hudTimeout) ? hudTimeout : d.PhotonOverlayMessageTimeout,
            TimeoutHudOverlay = ConfigBool("VRCX_TimeoutHudOverlay", d.TimeoutHudOverlay),
            TimeoutHudOverlayFilter = Config("VRCX_TimeoutHudOverlayFilter") ?? d.TimeoutHudOverlayFilter,
            ProgressPie = ConfigBool("VRCX_progressPie", d.ProgressPie),
            ProgressPieFilter = ConfigBool("VRCX_progressPieFilter", d.ProgressPieFilter),
            SelfInviteOverride = ConfigBool("VRCX_selfInviteOverride", d.SelfInviteOverride),
            UgcFolderPath = Config("VRCX_userGeneratedContentPath") ?? d.UgcFolderPath,
            EnableAppLauncher = ConfigBool("VRCX_enableAppLauncher", d.EnableAppLauncher),
            EnableAppLauncherAutoClose = ConfigBool("VRCX_enableAppLauncherAutoClose", d.EnableAppLauncherAutoClose),
            EnableAppLauncherRunProcessOnce = ConfigBool("VRCX_enableAppLauncherRunProcessOnce", d.EnableAppLauncherRunProcessOnce),
            LogResourceLoad = ConfigBool("VRCX_logResourceLoad", d.LogResourceLoad),
            UdonExceptionLogging = ConfigBool("VRCX_udonExceptionLogging", d.UdonExceptionLogging),
            AutoStateChangeEnabled = ConfigBool("VRCX_autoStateChangeEnabled", d.AutoStateChangeEnabled),
            AutoStateChangeAloneStatus = Config("VRCX_autoStateChangeAloneStatus") ?? d.AutoStateChangeAloneStatus,
            AutoStateChangeCompanyStatus = Config("VRCX_autoStateChangeCompanyStatus") ?? d.AutoStateChangeCompanyStatus,
            AutoStateChangeInstanceTypes = ConfigList("VRCX_autoStateChangeInstanceTypes", d.AutoStateChangeInstanceTypes),
            AutoStateChangeNoFriends = ConfigBool("VRCX_autoStateChangeNoFriends", d.AutoStateChangeNoFriends),
            AutoStateChangeAloneDescEnabled = ConfigBool("VRCX_autoStateChangeAloneDescEnabled", d.AutoStateChangeAloneDescEnabled),
            AutoStateChangeAloneDesc = Config("VRCX_autoStateChangeAloneDesc") ?? d.AutoStateChangeAloneDesc,
            AutoStateChangeCompanyDescEnabled = ConfigBool("VRCX_autoStateChangeCompanyDescEnabled", d.AutoStateChangeCompanyDescEnabled),
            AutoStateChangeCompanyDesc = Config("VRCX_autoStateChangeCompanyDesc") ?? d.AutoStateChangeCompanyDesc,
            AutoStateChangeGroups = ConfigList("VRCX_autoStateChangeGroups", d.AutoStateChangeGroups),
            AutoAcceptInviteRequests = Config("VRCX_autoAcceptInviteRequests") ?? d.AutoAcceptInviteRequests,
            AutoAcceptInviteGroups = ConfigList("VRCX_autoAcceptInviteGroups", d.AutoAcceptInviteGroups),
            AutoAcceptInviteStatuses = ConfigList("VRCX_autoAcceptInviteStatuses", d.AutoAcceptInviteStatuses),
        };
    }

    /// <summary>
    /// cache_avatar and cache_world: avatars and worlds as VRChat returned them -> avatars / worlds,
    /// filling a row that is missing or only a stub. A row this server fetched itself is newer.
    /// </summary>
    private async Task CachedEntitiesAsync()
    {
        foreach (var (table, target) in new[] { ("cache_avatar", "avatars"), ("cache_world", "worlds") })
        {
            var report = _report.Table(table, target);
            var rows = new List<object?[]>();
            foreach (var r in _src.Rows(table, "id", "added_at", "author_id", "author_name", "created_at", "description", "image_url", "name", "release_status", "thumbnail_image_url", "updated_at", "version"))
            {
                report.Read++;
                if (r.String(0) is not { } id)
                {
                    report.Skip("no id");
                    continue;
                }
                var added = r.Time(1) ?? _now;
                rows.Add([id, added, r.String(2), r.Raw(3), r.Time(4), r.Raw(5), r.String(6), r.Raw(7) ?? id, r.String(8), r.String(9), r.Time(10), r.Long(11)]);
            }

            await BulkAsync(report, "stage_cache", [Text("id"), Time("added_at"), Text("author_id"), Text("author_name"), Time("created_at"), Text("description"), Text("image_url"),
                    Text("name"), Text("release_status"), Text("thumbnail_image_url"), Time("updated_at"), Long("version")],
                Rows(rows, r =>
                {
                    if (r[2] is string author) _refs.User(author, r[3] as string, (DateTimeOffset)r[1]!);
                }), async pg =>
                {
                    report.Updated += await pg.ExecuteAsync($"""
                        UPDATE {target} t SET name = s.name, description = s.description, author_id = s.author_id, author_name = s.author_name,
                            image_url = s.image_url, thumbnail_image_url = s.thumbnail_image_url, release_status = s.release_status, version = s.version,
                            created_at = s.created_at, updated_at = s.updated_at, is_stub = false, fetched_at = s.added_at,
                            first_seen_at = LEAST(t.first_seen_at, s.added_at)
                        FROM stage_cache s WHERE t.id = s.id AND t.is_stub
                        """, _ct);
                    report.Inserted += await pg.ExecuteAsync($"""
                        INSERT INTO {target} (id, name, description, author_id, author_name, image_url, thumbnail_image_url, release_status, tags, version,
                                              featured, platforms, created_at, updated_at, is_stub, first_seen_at, fetched_at)
                        SELECT DISTINCT ON (s.id) s.id, s.name, s.description, s.author_id, s.author_name, s.image_url, s.thumbnail_image_url, s.release_status, ARRAY[]::text[], s.version,
                               false, ARRAY[]::text[], s.created_at, s.updated_at, false, s.added_at, s.added_at
                        FROM stage_cache s
                        ON CONFLICT (id) DO NOTHING
                        """, _ct);
                });

        }
    }

    /// <summary>Memos, avatar tags, local favorites and print favorites.</summary>
    private async Task LocalDataAsync()
    {
        // memos / world_memos / avatar_memos -> user_memos / world_memos / avatar_memos. The newer edit wins.
        foreach (var (table, key, target, targetKey, kind) in new[]
                 {
                     ("memos", "user_id", "user_memos", "user_id", "user"),
                     ("world_memos", "world_id", "world_memos", "world_id", "world"),
                     ("avatar_memos", "avatar_id", "avatar_memos", "avatar_id", "avatar"),
                 })
        {
            var report = _report.Table(table, target);
            var rows = new List<object?[]>();
            foreach (var r in _src.Rows(table, key, "edited_at", "memo"))
            {
                report.Read++;
                if (r.String(0) is not { } id)
                {
                    report.Skip("no id");
                    continue;
                }
                rows.Add([id, r.Time(1) ?? _now, r.Raw(2) ?? ""]);
            }
            await BulkAsync(report, "stage_memos", [Text("id"), Time("at"), Text("memo")], Rows(rows, r =>
            {
                var at = (DateTimeOffset)r[1]!;
                if (kind == "user") _refs.User((string)r[0]!, null, at);
                else if (kind == "world") _refs.World((string)r[0]!, null, at);
                else _refs.Avatar((string)r[0]!, at);
            }), async pg =>
            {
                report.Inserted += await pg.ExecuteAsync($"""
                    INSERT INTO {target} ({targetKey}, memo, updated_at)
                    SELECT DISTINCT ON (s.id) s.id, s.memo, s.at FROM stage_memos s
                    ON CONFLICT ({targetKey}) DO NOTHING
                    """, _ct);
                report.Updated += await pg.ExecuteAsync($"""
                    UPDATE {target} t SET memo = s.memo, updated_at = s.at FROM stage_memos s
                    WHERE t.{targetKey} = s.id AND t.updated_at < s.at
                    """, _ct);
            });
        }

        var tags = _report.Table("avatar_tags", "avatar_tags");
        var tagRows = new List<object?[]>();
        foreach (var r in _src.Rows("avatar_tags", "avatar_id", "tag", "color"))
        {
            tags.Read++;
            if (r.String(0) is not { } avatarId || r.Raw(1) is not { } tag)
            {
                tags.Skip("no avatar_id or tag");
                continue;
            }
            tagRows.Add([avatarId, tag, r.String(2)]);
        }
        await BulkAsync(tags, "stage_tags", [Text("avatar_id"), Text("tag"), Text("color")], Rows(tagRows, r => _refs.Avatar((string)r[0]!, _now)), async pg =>
        {
            tags.Inserted += await pg.ExecuteAsync("""
                INSERT INTO avatar_tags (avatar_id, tag, color) SELECT DISTINCT ON (s.avatar_id, s.tag) s.avatar_id, s.tag, s.color FROM stage_tags s
                ON CONFLICT (avatar_id, tag) DO NOTHING
                """, _ct);
        });

        // Local favorites belonged to the PC: every account there saw them, so every account gets them.
        var groupNames = new List<object?[]>();
        foreach (var name in ConfigList("VRCX_localFavoriteFriendsGroups", [])) groupNames.Add(["friend", name, _now]);
        foreach (var (table, column, type) in new[] { ("favorite_friend", "user_id", "friend"), ("favorite_world", "world_id", "world"), ("favorite_avatar", "avatar_id", "avatar") })
        {
            var report = _report.Table(table, $"local_favorites (type {type}, once per account) + local_favorite_groups");
            var rows = new List<object?[]>();
            foreach (var r in _src.Rows(table, column, "group_name", "created_at"))
            {
                report.Read++;
                if (r.String(0) is not { } target || r.Raw(1) is not { } group)
                {
                    report.Skip("no target or group_name");
                    continue;
                }
                var at = r.Time(2) ?? _now;
                foreach (var account in _accounts) rows.Add([account.UserId, type, group, target, at]);
                groupNames.Add([type, group, at]);
            }
            await BulkAsync(report, "stage_favorites", [Text("owner"), Text("type"), Text("group_name"), Text("target"), Time("at")], rows, async pg =>
            {
                report.Inserted += await pg.ExecuteAsync("""
                    INSERT INTO local_favorites (owner_user_id, type, group_name, target_id, created_at)
                    SELECT DISTINCT ON (s.owner, s.type, s.group_name, s.target) s.owner, s.type::favorite_type, s.group_name, s.target, s.at FROM stage_favorites s
                    ON CONFLICT (owner_user_id, type, group_name, target_id) DO NOTHING
                    """, _ct);
            });
        }
        var groupsReport = _report.Table("favorite_* group names + config VRCX_localFavoriteFriendsGroups", "local_favorite_groups (once per account)");
        var groupRows = groupNames.SelectMany(g => _accounts.Select(a => new object?[] { a.UserId, g[0], g[1], g[2] })).ToList();
        groupsReport.Read = groupNames.Count;
        await BulkAsync(groupsReport, "stage_favorite_groups", [Text("owner"), Text("type"), Text("name"), Time("at")], groupRows, async pg =>
        {
            groupsReport.Inserted += await pg.ExecuteAsync("""
                INSERT INTO local_favorite_groups (owner_user_id, type, name, created_at)
                SELECT s.owner, s.type::favorite_type, s.name, min(s.at) FROM stage_favorite_groups s GROUP BY s.owner, s.type, s.name
                ON CONFLICT (owner_user_id, type, name) DO NOTHING
                """, _ct);
        });

        var prints = _report.Table("favorite_print", "print_favorites (once per account)");
        var printRows = new List<object?[]>();
        foreach (var r in _src.Rows("favorite_print", "print_id", "created_at"))
        {
            prints.Read++;
            if (r.String(0) is not { } printId)
            {
                prints.Skip("no print_id");
                continue;
            }
            foreach (var account in _accounts) printRows.Add([account.UserId, printId, r.Time(1) ?? _now]);
        }
        await BulkAsync(prints, "stage_prints", [Text("owner"), Text("print_id"), Time("at")], printRows, async pg =>
        {
            prints.Inserted += await pg.ExecuteAsync("""
                INSERT INTO print_favorites (owner_user_id, print_id, created_at)
                SELECT DISTINCT ON (s.owner, s.print_id) s.owner, s.print_id, s.at FROM stage_prints s
                ON CONFLICT (owner_user_id, print_id) DO NOTHING
                """, _ct);
        });
    }

    /// <summary>Keys left out of legacy_settings: the saved logins (passwords and session cookies).</summary>
    private static readonly HashSet<string> SecretConfigs = new(StringComparer.OrdinalIgnoreCase) { "config:savedcredentials" };

    /// <summary>
    /// configs -> legacy_settings, every key verbatim (the desktop UI preferences have no other
    /// home yet), except the saved logins. VRCX_id also becomes the server's VRCX_id when it has
    /// none, and the settings this server acts on went into the accounts the import created.
    /// </summary>
    private async Task SettingsAsync()
    {
        var report = _report.Table("configs", "legacy_settings (+ server_settings VRCX_id, accounts.settings of created accounts)");
        var rows = new List<object?[]>();
        foreach (var (key, value) in Configs())
        {
            report.Read++;
            if (SecretConfigs.Contains(key))
            {
                report.Skip("saved VRChat logins (passwords and session cookies): a secret, and this server keeps its own sessions");
                continue;
            }
            rows.Add([key, value]);
        }
        await BulkAsync(report, "stage_configs", [Text("key"), Text("value")], rows, async pg =>
        {
            report.Inserted += await pg.ExecuteAsync("""
                INSERT INTO legacy_settings (key, value, imported_at) SELECT s.key, s.value, now() FROM stage_configs s
                ON CONFLICT (key) DO NOTHING
                """, _ct);
            report.Updated += await pg.ExecuteAsync("""
                UPDATE legacy_settings t SET value = s.value, imported_at = now() FROM stage_configs s WHERE t.key = s.key AND t.value <> s.value
                """, _ct);
            if (Config("VRCX_id") is { Length: > 0 } vrcxId && !await pg.Db.ServerSettings.AnyAsync(s => s.Key == "VRCX_id", _ct))
            {
                pg.Db.ServerSettings.Add(new ServerSetting { Key = "VRCX_id", Value = vrcxId });
                await pg.Db.SaveChangesAsync(_ct);
                report.Updated++;
            }
        });
    }
}
