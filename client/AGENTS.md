# Web client: rules for coding agents

The root `AGENTS.md` applies here too. `README.md` describes the stack and layout.

## Commands

```sh
pnpm install --frozen-lockfile
pnpm run check        # buf generate + paraglide compile, then svelte-check; must report 0 errors
pnpm test             # vitest over src/**/*.test.ts (run check or generate first on a fresh checkout)
pnpm run test:e2e     # Playwright; builds, starts server/Testing.E2EHost (Docker) and vite preview
pnpm exec playwright install chromium   # once, before the first e2e run
pnpm run dev          # proxies /1/... to VRCX_SERVER_URL, default http://localhost:5280
```

There is no linter or formatter configured. Match the indentation and style of the file you edit.

## Rules

- Svelte 5 runes everywhere (`$state`, `$derived`, `$effect`, `$props`); runes mode is forced in
  `vite.config.ts`. No legacy `export let` or `$:`.
- Put logic in plain modules under `src/lib/<area>/` (`.ts`, or `.svelte.ts` when it holds runes
  state) and keep components to rendering. Test those modules in `src/lib/__tests__/<name>.test.ts`.
- Import library code as `#lib/...` (the `imports` map in `package.json`).
- REST goes through `src/lib/api/client.ts` (`api.get`, ...), from `onMount` or `$effect`, never at
  module top level: every route is prerendered.
- Live data comes from `src/lib/api/live.svelte.ts` today. Do not add new "event, then refetch"
  pairs; live data is moving to server topics (root `AGENTS.md`).
- Target: one store per live topic, `src/lib/live/topics/<topic>.svelte.ts`, a thin `$state`
  shell over a pure reducer module `<topic>.ts` (`applySnapshot`, `applyDelta`) with vitest
  tests. `src/lib/people/sidebar-events.ts` is the existing example of the pure-reducer style.
  Components do not call `#lib/api/client` for a feature that has a store.
- `src/lib/__tests__/architecture.test.ts` is the ratchet: it lists the components and pages that
  still request a store feature's REST paths themselves, and fails on a new one or on a line that
  no longer applies. The list may only shrink; never add to it to make a change pass. The friend
  list is read through `sidebarData` (`ensureFriends()` for a one-off lookup), never `GET friends`.
- Do not add notification or wrist-feed decision logic here. It is moving to the server, with
  its settings moving from `localStorage` into account settings (imported once).
- UI text comes from paraglide messages: `import { m } from '#lib/paraglide/messages.js'`, then
  `m.some_key()`. Add keys to `src/lib/localization/en.json` only (skill `client-i18n-string`).
- Generated, git-ignored, never edited: `src/lib/gen/` (protobuf-es from
  `server/Common/Protos/vrcx_live.proto`) and `src/lib/paraglide/`.
- Behaviour, wording and layout follow the original (`legacy/src`). Say in a comment which part of
  the original a module ports ("the original's `launchGame`") and why the port differs where it
  does, for example "there is no window to flash from a browser tab".
- Upstream regression tests: a `describe`, `test` or `it` title starting `upstream #n`
  (`upstream #1611 #1758 ...` for several).
