---
name: client-i18n-string
description: Add or change UI text in the web client as a paraglide message (m.*) in client/src/lib/localization/en.json, reusing the original VRCX's keys and translations where they exist. Use when a client change shows new text, or when porting upstream localization changes.
---

# Client UI text

The client's UI text is paraglide messages. The locales are listed in
`client/project.inlang/settings.json` (base locale `en`). The files are
`client/src/lib/localization/<locale>.json`, which are flat, tab-indented JSON. They compile to
`client/src/lib/paraglide/` (generated and git-ignored) on `pnpm run generate`, `check`, `dev`,
`build` and in vitest.

## 1. Reuse the original's key when there is one

The original's strings are nested in `legacy/src/localization/en.json`. The client uses the same
key path joined with `_`: `view.settings.notifications.notifications.desktop_notifications.queue_ready_alarm`
becomes `view_settings_notifications_notifications_desktop_notifications_queue_ready_alarm`.

```sh
grep -n '"queue_ready_alarm"' legacy/src/localization/en.json          # find the original's key
grep -n 'queue_ready_alarm' client/src/lib/localization/en.json        # already in the client?
```

When the port behaves differently, change the English text to say what the port does ("Play a
chime in this browser ..." instead of "flash the taskbar"). Keep the key.

## 2. Add the key

- Add it to `client/src/lib/localization/en.json` only, next to the keys of the same area.
- A new string with no original counterpart gets a key in the same style: area prefix, snake_case.
- Parameters are `{name}`: `"... Please use {feature} instead."`, called as
  `m.common_feature_relocated_description({ feature })`.
- Do not edit the other locale files by hand and do not machine-translate. A key missing from a
  locale shows the English text.
- The one exception is porting: when the key comes from the original, copy the original's
  translations from `legacy/src/localization/<locale>.json` into each client locale file that
  lacks it, and drop keys upstream removed from every locale. Do this with a script that keeps the
  files' tab indentation and LF endings, and say so in the commit ("zh-CN gains 211 strings the
  client uses").

## 3. Use it

```svelte
<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
</script>

<span>{m.view_settings_notifications_notifications_desktop_notifications_queue_ready_alarm()}</span>
```

In `.ts` modules the import is the same. Tests import it relatively
(`../paraglide/messages.js`). Never put English literals in components where the original had a
key.

## 4. Check

```sh
cd client
pnpm run check      # fails on an unknown m.* key
pnpm test
python3 -c "import json,sys; [json.load(open(f)) for f in sys.argv[1:]]" src/lib/localization/*.json
```
