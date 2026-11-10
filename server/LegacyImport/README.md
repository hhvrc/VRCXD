# LegacyImport

Carries an original VRCX database (`%APPDATA%\VRCX\VRCX.sqlite3`) into this server's Postgres.

## Running it

It runs on the PC that holds the file, not on the server. The file is large, it is in use while
VRCX runs, and it is read once from start to finish. A console tool reads it in place and needs
only a Postgres connection to wherever the server's database is, so nothing has to be uploaded
through the API. The server has to have started once against that database, so its migrations
are applied. The tool checks this and stops if they are not.

```
cd server
dotnet run --project LegacyImport -- --dry-run
dotnet run --project LegacyImport -- --connection "Host=...;Database=vrcx;Username=...;Password=..."
```

| Option | Meaning |
| --- | --- |
| `--sqlite PATH` | The file. Defaults to `%APPDATA%\VRCX\VRCX.sqlite3`. |
| `--dry-run` | Read the file only and print what would be imported. Postgres is not contacted. |
| `--connection CONN` | Npgsql connection string. `VRCX__DB__CONN` works as well. |
| `--game-log-account ID` | Owner of game log stays that the log cannot attribute. Defaults to the account with most stays. |
| `--match-window-seconds N` | How close a feed row and one this server recorded must be to count as the same event. Defaults to 120. |

How the tool treats the source file and the database:

- **The source file is never written.** SQLite opens it with `Mode=ReadOnly` and `PRAGMA query_only`, inside one read transaction. That works while VRCX has the file open and is writing to it, and the whole import sees one consistent snapshot.
- **Each table is its own transaction.** Rows go in by binary `COPY` into a temporary staging table, then set-based `INSERT ... SELECT` statements add the rows that are missing. No row costs a round trip of its own.
- **Safe to run again or resume.** Rows already present are matched by their natural key, so a run that failed halfway is simply run again.
- **Speed.** A dry run over a 776 MB file takes about 20 seconds.

## Coexisting with what this server recorded

The original and this server may both have watched the same things.

- **Game log visits.** A stay (or a player's visit) with the same user, instance, `game_log` source and a join time within 2 seconds is the same row. If this server's copy was left open, it takes the original's leave time.
- **Feed rows.** A presence event, status, bio or avatar change for the same user and the same value within the match window is the same event.
- **Friendships.** Friend sync records a friendship as starting at this server's first sync. If the original knew of it earlier, it now starts then, and it takes the original's friend number.
- **Everything else** has a real key: notification ids, avatar and world ids, (owner, target) pairs, and (at, url) for video plays and resource loads.

## Mapping

`P_` is an account's table prefix (its user id without `-` and `_`).

| Original | Here | Notes |
| --- | --- | --- |
| `P_*` prefixes | `accounts` | An account not yet here is created with `session_valid = false`, so it is waiting for a login. Its settings come from `configs`. |
| `gamelog_location` | `stored_instance_visits` (the account, source `game_log`) | `left_at = created_at + time`. A stay whose time is 0 was never closed (a crash) and ends where it began. `world_name` and `group_name` are kept. The owner is the account whose own join line falls in the stay, else the nearest such stay within a day, else the fallback account. Non-instances (`local:`, `traveling`, offline) are skipped. |
| `gamelog_join_leave` | `stored_instance_visits` (other players, `game_log`), `unidentified_visits` | Join and leave lines are paired per player and instance. A lone leave starts at `created_at - time`. A lone join ends with the stay. Old lines without a user id are resolved by a display name that only one user id ever had; the rest go to `unidentified_visits` (name, instance, joined, left). The PC's own lines are the stay itself. |
| `gamelog_video_play` | `video_plays` | The user id is resolved by name when missing. |
| `gamelog_portal_spawn` | `portal_spawns` | `instance_id` and `world_name` become the target. |
| `gamelog_resource_load` | `resource_loads` | `ImageLoad`/`StringLoad` map to `image`/`string`. |
| `gamelog_event` | `game_log_events` (kind `Event`) | The location is the stay the PC was in. |
| `gamelog_external` | `game_log_events` (kind `External`) | |
| `P_feed_gps` | `presence_events` (kind `location`) | `time` becomes `duration_at_previous`. `display_name`, `world_name` and `group_name` are kept. |
| `P_feed_online_offline` | `presence_events` (kind `online` / `offline`) | Offline leaves `location` (kept as `previous_location`) after `time`. |
| `P_feed_status` | `user_status_changes` | |
| `P_feed_bio` | `user_bio_changes` | `bio_links` JSON becomes `text[]`. |
| `P_feed_avatar` | `user_avatar_changes` | `avatar_id` is filled when the image is a known avatar's. |
| `P_friend_log_current`, `P_friend_log_history` Friend/Unfriend | `friendships` | Friend opens a friendship and Unfriend ends it. Friends from before logging began start just before the account's first record, which the friend log treats as the starting point. `display_name` and `ended_display_name` are kept. |
| `P_friend_log_history` DisplayName / TrustLevel | `user_display_name_changes`, `user_trust_changes` | Stored once per user. The copies other accounts logged of the same change are dropped. |
| `P_friend_log_history` FriendRequest / CancelFriendRequest | `friend_request_events` | |
| `P_notifications` | `notifications` (version 1) | The world, image and message columns become `details`. `expired` becomes `hidden`. Seen. |
| `P_notifications_v2` | `notifications` (version 2) | `data` goes to `details`, `responses` to `responses`, and `details` to `v2_details`. |
| `P_moderation` | `moderations_against` | Users who blocked or muted the account (Photon). These are not the account's own moderations. |
| `P_notes` | `user_notes` | Only where there is no note for the pair. The next notes sync replaces it with VRChat's own. |
| `P_avatar_history` | `avatar_usages` | The later use and the longer time win. |
| `P_mutual_graph_links`, `P_mutual_graph_meta` | `mutual_friend_links`, `mutual_graph_meta` | |
| `P_mutual_graph_friends` | (in links / meta) | A fetched friend with neither links nor meta has nothing more to store. |
| `P_user_profile` | `users.bio`, `bio_links`, `profile_fetched_at` | Only where newer. |
| `cache_avatar`, `cache_world` | `avatars`, `worlds` | Inserted, or filling a stub. |
| `memos`, `world_memos`, `avatar_memos` | `user_memos`, `world_memos`, `avatar_memos` | The newer edit wins. |
| `avatar_tags` | `avatar_tags` | |
| `favorite_friend` / `_world` / `_avatar` | `local_favorites`, `local_favorite_groups` | These were PC-wide, so every account gets them. Friend groups also come from `config:vrcx_localfavoritefriendsgroups`. |
| `favorite_print` | `print_favorites` | Every account gets them. |
| `configs` | `legacy_settings` | Every key verbatim. `VRCX_id` becomes `server_settings` `VRCX_id` when there is none. The settings this server acts on are applied to the accounts it creates. |

Every user, world, group, avatar and instance referenced is created as a stub when it is not
already known, named after the latest name the file has for it.

## Not imported

- **`cookies` and `config:savedcredentials`.** These are VRChat logins and session cookies: secrets. This server keeps its own sessions.
- **`P_activity_*_v2`.** The original's Activity tab cache, built from `feed_online_offline` and `gamelog_location`. Both are imported, and this server computes the cache from them.
- **`sqlite_*`.** SQLite's internal tables.
