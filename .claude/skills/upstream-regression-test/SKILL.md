---
name: upstream-regression-test
description: Write a regression test for a closed vrcx-team/VRCX issue or PR in the server, web client or agent, with the tags and names server/Testing/upstream-coverage.py counts, or record it as not applicable. Use when asked to cover an upstream issue or PR, to triage upstream items, or when a port fixes one.
argument-hint: "[issue-or-pr-number]"
---

# Upstream regression test

The full procedure is "Turning an upstream issue or PR into a test" in `server/Testing/README.md`.
Read it first. These are the steps and the checks.

## 1. Find and read the item

- The closed issues and PRs are exported outside the repo (`all_closed.json`, `issues/{n}.json`,
  `pulls/{n}.json`, `pulls/{n}.diff`). Ask the owner for the path if you do not know it. Read
  them by absolute path; never copy them, or long quotes from them, into the repo.
- Without the export, read one public item with `gh issue view <n> -R vrcx-team/VRCX` or
  `gh pr view <n> -R vrcx-team/VRCX --comments` (`gh pr diff` for the fix). Read only; never
  comment.
- Untriaged items: `python -I server/Testing/upstream-coverage.py <export> --todo 20 --grep friend`.
- Already covered? `git grep -n -e "Issue(<n>)" -e "Pr(<n>)" -e "upstream #<n>" -e "upstream_<n>_"`
  and look for `[<n>] =` in `server/API.IntegrationTests/Regressions/NotApplicable.cs`.

## 2. Pick the part that owns the behaviour

| Behaviour | Test | Tag |
| --- | --- | --- |
| Server (sync, pipeline, game log handling, API, stores) | `server/API.IntegrationTests/Regressions/<Area>Regressions.cs` (class has `[Category("Regression")]`), or the feature's own `Tests/<Area>Tests.cs` | `[Test, Issue(n)]` (add `Pr(m)` when a PR fixed it); outside `Regressions/`: `[Test, Regressions.Issue(n)]`. Name `Issue_n_<what must hold>` or `Pr_n_...` |
| Pure server logic | `server/Common.Tests/<Thing>Tests.cs` | `Issue`/`Pr` from `Common.Tests/Upstream.cs`, same names |
| Client logic | `client/src/lib/__tests__/<module>.test.ts` | title starts `upstream #n` (`upstream #1611 #1758 ...`) |
| Agent (log parsing, launch, IPC, screenshots) | `#[cfg(test)] mod tests` in the `agent/src` file | `fn upstream_<n>_<what must hold>()` |

Write one line of comment above the test: what users saw, in your own words. Assert on what a user
would see (the API response, a live event, a row, the parsed `LogEntry`), and reproduce the
original trigger (a restart, a sync after a pipeline frame), not only the fixed code path.

Server test skeleton (`.cs` files are CRLF):

```csharp
// Users saw <symptom>. <What must hold instead>.
[Test, Issue(1234)]
public async Task Issue_1234_<what_must_hold>()
{
    await using var app = await VrcxScenario.StartAsync();
    var alice = app.Vrchat.Friend("usr_alice", "Alice");
    await app.LoginAsync();
    // act as VRChat, the agent or the browser would: app.Pipeline..., app.ConnectDesktopAsync(), app.Api...
    // assert on what a user sees, and on VRChat calls: app.Vrchat.AssertCalled / AssertNotCalled
}
```

## 3. Not applicable

If no part can test it (Electron/CEF packaging, a question, a dropped feature, UI with no logic),
add `[n] = <Prefix> + "<short reason>",` to `NotApplicable.Items`, in number order, using a prefix
constant there (`Desktop`, `Overlay`, `Packaging`, `ClientOnly`, `Question`). An item that has a
test in any part does not go in the ledger.

## 4. Run it

```sh
cd server && dotnet test --project API.IntegrationTests --treenode-filter "/*/*/*/*[Upstream=vrcx-team/VRCX#1234]"
cd server && dotnet test --project API.IntegrationTests --treenode-filter "/*/*/LedgerTests/*"
cd server && dotnet test --project Common.Tests --treenode-filter "/*/*/UpstreamTagTests/*"   # Common.Tests tags
cd client && pnpm test src/lib/__tests__/<module>.test.ts
cd agent && cargo test --locked upstream_1234
python -I server/Testing/upstream-coverage.py <export>      # the item now counts as tested
```

- If it fails because the port has the bug and the task is not to fix it: keep the test, mark it
  `[Skip("Bug: <one line>, see <file>")]` (`test.skip`, `#[ignore = "Bug: ..."]`) and report it.
- If it fails because a fake does not model VRChat's behaviour, extend `FakeVrchatApi` or the
  fixtures with VRChat's real response shape.
