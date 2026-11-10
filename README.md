# VRCXD

VRCXD is a self-hosted companion for VRChat, built from [VRCX](https://github.com/vrcx-team/VRCX).
It keeps track of your friends, their status and locations, your game log and your feed, and
shows it all in a web UI you can open from any device on your network.

**Why VRCXD: the D is for daemon.** The server runs on its own machine (a NAS, a home server) and
keeps tracking while your PC and the game are off. The web UI and the desktop app connect to it
when they are running, and nothing is missed in between.

## Based on VRCX

VRCXD is a port of [VRCX](https://github.com/vrcx-team/VRCX) by pypy, Natsumi and the VRCX
contributors. Its features, settings, wording and layout come from VRCX, and most of the work
here is carrying them over from a desktop app to a server and a browser. All credit for the
original design goes to the VRCX team. VRCXD is not made or supported by them: report problems
with VRCXD here, not to VRCX.

If you want the desktop app that runs on your PC, use VRCX itself. VRCXD is for running the
tracking all the time on a separate machine. A database from VRCX can be imported once
(`Server/LegacyImport/README.md`).

## The parts

| Part | Where | What it does |
| --- | --- | --- |
| Server | `Server/` | ASP.NET Core with Postgres and Redis. Holds the VRChat sessions, listens to VRChat's pipeline websocket, syncs friends, favorites, groups and notifications on a schedule, stores the history and serves the REST API and a live websocket. |
| Web client | `client/` | SvelteKit, built to a static site and served by Caddy next to the API. The UI: friends, feed, friend log, game log, notifications, favorites, charts, tools and settings. |
| Desktop app | `desktop/` | A small Qt tray app for the gaming PC. Reads VRChat's log and ships it to the server, runs the actions that need the PC (launching VRChat, joining an instance, writing screenshot metadata) and shows a status overlay in VR. |

`src/` and `Dotnet/` still hold the original VRCX code that is being ported, and `legacy/` holds
the parts that are done.

## Getting started

- Run the server stack with Docker Compose: `Server/README.md`, section Deploy. It starts
  Postgres, Redis, the API, the web client and a nightly database backup.
- Open the web address, enter the API token and log in to VRChat.
- On the gaming PC, point the desktop app at the same address and token: `desktop/README.md`.

For development: `Server/README.md` (build, tests, configuration), `client/README.md` and
`Server/Testing/README.md` (the integration test harness).

## Is this against VRChat's TOS?

Like VRCX, VRCXD uses the VRChat API and does not modify the game. It is not a mod or a cheat.
For VRChat's stance on API use, see the #faq channel in the VRChat Discord.

---

VRCXD is not endorsed by VRChat and does not reflect the views or opinions of VRChat or anyone
officially involved in producing or managing VRChat properties. VRChat and all associated
properties are trademarks or registered trademarks of VRChat Inc. VRChat © VRChat Inc.

Licensed under the MIT License, see `LICENSE`.
