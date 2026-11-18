# Contributing to VRCXD

VRCXD is a port of [VRCX](https://github.com/vrcx-team/VRCX). Report problems with VRCXD here, not
to VRCX. Changes that belong in the original (its design, wording, features) are better made
upstream, from where they are ported.

## Before you start

- Open an issue first for anything larger than a fix, so the approach can be agreed.
- Features, wording and layout follow the original. A change that departs from it needs a reason
  the port has (a browser instead of a desktop window, a server instead of a PC).
- Keep the original's performance baseline: users with 1,000 to 4,000 friends, and years of
  history. Server flows over lists (friends, groups, players) are bounded in SQL round trips and
  in VRChat requests, and tests pin both.

## Making a change

- `AGENTS.md` has the commands each part's CI runs, the testing rules, the architecture rules and
  the commit message format. It is written for AI coding agents, and it applies to people as well.
- Every feature and fix comes with tests. Every upstream VRCX bug that applies to the port gets
  a regression test (`server/Testing/README.md`, "Turning an upstream issue or PR into a test").
- Development setup: `server/README.md`, `client/README.md`, `agent/README.md`.
- `legacy/` is the original's code, kept for reference. Do not change it.

## Commits

Conventional commits (`feat(server): ...`, `fix(client): ...`), with a body that says why.
