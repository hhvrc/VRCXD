# VRCXD web client (SvelteKit)

The browser UI for the server in `../Server`, and the eventual replacement for the
Electron/CefSharp client in `Dotnet/`/`src/`. Opened from any device on the LAN/VPN; the
gaming PC runs the Qt app in `../desktop` instead, which only ships game logs.

- **Frontend**: SvelteKit, fully prerendered (`export const prerender = true` in
  `src/routes/+layout.ts`) via `@sveltejs/adapter-static`. All dynamic data comes from the
  server at runtime, never from a request-time backend.
- **API**: `src/lib/api/client.ts`, a thin `fetch` wrapper over the server's REST routes (`/1/...`)
  with the API token header, problem-details errors and the response types. Pages call
  `api.get(...)` from `onMount`/`$effect`, never at module top level.
- **Live**: `src/lib/api/live.svelte.ts` keeps one websocket to `/1/ws` (binary protobuf frames,
  `ServerMessage` in) open for every page that asks for it, and applies the `ServerEvent`s to
  a shared store: presence per user, who is in which instance, and the raw event list. The
  TypeScript types come from `../server/Common/Protos/vrcx_live.proto` via `buf.gen.yaml`
  (protobuf-es; `pnpm run generate`, run automatically before dev/build/check).
- **Design system**: Tailwind v4 + `shadcn-svelte`, matching the Vue app's own stack
  (`src/app.css` ports its design tokens, `src/lib/components/ui/` holds the primitives).

## Status

Ported against the server's current endpoints: the token and VRChat login gates, friend list,
friend log, friends map, player list, user page (profile, memo, history, friend actions),
notifications, moderation, the three favorites pages, game log (instance visits), user search,
settings (accounts, app settings, sync, token), the feed (stored history plus live refresh),
the three charts (instance activity, mutual friend network, hot worlds), my avatars, and the
tools page (group lists, calendar, info push, exports, invite messages, reports, purchase history). Tools that are
not here yet say so on their card.

## Developing

```sh
pnpm install
pnpm run dev
```

REST calls and the websocket are proxied to a running server, `http://localhost:5280` by
default; override with `VRCX_SERVER_URL`.

## Building

```sh
pnpm run build
```

Produces the prerendered static site in `build/`, for the server to serve.
