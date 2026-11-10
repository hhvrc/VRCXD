# VRCXD server

The second take on the server (the first, gRPC + SQLite-shaped tables, is gone). Two things
are different:

- **The data model** is a normalized Postgres schema around the things VRChat actually has -
  users, worlds, avatars, groups, instances - with connecting tables between them, instead of
  a mirror of the Electron client's SQLite tables. Every VRChat API call goes through one store
  that upserts into it and records history only when a value actually changed.
- **The backend is laid out like [OpenShock/API](https://github.com/OpenShock/API)**: a `Common`
  library holding the EF Core context, options, services, errors and the shared host wiring;
  an `API` host built on it; a `MigrationHelper` for `dotnet ef`; unit tests against `Common`
  and integration tests that boot the real host with `WebApplicationFactory`. The
  `OpenShock.Internal.*` packages supply problem details, the exception handler and the
  controller base.

Everything server-side is **one process** (`API`): the HTTP API, the pipeline websocket
listener per account, and the Hangfire recurring jobs that pull from VRChat. There is no
separate cron/worker host.

```
Server/
  Vrchat/               typed VRChat REST + pipeline-websocket client (no persistence)
  Common/               VrcxDb (context, models, Postgres enums, migrations), options,
                        authentication, errors, services (sessions, ingestion, sync),
                        VrcxApplication / VrcxServiceHelper / VrcxMiddlewareHelper
  API/                  controllers (one file per endpoint), request/response models,
                        Hangfire jobs ([CronJob]), the pipeline hosted service, Program.cs
  MigrationHelper/      startup project for dotnet ef
  Common.Tests/         TUnit: wire protocol + store semantics (Postgres via Testcontainers)
  API.IntegrationTests/ TUnit: the real host, fake VRChat, Testcontainers Postgres
  docker/               Base + API Dockerfiles, container appsettings
  docker-compose.yml    Postgres + API
  VRCXBackend.slnx
```

## Configuration

The `VRCX:*` configuration schema, as environment variables:

| Variable                  | Required | Default         | Notes                                                        |
|---------------------------|----------|-----------------|--------------------------------------------------------------|
| `VRCX__DB__CONN`          | x        |                 | Npgsql connection string                                     |
| `VRCX__DB__SKIPMIGRATION` |          | `false`         | Integration tests migrate up front and set this              |
| `VRCX__REDIS__CONN`       | x        |                 | Current presence, live game client state, pending presence changes, Hangfire jobs |
| `VRCX__IMAGES__DIR`       |          | `image-cache`   | Where VRChat's images are kept on disk. Safe to delete: they are fetched again |
| `VRCX__KEYS__DIR`         |          | `keys`          | Keys that encrypt the stored VRChat session cookies. Keep out of database backups; losing them means logging in again |
| `VRCX__ERRORREPORTING__DSN` |        | (none)          | A Sentry DSN for the web client's opt-in error reports. Unset: error reporting is off, users are never asked and nothing is sent |
| `VRCX__DB__DEBUG`         |          | `false`         | Sensitive data logging + detailed errors                     |
| `VRCX__AUTH__TOKEN`       | x        |                 | The one API token; `VrcxToken:` header or `Authorization: Bearer` |
| `VRCX__METRICS__ALLOWEDNETWORKS` |   | private ranges  | Who may read `/metrics` and `/1/health` (CIDR list)          |
| `VRCX__API__PATHBASE`     |          |                 | e.g. `/api` when reverse-proxied on a shared host            |

Layered appsettings as in OpenShock: `appsettings.json`, `appsettings.Container.json`,
`appsettings.{Environment}.json`, `appsettings.Custom.json`, then environment, user secrets,
command line.

## Model

Entity rows are **the latest state**; `*_changes` and event tables are the history. A row can
be a *stub* (`is_stub`): we know the id from a reference (a location tag, an author id, a
notification sender) but have never fetched it. Stubs never produce history when filled in.

| Table | What it is |
| --- | --- |
| `users` | Every usr_ id ever seen: profile, tags + derived `trust_level`. Current presence is in Redis. Shared by all accounts. |
| `accounts` | The VRChat accounts logged into this server (FK to `users`), with the cookie jar so restarts resume sessions. The owner side of everything owner-scoped. |
| `friendships` | account ↔ user over time: `started_at`, `ended_at`, `friend_number`. Unfriending ends the row; re-friending opens a new one. This *is* the friend log. |
| `user_display_name_changes`, `user_trust_changes`, `user_status_changes`, `user_bio_changes`, `user_avatar_changes` | Written only when a stored value differs from what VRChat just returned. |
| `presence_events` | Every online/active/offline/location transition, raw, partitioned by month. The record of presence: Redis is rebuilt from it. |
| `instance_visits` | A view: user ↔ instance with `joined_at`/`left_at` and a `source`. Pipeline visits are derived from `presence_events`; game log visits are stored in `stored_instance_visits`. |
| `cached_images` | Which file on disk holds a VRChat image URL. Files are named by SHA-256, so identical images share one. The bytes are never in the database. |
| `worlds`, `avatars` | Content, FK to author in `users`. |
| `instances` | One row per location tag, created when a tag is first seen, filled by `GET instances/…`. FK to world, owner user, group. |
| `groups`, `group_roles`, `group_members`, `group_member_roles` | Groups, roles, memberships (with `membership_status`, `is_representing`, `left_at`) and roles per membership. |
| `notifications` | v1 and v2 feeds in one table (`version`), payload as `jsonb`. |
| `favorites`, `favorite_groups`, `player_moderations`, `user_notes` | VRChat-side per-account lists, synced wholesale. |
| `user_memos`, `world_memos`, `avatar_memos`, `avatar_tags`, `avatar_usages` | VRCXD's own local data. |
| `video_plays`, `portal_spawns`, `sticker_spawns`, `resource_loads` | Game-log facts that are not presence. |
| `game_log_clients` | One row per log-shipping PC: which account plays there, which instance it is in, and how far its log was stored (what a reconnect resumes from). |

Postgres-native enums (`[PgEnum]`, registered through `NpgsqlEnumExtensions`): `user_state`,
`presence_kind`, `visit_source`, `favorite_type`, `instance_access_type`. VRChat vocabularies
it extends without notice (status, membership status, release status, developer type) stay
`text`. Arrays are `text[]`, instants `timestamptz`, JSON `jsonb`, durations `interval`.
Table and column names are explicit snake_case in `VrcxContext`.

## Ingestion

- `VrchatStore` is the only writer. Every method takes the caller's `VrcxContext` and does not
  save, so the caller owns the unit of work. A field an endpoint does not carry is never cleared.
- `PipelineService` (hosted service) keeps one websocket per account open and hands decoded
  events to `PipelineIngestor`. It also picks up accounts logged in since the last tick.
- `FriendSyncJob` (every 5 min) diffs the friend list and applies presence from the current
  user's online/active id lists; `FullSyncJob` (hourly) pulls groups + roles, favorites,
  moderations, notes and notifications. Both are Hangfire recurring jobs found by `[CronJob]`;
  the dashboard is at `/hangfire` behind the same API token.
- `AccountSessionManager` owns one `VrchatApiClient` per account: login + 2FA, cookie
  persistence, resume at startup, and `session_valid = false` on a 401 instead of retrying.
- Every client shares one `VrchatRateLimiter` (`VrchatRateLimiter.Shared`): VRChat limits per
  IP, not per session, so the budget is process-wide across accounts. It holds the limits the
  upstream VRCX client uses on itself - 5 requests/second burst, 60/minute sustained, sliding
  windows - and a 429 parks the whole budget for VRChat's `Retry-After` before the retry. Only
  `*.vrchat.cloud` / `*.vrchat.com` hosts count; tests pass `VrchatRateLimiter.Unlimited`.

## Transport

- **REST** for everything request/response-shaped (below).
- **One websocket, `/1/ws`, carrying protobuf frames both ways** (`Common/Protos/vrcx_live.proto`):
  `ClientMessage` in, `ServerMessage` out. Both envelopes are a `oneof` that grows by adding
  cases; the hellos carry a protocol version. Today the client side sends game-log batches from
  the desktop app (`GameLogBatch` of `LogEntry`, the same shape `desktop/src/log_parser.cpp`
  already produces) and the server side streams `ServerEvent`s (presence, user changes,
  friendships, notifications, instance visits, account sessions) plus `ClientAction`s for the
  PC. Auth is the same token, as a header or `?token=` for browsers. The web UI generates its
  TypeScript from the same `.proto` (protobuf-es / buf).
- Game-log lines become rows through `GameLogIngestor`: the PC's own instance visits
  (`source = game_log`), other players' visits from OnPlayerJoined/Left, video plays and portal
  spawns. Everything the ingestion writes is announced on `ILiveEventBus` after commit, so a
  REST read right after an event sees it.

## API

Versioned under `/1/…`, every route needs the API token, errors are RFC 9457 problem details
with stable `type` strings (`Login.InvalidCredentials`, `User.NotFound`, …). OpenAPI at
`/openapi/1.json`, Scalar at `/scalar/viewer`, health at `/1/health`, Prometheus at `/metrics`.

| Route | |
| --- | --- |
| `POST /1/accounts/login`, `POST /1/accounts/login/2fa` | Log a VRChat account in |
| `GET /1/accounts`, `DELETE /1/accounts/{id}/session`, `POST /1/accounts/{id}/sync` | Accounts, logout, pull everything now |
| `GET /1/accounts/{id}/friends`, `GET /1/accounts/{id}/friend-log?page=&pageSize=` | Current friends with presence; friendships over time |
| `GET /1/accounts/{id}/notifications`, `/moderations`, `/favorites?type=`, `/visits` | The per-account lists the sync mirrors, and the account's own game log |
| `POST`/`DELETE /1/accounts/{id}/friend-requests/{userId}`, `DELETE /1/accounts/{id}/friends/{userId}` | Friend actions on VRChat |
| `GET /1/users/{id}[?refresh=true&account=]`, `GET /1/users/{id}/history` | A user and everything that changed about them |
| `GET`/`PUT`/`DELETE /1/users/{id}/memo`, `GET /1/users/search?q=` | VRCXD's own memo; a live VRChat user search |
| `GET /1/worlds/{id}`, `/1/avatars/{id}`, `/1/groups/{id}`, `/1/instances/{location}` | Same `refresh` semantics |
| `GET /1/accounts/{id}/feed?types=&search=&vip=&from=&to=&limit=` | The feed: friends' moves (GPS), online/offline, status, bio and avatar changes |
| `GET /1/accounts/{id}/activity?from=&to=`, `/activity/dates` | Instance activity from the game log, for the chart |
| `GET /1/accounts/{id}/hot-worlds?days=`, `/hot-worlds/{worldId}/friends?days=` | Worlds friends moved into most, and who |
| `GET /1/accounts/{id}/mutuals`, `GET`/`POST`/`DELETE …/mutuals/fetch`, `POST …/mutuals/{userId}/refresh` | The mutual-friend network and its background fetch |
| `GET`/`PUT /1/accounts/{id}/settings` | The original's app settings (screenshot helper, YouTube key, quit fix, resource loads) |

## Run

```sh
# dev: any Postgres with an empty database; the API migrates on startup
export VRCX__DB__CONN="Host=localhost;Database=vrcx;Username=vrcx;Password=vrcx"
export VRCX__AUTH__TOKEN="something-at-least-16-chars"
export VRCX__REDIS__CONN="localhost:6379"             # any Redis; presence is rebuilt from Postgres if it is empty
dotnet run --project Server/API                        # http://localhost:5280

# prod: see Deploy below
```

## Deploy

The stack is five containers: Postgres, Redis, the API, the web client (Caddy serving the static
build and proxying `/1/...` to the API, websocket included) and a nightly database dump. Browsers
and the desktop app both use the web container's address; the API itself is not published.

On the NAS, with Docker Compose:

```sh
git clone https://github.com/hhvrc/vrcxd && cd vrcxd/Server
cp .env.example .env        # set PG_PASS and VRCX_TOKEN (16+ chars, e.g. openssl rand -hex 24)
docker compose up -d --build                  # build here, or
docker compose pull && docker compose up -d   # use the images CI publishes from master
```

Then open `http://<nas>:5280`, enter the token, and log in to VRChat. In the desktop app, set the
server address to the same `http://<nas>:5280` and the same token.

| Folder (next to the compose file) | Holds | Back it up? |
|---|---|---|
| `postgres-data/` | the database | dumped nightly into `backups/` |
| `backups/` | `vrcx-*.dump`, `BACKUP_KEEP_DAYS` days of them | copy them to another disk (`BACKUP_DIR`) |
| `keys/` | the key encrypting stored VRChat cookies | optional; losing it means logging in to VRChat again |
| `redis-data/`, `image-cache/`, `caddy-*` | rebuildable state | no |

Restore a dump into an empty database:
`docker compose exec -T db pg_restore -U vrcx -d vrcx --clean --if-exists < backups/vrcx-....dump`

HTTPS: set `VRCX_SITE` to a domain that points at the NAS and forward ports 80 and 443 to
`VRCX_PORT` and `VRCX_HTTPS_PORT`; Caddy fetches and renews the certificate. Without that the
token travels in plain HTTP, so keep `VRCX_BIND` on the LAN address and do not forward the port.

Watch `GET /1/health` (allowed from private networks): it says `degraded` and names the account
when a VRChat session is lost or friend sync stalls, while still answering 200.

To import the original VRCX's database once the server has started, see `LegacyImport/README.md`.

## Develop

```sh
dotnet build Server/VRCXBackend.slnx
cd Server && dotnet test VRCXBackend.slnx             # needs Docker; run from Server so its global.json applies
cd Server && dotnet ef migrations add <Name> --project Common --startup-project MigrationHelper \
    --context MigrationVrcxContext -o Migrations      # after changing the model
```

`dotnet ef` 11 is pinned in `Server/.config/dotnet-tools.json` (`dotnet tool restore`).
Debug builds treat warnings as errors.

## Not here yet

- Tools still to come: screenshots. The
  folder shortcuts, VRChat config, launch options and registry backup work on the player's own
  PC, so they belong to the desktop app.
- Game-log lines with no table (api-request, avatar change, udon exceptions) are acknowledged
  and dropped. Screenshot lines are not stored either; they only trigger the desktop app's
  metadata action.
- Momentary counts VRChat reports (occupants, heat, popularity, online members, instance user
  counts) are deliberately not stored; anything relative to the viewing account (membership,
  friendship, favorites, moderations) lives in a connecting table, never on the entity.
- Invite, join and boop actions from the feed's user menu: the web client has no game to
  launch and no invite endpoints yet.
