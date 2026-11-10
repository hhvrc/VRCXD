# VRCX.Testing: the integration test harness

`VRCX.Testing` runs the real server (the `API` host, its controllers, ingestors, stores, Postgres
and Redis) against a fake VRChat. Every test gets its own world:

| Piece | What it is |
| --- | --- |
| `SharedContainers` | One `postgres:18` and one `redis:8-alpine` container per test run (Testcontainers, so Docker must be running). Each scenario gets a fresh database cloned from a migrated template (`CREATE DATABASE ... TEMPLATE vrcx_template`, a few ms) and its own Redis logical database. |
| `VrcxScenario` | The entry point. Starts the API host (`WebApplicationFactory<Program>`, in memory or on a real Kestrel port) wired to all the fakes below. Background hosted services are off; their work runs on demand through `app.Jobs`. |
| `FakeVrchatApi` (`app.Vrchat`) | A stateful VRChat: users, friends, worlds, avatars, groups, instances, notifications, favorites, moderations, notes. Modelled GETs answer from that state; writes that the server's flows rely on (favorites, moderations, notification hide/accept, profile edits) change it; other writes answer `200 {}`. Unknown GETs are 404 with VRChat's error envelope. Records every request. A URI outside `/api/1/` throws, so nothing leaves the machine. |
| `FakeImageFetcher` (`app.Vrchat.Images`) | The image cache's downloader. Every image is a 1x1 PNG. |
| `FakePipeline` (`app.Pipeline`) | VRChat's pipeline websocket. Frames go through `PipelineEvent.Parse` and `PipelineService.DispatchAsync`, exactly the path the real socket uses, and any error the server logs while handling one fails the call. Helpers keep the fake API in step (`FriendAddAsync` also makes them a friend). |
| `FakeDesktop` (`app.ConnectDesktopAsync`) | The desktop app on `/1/ws`: ships parsed game log lines (`GameLog.*` builders), reports the game running, receives and completes actions. |
| `LiveClient` (`app.ConnectLiveAsync`) | A browser's live connection on `/1/ws`. `WaitForAsync<T>(match)` awaits a `ServerEvent` payload; `AssertNoneAsync<T>` proves one did not come. |
| `ScenarioClock` (`app.Time`) | The server's `TimeProvider`: wall clock plus an offset the test moves with `Advance`. |
| `SyncTracker` (`app.Syncs`) | Counts friend and full syncs. A login through `app.Api` returns only after the full sync it starts in the background has finished, so tests never race it. |
| `CapturedLogs` (`app.Logs`) | Everything the server logged. `VRCX_TEST_LOG=1` echoes it to the console. |

```csharp
[Test]
public async Task A_friend_going_offline_is_held_until_the_delay_passes()
{
    await using var app = await VrcxScenario.StartAsync();
    var alice = app.Vrchat.Friend("usr_alice", "Alice");
    await app.LoginAsync();                       // POST /1/accounts/login, and its initial sync
    await app.Pipeline.FriendOnlineAsync(alice, "wrld_x:1~region(eu)");
    await using var live = await app.ConnectLiveAsync();

    await app.Pipeline.FriendOfflineAsync(alice);
    await Assert.That(await app.Jobs.SweepPendingPresenceAsync()).IsEqualTo(0);
    await live.AssertNoneAsync<PresenceChanged>();

    app.Time.Advance(PresenceDebouncer.Delay + TimeSpan.FromSeconds(1));
    await Assert.That(await app.Jobs.SweepPendingPresenceAsync()).IsEqualTo(1);
    var offline = await live.WaitForAsync<PresenceChanged>(p => p.UserId == alice.Id);
    await Assert.That(offline.State).IsEqualTo("offline");
}
```

Other things a scenario can do: `app.Api` (an authenticated `HttpClient` for `/1/...`),
`app.CreateDbContext()` to arrange or inspect rows, `app.RestartAsync()` (a new server process
over the same database, Redis and fake VRChat), `ScenarioOptions.ConfigureServices` to replace
a service, and `ScenarioOptions.KestrelPort` for a real socket.

`server/API.IntegrationTests/IsolatedHost.cs` adapts a scenario to TUnit's `ClassDataSource`
for the older test classes; new tests just `await using` a scenario.

## Running

```sh
cd server
dotnet test VRCXBackend.slnx                      # everything (Docker must be running)
dotnet test --project API.IntegrationTests -- --treenode-filter "/*/*/*/*[Category=Regression]"
dotnet test --project API.IntegrationTests -- --treenode-filter "/*/*/*/*[Upstream=vrcx-team/VRCX#1228]"
```

The browser tests in `client/e2e` run the built client against `server/Testing.E2EHost`, which
is one scenario on port 5299 with a seeded account (`usr_e2e_me`, friends Alice, Bob, Carol)
and a control API on 5298 (`POST /friends/{id}/online`, `/friends/{id}/offline`,
`/notifications/friend-request?from=&name=`, `/pipeline` with a raw frame):

```sh
cd client
pnpm exec playwright install chromium             # once
pnpm run test:e2e                                 # builds, starts both servers, runs the tests
```

## Turning an upstream issue or PR into a test

The closed issues and PRs of vrcx-team/VRCX are exported outside the repo (on the dev machine,
`E:\src\pgvrcx\tmp\vrcx-github\`: `all_closed.json`, `issues/{n}.json`, `pulls/{n}.json` and
`pulls/{n}.diff`). Read them by absolute path. Never copy them, or quote long parts of them,
into the repo.

1. **Pick one.** `python -I server/Testing/upstream-coverage.py <export path> --todo 20 --grep friend`
   lists items with neither a test nor a not-applicable entry.
2. **Read it.** The issue body and comments say what users saw. For a PR, the `.diff` says
   what the fix changed, which is usually the clearest statement of the expected behaviour.
   Then find where the port does the same thing (`server/Common/Services/...` for ingestion
   and stores, `server/API/Controller/...` for endpoints, `client/src` for UI).
3. **Decide whether it applies.** It does not when the behaviour lives entirely in:
   - the desktop app (`desktop/`: game log parsing, launching VRChat, OSC, Discord presence,
     screenshots on disk). Log-line parsing regressions belong in the desktop app's own tests;
     the server side starts at a parsed `LogEntry`, so test what the server does with it;
   - the original's VR overlay, Electron/CEF shell, installer or updater;
   - pure UI with no server behaviour (a Playwright test in `client/e2e` if worth it);
   - a question, a duplicate, or a feature the port deliberately dropped.

   Record it in `server/API.IntegrationTests/Regressions/NotApplicable.cs` with a short reason
   using one of the prefixes there. `LedgerTests` checks the ledger stays consistent.
4. **Write the test** in `server/API.IntegrationTests/Regressions/`, in the file for its area
   (`FriendRegressions.cs`, `NotificationRegressions.cs`, `GameLogRegressions.cs`, or a new
   `<Area>Regressions.cs` with `[Category("Regression")]` on the class).
   - Name: `Issue_1234_<what must hold>` or `Pr_1234_<what must hold>`, in words, for example
     `Issue_1228_a_friend_changing_their_name_is_a_name_change_not_an_unfriend`.
   - Tag it `[Test, Issue(1234)]` (add `Pr(n)` too when a PR fixed that issue).
   - One line of comment: what users saw, in your own words.
   - Arrange with the fakes, act the way VRChat, the desktop app or the browser would (a pipeline
     frame, a game log line, an API call), and assert on what a user would see: the API's
     response, a live event, or a row. Assert on the calls made to VRChat with
     `app.Vrchat.AssertCalled` / `AssertNotCalled` / `Bodies`.
   - Reproduce the original's trigger, not only the fixed code path: if the bug came from
     ordering (a restart, a refresh, a sync after a pipeline event), do those steps.
5. **Run it.** If it fails because the port has the bug, keep the test, mark it
   `[Skip("Bug: <one line>, see <file>")]` and report it. Do not change production code from a
   regression test change. If it fails because the fake does not model something, extend the
   fake (`FakeVrchatApi` or the fixtures); model VRChat's real response shape, taken from
   `server/Vrchat/Models` and the original's code, not what makes the test pass.

### Time

`app.Time` drives what reads the server's `TimeProvider`: the pipeline ingestor, the fake
desktop's log timestamps, the fake VRChat's notification times, and
`app.Jobs.SweepPendingPresenceAsync()`. Code that reads `DateTimeOffset.UtcNow` directly (most
controllers and the sync service, for now) sees only the wall clock, which the scenario clock
also follows, so both agree until a test calls `Advance`. After that, rows written by the
clock-aware parts are ahead of the rest. Tests that compare times across both kinds of code
should do so before advancing.

### Fake VRChat state

`app.Vrchat` starts empty. `LoginAsync` creates `usr_me` (or use `SetMe` first to pick the id
and name). Fixture builders (`FakeUser`, `FakeWorld`, `FakeGroup`, `FakeInstance`,
`FakeNotification`, ...) are mutable JSON with fluent setters; mutate one and the fake's
answers change with it. For a one-off answer use `app.Vrchat.Json(path, json)`,
`Fail(method, path, status, message)` or `On(method, path, handler)`; those win over the model,
newest first.

## Not covered yet

- Hosted services (Pipeline socket loop, presence partitions, profile and stub refresh,
  auto-status) are off; `app.Jobs` runs the ones tests need. Add a job there when a test needs
  another.
- The avatar search providers (`Search.cs`) call user-configured URLs with their own
  `HttpClient`; no test configures one, and none may.
- VRChat's rate limiting and 2FA variants beyond TOTP and email are not modelled.
