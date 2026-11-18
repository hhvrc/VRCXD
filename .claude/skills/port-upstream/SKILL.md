---
name: port-upstream
description: Bring new vrcx-team/VRCX commits into VRCXD. Finds upstream commits since the last recorded port, merges them into legacy/, decides for each whether it is ported or not applicable, ports with tests, and writes the commit-message record. Use when asked to port, sync or catch up with upstream VRCX.
---

# Port upstream VRCX changes

Upstream is the git remote `vrcx` (`https://github.com/vrcx-team/VRCX.git`). Its history is
merged into `master`, where its files live under `legacy/` (git follows the move). The port
itself is a separate commit that records what happened to every upstream commit.

## 1. Find what is new

```sh
git fetch vrcx
git log -1 --grep "Brings the changes from vrcx-team/VRCX" --format=%B   # last range, <from>..<to>
git log --oneline --no-merges <to>..vrcx/master                          # the new commits
```

`<to>` is the last hash of the range in the newest port commit (`1cd0aaaa`: `bfd91089..e819c4e4`).
If `git log <to>..vrcx/master` is empty, there is nothing to port.

## 2. Merge upstream into legacy/

Ask the owner before merging; it is a commit on `master`.

```sh
git merge vrcx/master            # merge commit "Merge remote-tracking branch 'vrcx/master'"
```

- Every changed path must end up under `legacy/`. Check `git diff --stat HEAD^1 HEAD` after the
  merge: a new upstream file at the root (`src/...`, `Dotnet/...`) goes to `legacy/` at the same
  path, in the merge commit.
- Resolve conflicts only inside `legacy/`, in upstream's favour. Never resolve a conflict by
  changing `client/`, `server/` or `agent/`.
- Root files upstream also has (`.gitignore`, `.github/`) can conflict. Keep VRCXD's version and
  mention it.

## 3. Decide each commit

For each commit, `git show <hash>` (upstream paths are `src/...`; the same file is now
`legacy/src/...`). Then find where the port does the same thing:

- UI, stores, coordinators (`src/...`) -> `client/src/...`, often with a server side in
  `server/Common/Services` or `server/API/Controller`.
- `Dotnet/` behaviour on the PC (game log, launch, IPC, screenshots, overlay) -> `agent/src`.
- `src/localization/*.json` -> `client/src/lib/localization/*.json` (skill `client-i18n-string`).

It is **ported** when the port now behaves as upstream does, including when it already did
("already covered by ..."). It is **not applicable** when the change is only:

- Electron/CEF, the installer, the updater, or the desktop's SQLite schema;
- build, CI, lint, types, or upstream's own tests;
- a refactor with no behaviour change (say "no behaviour change");
- a revert reapplied later, or a feature the port dropped.

Give the reason in a few words. When it is a judgement call, ask the owner.

## 4. Port, with tests

- Port the behaviour, not the code shape. Follow `AGENTS.md` (VRChat budget, query bounds,
  topics, i18n).
- Every ported behaviour change gets a test in the part that owns it. If the commit fixes an
  upstream issue or PR (`(#1234)` in the subject, `Fixes #n` in the body), tag that test
  (skill `upstream-regression-test`).
- Where the browser cannot do what upstream did (flash a window, read the desktop), do the closest
  thing and say so in the UI text and the commit.
- Run the checks of every part you touched (`AGENTS.md`, Commands).

## 5. Commit message

One commit for the port, after the merge. Use `feat:` (or `fix:`) without a scope when it spans
parts. Format, as in `1cd0aaaa` and `23726d75`:

```
feat: port upstream <short summary>

Brings the changes from vrcx-team/VRCX <from>..<to> into the client and server.

Ported:
- <hash> <upstream subject>: <what the port now does, and how it differs>.
- <hash> <subject>: already covered by <where>.

Not applicable to the port:
- <hash> <subject>: <why, in a few words>.
- <hash>, <hash>: build and CI.
```

Every non-merge commit in `<from>..<to>` appears exactly once. Check with
`git log --format=%h --no-merges <from>..<to>` against the message.

## Auditing an earlier range

To check commits an earlier port claimed wholesale (as `cbd077de` did for the three months before
the baseline `2929696b`): list them with `git log --oneline --no-merges <from>..<to>`, keep the
fix-like ones, and compare each with the port. Missing behaviour is ported with tests in a
`fix: port the upstream fixes ...` commit with the same `Ported:` / `Not applicable to the port:`
lists, plus a count of how many already matched. The `upstream-auditor` subagent does the
read-only part.
