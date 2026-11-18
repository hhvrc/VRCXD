# Server: rules for coding agents

The root `AGENTS.md` applies here too. `README.md` describes the layout, model, ingestion,
transport, API and MCP; `Testing/README.md` describes the test harness. Read the section you are
changing before you change it.

## Where things go

| Change | Where |
| --- | --- |
| A VRChat call | `Vrchat/VrchatApiClient.cs`, models in `Vrchat/Models/` (skill `add-vrchat-endpoint`) |
| Storing what VRChat returned | `Common/Services/Ingestion/VrchatStore.cs`, the only writer of VRChat data. Its methods take the caller's `VrcxContext` and do not save |
| A read that REST and MCP share | `Common/Services/Queries/`, or `Common/Features/<Feature>/` for a moved feature; registered scoped in `Common/VrcxServiceHelper.cs` |
| An endpoint | `API/Controller/<Area>/`, in the area's controller (a `partial` class when it spans files), `[MapToApiVersion("1")]`. It takes feature classes, never the database or a session |
| An API error | `Common/Errors/Errors.cs`: one static class per domain, stable `Domain.Reason` type strings |
| A recurring job | `API/Jobs/`, found by `[CronJob]` |
| A live event | today `ILiveEventBus.Publish` after the commit, and a case in `vrcx_live.proto`. New live data is meant to be a topic (skill `add-live-topic`) |
| An MCP tool | `API/Mcp/` (skill `add-mcp-tool`) |
| The model | `Common/VrcxDb/`, then a migration (skill `add-ef-migration`) |

## Target architecture

The root `AGENTS.md` (Target architecture) is the summary; this is how it applies here. It is
introduced feature by feature with the live topics. Until a feature has moved, the table above
is where its code lives.

- A moved feature lives in `Common/Features/<Feature>/` inside the existing `Common` project: its
  queries, its writer, its topic (snapshot and deltas) and small queryable extensions
  (`IQueryable<T>` helpers). Name it after upstream's store or coordinator where there is one
  (`legacy/src/stores`, `legacy/src/coordinators`).
- Its controller is thin, one per feature.
- Controllers and MCP tools call `Features/*` only. They do not inject `VrcxContext`,
  `VrchatApiClient` or `IAccountSessionManager`. Endpoints that only forward to VRChat are no
  exception: their controller derives from `API/Controller/AccountForwardController.cs`, whose
  `RunAsync`, `ReadLiveAsync` and `FetchAsync` run one call on the account's client through
  `AccountSessionRunner` (`API/Controller/Groups/` is the example). What such an endpoint reads or
  writes in the database goes through a feature class, as `GroupQueries` does for the group
  pop-up's visit counts.
- A feature method that can fail returns `Result<T>` (`Common/Result.cs`), a union of the value and
  the problem: write `return AccountError.NoLiveSession;` or `return avatar;`, and declare
  `Result<T?>` when finding nothing is an answer and not a failure. A controller answers with
  `Answer(result)` (200, 204 for nothing, or the problem), or `Answer(result, value => ...)` to
  shape the value first. More than two outcomes get a `closed` record hierarchy as the value
  (`Result<LoginOutcome>`), switched over without a default arm so a new case fails the build. A
  method with nothing to hand back returns a nullable problem. Do not add a record that pairs a
  nullable value with a nullable problem.
- Only the feature's writer changes its topic tables and emits its deltas. No `ExecuteUpdate` or
  `ExecuteDelete` on those tables anywhere else: bulk writes bypass `LiveChangeInterceptor` and
  publish nothing, so pages go stale.
- The topic's snapshot builder is the code that serves the REST GET and the MCP read. Do not
  write a second query for one of them.
- `API.IntegrationTests/Tests/ArchitectureTests.cs` is the ratchet: it fails when a controller or
  MCP tool takes one of those three. Its allow-list of older violations is empty and stays empty.
- Friends is the moved feature to copy: `Common/Features/Friends/` (`FriendQueries`,
  `FriendActions` for what goes to VRChat, `FriendWriter`, `FriendsTopic`) and
  `API/Controller/Friends/FriendsController.cs`. GameLog, Memos, Feed, Notifications,
  Moderation, Favorites, Charts, UserActivity, QuickSearch and the avatar history (in `Avatars`)
  have moved the same way, and so have the get-by-id controllers (users, worlds, avatars, groups,
  instances). `API/Controller/Accounts/` holds only the account list, login and saved logins,
  done by `Features/Accounts/AccountLogin`. What an account does with VRChat lives in a
  forwarding controller per area (`Groups`, `Worlds`, `Avatars`, `Instances`, `Search`, `Tools`,
  `Profile`, `Users`, `Desktop`, `Settings`), each with the feature classes for what it stores.
  `Features/Accounts/AccountQueries` answers a feature
  controller's "is this account logged in", `Features/Accounts/AccountSessionRunner` runs an
  actions class's VRChat calls with the account's session and maps the failures to problems, and
  `Features/Locations/LocationQueries` names location tags for the MCP tools.
- Never: a Domain / Application / Infrastructure project split, a generic `IRepository<T>`, or
  large file moves while other worktrees are active.

## Rules

- Build in Debug before you finish: `dotnet build VRCXBackend.slnx`. Debug treats warnings as
  errors; Release, which CI builds, does not, so a Release-only build misses them.
- Keep `.cs` files CRLF. If you write them with a script, convert with `unix2dos` or write `\r\n`.
- Every VRChat call must be visible in a test. Arrange with `app.Vrchat` and assert with
  `AssertCalled`, `AssertNotCalled`, `CountOf` or `Calls`. A flow that could repeat a call
  (a sync, a dialog open, a refresh) gets a budget test in `VrchatRequestBudgetTests`.
- Flows over lists the account controls (friends, groups, players, game-log batches) get a
  round-trip bound in `QueryCountTests` with `app.Sql.CountAsync`. Preload rows before the loop.
- New code that compares or stores times reads the injected `TimeProvider`, so `app.Time.Advance`
  moves it. Older code reads `DateTimeOffset.UtcNow`; see "Time" in `Testing/README.md`.
- Moments VRChat reports (occupants, member counts online, heat) are not stored. Anything relative
  to the viewing account lives in a connecting table, never on the entity.
- Extend the fakes in `Testing/` when a test needs VRChat behaviour they lack. Model VRChat's real
  response shape (from `Vrchat/Models` and the original's code), not what makes the test pass.

## Tests

- Integration tests: `API.IntegrationTests/Tests/<Area>Tests.cs`, `await using var app = await
  VrcxScenario.StartAsync();`. Regressions from upstream: `API.IntegrationTests/Regressions/`.
- Unit tests: `Common.Tests/`, with its own `Issue`/`Pr` attributes in `Upstream.cs`.
- Run one class: `dotnet test --project API.IntegrationTests --treenode-filter "/*/*/<Class>/*"`.
  Docker must be running (Testcontainers starts Postgres 18 and Redis 8).
- `VRCX_TEST_LOG=1` echoes the server's log to the console.
