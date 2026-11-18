# VRCXD: instructions for coding agents

VRCXD is a self-hosted port of [VRCX](https://github.com/vrcx-team/VRCX) (git remote `vrcx`). A
server on a NAS tracks VRChat all the time, a static web client shows it, and a headless agent on
the gaming PC ships the game log and does what needs the PC. `README.md` is the overview for
people; this file is the rules for agents. Each part has its own `AGENTS.md` with the rules that
apply only there.

## Repo map

| Path | What | Read |
| --- | --- | --- |
| `server/` | ASP.NET Core (.NET 11), EF Core on Postgres, Redis, Hangfire. REST under `/1/`, the protobuf websocket `/1/ws`, MCP at `/1/mcp` | `server/README.md`, `server/AGENTS.md` |
| `server/Common/Protos/vrcx_live.proto` | The one websocket contract, shared by server, client and agent | |
| `server/Testing/` | The integration test harness (`VrcxScenario` and the fakes) | `server/Testing/README.md` |
| `server/LegacyImport/` | One-time import of an original VRCX database | `server/LegacyImport/README.md` |
| `client/` | SvelteKit static web client, Svelte 5 runes, paraglide i18n | `client/README.md`, `client/AGENTS.md` |
| `agent/` | Headless Rust agent for the gaming PC | `agent/README.md`, `agent/AGENTS.md` |
| `docs/desktop-ipc.md` | The VRCX named pipe the agent runs | |
| `legacy/` | The original VRCX sources, unchanged. Reference only | |
| `.github/workflows/` | CI per part (`server.yml`, `client.yml`, `agent.yml`) and `repo.yml` | |

## Commands

Run the checks of every part you touched before calling the work done. They are what CI runs.

```sh
# server (run from server/ so its global.json applies; tests need Docker)
cd server
dotnet build VRCXBackend.slnx                 # Debug: warnings are errors. CI builds -c Release
dotnet test VRCXBackend.slnx                  # everything; slow
dotnet test --project API.IntegrationTests --treenode-filter "/*/*/McpTests/*"   # one class
dotnet test --project API.IntegrationTests --treenode-filter "/*/*/McpTests/*" --list-tests  # preview
dotnet tool restore && dotnet ef migrations has-pending-model-changes --project Common \
    --startup-project MigrationHelper --context MigrationVrcxContext   # CI fails on pending changes

# client
cd client
pnpm install --frozen-lockfile
pnpm run check                                # buf + paraglide generate, svelte-check
pnpm test                                     # vitest
pnpm run test:e2e                             # Playwright against server/Testing.E2EHost (Docker)

# agent
cd agent
cargo fmt --check
cargo clippy --all-targets --locked -- -D warnings
cargo test --locked
```

Prefer one test class or file over a whole suite while iterating. The full server suite starts
many Postgres databases; run it once at the end, not in a loop.

## Testing rules

- Every feature and every fix comes with tests in the same commit. Every upstream VRCX bug that
  applies to the port gets a regression test.
- Server: TUnit. Behaviour goes in `server/API.IntegrationTests` through `VrcxScenario`
  (`server/Testing/README.md`); pure logic and stores go in `server/Common.Tests`.
- Client: vitest under `client/src/lib/__tests__`. Browser flows: Playwright in `client/e2e`.
- Agent: `#[cfg(test)]` modules next to the code in `agent/src`.
- Put logic in plain modules that tests can import (`.ts`, `.svelte.ts`, a service class, a Rust
  function), not inside a component or a controller action.
- Upstream items are tagged so `server/Testing/upstream-coverage.py` counts them: server
  `[Test, Issue(n)]` / `Pr(n)` with a name starting `Issue_n_` or `Pr_n_` (LedgerTests and
  UpstreamTagTests enforce it), client test titles starting `upstream #n`, agent test functions
  named `upstream_<n>_...`. Items no part can test go in
  `server/API.IntegrationTests/Regressions/NotApplicable.cs`. The skill
  `upstream-regression-test` has the steps.
- A test that finds a bug the change is not about stays in, marked `[Skip("Bug: ...")]` (or
  `test.skip`, `#[ignore]`), and is reported. Do not fix unrelated production code silently.

## Architecture rules

These are decided. Follow them; do not work around them.

- **VRChat requests are scarce.** Every call goes through `VrchatApiClient` (`server/Vrchat/`)
  and the one shared `VrchatRateLimiter` (5 per second, 60 per minute, per IP across all
  accounts). Never make a second `HttpClient` to VRChat, never call VRChat in a loop per item
  when a list endpoint or stored data answers, and pin new request patterns in
  `VrchatRequestBudgetTests`.
- **SQL round trips are bounded.** No query per item in loops over friends, groups or players.
  `QueryCountTests` pin the bounds with `app.Sql`; extend them when you add such a flow.
- **REST and MCP share one query.** Anything both read lives in its feature's folder,
  `server/Common/Features/<Feature>/` (see Target architecture). Controllers and MCP tools stay
  thin.
- **Live data is moving to topics.** `/1/ws` is moving from "notify, then the page GETs" to
  subscribed topics: a typed protobuf snapshot, then deltas, a fresh snapshot after a reconnect.
  Topics are independent streams, not request/response. New live data should be a topic, not a
  new `ServerEvent` plus a REST refetch. The friends and notifications topics exist; see the
  skill `add-live-topic` and ask the owner before adding another.
- **Notification decisions move to the server (direction, not current state).** Which
  notifications and wrist-feed rows to show will be decided on the server, so the agent and MCP
  work with no tab open. Their settings move from browser `localStorage` into account settings,
  imported once from `localStorage`. The agent's notify and wrist-feed relay endpoints
  (`AgentNotify.cs`, `AgentWristFeed.cs`) go away once the server decides. Do not add new
  decision logic of that kind to the client.
- **MCP v1 is read-only plus memos.** Tools read Postgres and Redis and never call VRChat. The
  only writes are VRCXD's own memos. Any tool that writes to VRChat needs scoped, revocable
  tokens first; do not add one before they exist (skill `add-mcp-tool`).
- **`legacy/` is reference.** Read it to see what the original does; never edit it by hand.
  It changes only when upstream is merged (skill `port-upstream`).

### Target architecture

The structure the code is moving to. It comes in feature by feature, together with the live
topics; there is no big-bang move. Code not yet moved keeps its current shape until its feature
is moved.

Server (details in `server/AGENTS.md`):

- Feature folders inside the existing projects: `server/Common/Features/<Feature>/` holds that
  feature's queries, its writer, its topic (snapshot and deltas) and small queryable extensions.
  Name a feature after upstream's store or coordinator where one exists.
- One thin controller per feature, on the same routes. The pure VRChat proxy controllers (about
  20) stay as they are.
- Controllers and MCP tools call `Features/*`. They do not touch `VrcxContext`,
  `VrchatApiClient` or `IAccountSessionManager` directly.
- Only a feature's writer changes its topic tables and emits its deltas.
- A topic's snapshot builder is the same code that serves the REST GET and the MCP read.

Client (details in `client/AGENTS.md`):

- One store per live topic, `client/src/lib/live/topics/<topic>.svelte.ts`: a thin `$state`
  shell over a pure reducer module `<topic>.ts` (`applySnapshot`, `applyDelta`) that vitest
  covers.
- Components do not call `#lib/api/client` for a feature that has a store.

Enforcement: architecture ratchet tests, `server/API.IntegrationTests/Tests/ArchitectureTests.cs`
and `client/src/lib/__tests__/architecture.test.ts`. Today's violations go on a checked-in
allow-list that may only shrink; a new violation fails the suite. Never add to the allow-list to
make a change pass.

Rejected. Never do these:

- a Domain / Application / Infrastructure project split;
- a generic `IRepository<T>`;
- MediatR or pipeline behaviours;
- route or proto shape changes as part of a refactor;
- large file moves while other worktrees are active.

## Code and text

- Match the code around you. Read a neighbouring file before writing a new one.
- Comments say why, not what. When behaviour comes from the original, say so and name it: "as the
  original's `onGroupLeft`", "the limits upstream pins itself to in
  `src/shared/utils/throttle.js`". Write full sentences in plain English; no section banners or
  restating the code.
- `.cs`, `.csproj`, `.slnx` and the other files `.gitattributes` lists are CRLF; everything else
  is LF. Scripted edits (`sed`, Python) must keep the file's line endings. Check with
  `git diff --stat` and `git ls-files --eol <file>`.
- Never write raw control bytes (NUL and the like) into a text file; use escapes such as `\0`.
  `repo.yml` fails the build on any text file git sees as binary.
- Do not edit generated code: `client/src/lib/gen` (buf, from the proto), `client/src/lib/paraglide`
  (paraglide, from the locale files), the EF `*.Designer.cs` and `MigrationVrcxContextModelSnapshot.cs`
  files (`dotnet ef`). Change the source and regenerate.
- Do not hand-edit locale files other than `client/src/lib/localization/en.json`, except to copy
  upstream's own translations when porting (skill `client-i18n-string`).

## Commits

- Commit on `develop`. `master` only receives merges from `develop`; a merge that brings change
  files cuts a release (see Releases). Never create other branches.
- Commits are SSH-signed (`commit.gpgsign`). Do not pass `--no-gpg-sign`. If signing fails, stop
  and tell the owner.
- Conventional commits: `type(scope): summary`, lower case, imperative, no full stop. Types in use:
  `feat`, `fix`, `perf`, `test`, `ci`, `chore`, `docs`. Scope is the part (`server`, `client`,
  `agent`); leave it out when a commit spans parts.
- The body explains why in plain prose, wrapped near 72 columns: what users saw or what was wrong,
  what changes, and what was left out. Lists are fine for several items. Cite upstream as
  "Upstream #1455:" or by commit hash.
- Upstream ports carry the record format of `git log --grep "port upstream"` and `cbd077de`:
  `Brings the changes from vrcx-team/VRCX <from>..<to> into the ...`, then a `Ported:` list and a
  `Not applicable to the port:` list, one line per upstream commit with its hash and why.
- Commit only when asked.

## Releases

- Releases are made by OpenShock's release-tool (`.github/workflows/release.yml`) from change files
  in `.changes/` (format in `.changes/README.md`). A change users would notice (server, client or
  agent behaviour) gets one, in the same commit: a `.md` file with `kind:` frontmatter and an
  imperative one-line title. `chore` changes stay out of the release notes. Docs, CI, `legacy/`
  and `.claude/` never need one (`ignore_paths` in `.changes/config.json`).
- `release-tool new "<title>" --kind fixed` writes one; it needs a terminal, so outside one write
  the file by hand. `release-tool status` prints the coming version.
- On `develop` the workflow only computes the coming version. On `master` it writes the version
  into `client/package.json` and `CHANGELOG.md`, moves the change files to `.changes/released/`,
  and tags `vrcxd-v<version>`. `publish.yml` then pushes the API and web images tagged with the
  version and attaches the agent binaries to the GitHub release. Tags use the `vrcxd-v` prefix
  because clones with the `vrcx` remote have upstream's `v<date>` tags, which release-tool would
  read as versions.
- Do not edit `CHANGELOG.md`, `.changes/released/` or the version in `client/package.json` by hand.

## Safety

- Never push, and never post to GitHub (issues, PRs, comments, reviews), unless the owner asks for
  that specific action. Reading public upstream data is fine.
- Never put personal data (names, emails, account ids, tokens, cookies) in requests to outside
  services or in committed files. Tests use made-up `usr_` ids.
- Nothing in a test may reach VRChat: `FakeVrchatApi` throws on any URI outside `/api/1/`.
- No destructive git (`reset --hard`, `clean -f`, `push --force`, rewriting published history)
  without the owner asking.
