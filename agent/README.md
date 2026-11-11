# VRCXD agent

A small headless program for the gaming PC. It has no window: the web client is its interface.

- Ships VRChat's logs (`output_log_*.txt`) to the server over its websocket
  (`server/Common/Protos/vrcx_live.proto`), resuming where the server's stored log ends.
- Tells the server whether VRChat runs, so the web client can offer to start it.
- Carries out what the web client asks of this PC: start VRChat, hand it an instance, end a hung
  copy, and write screenshot metadata (rename with the world id, the VRCX `Description` chunk,
  copy to the clipboard).
- Runs the original VRCX's named pipe for other programs (the Photon logger, external notifiers,
  tagging tools), as `docs/desktop-ipc.md` describes, and relays what they send to the server.
- Opens `vrcx://` links in the web client's `/launch` page.

It replaces the Qt desktop app's log shipping and actions, and reads the same settings file, so an
install of that app keeps its settings.

## Use

```powershell
vrcxd-agent setup --server http://nas:5280 --token <the server's VRCX__AUTH__TOKEN>
vrcxd-agent install      # start at login, and open vrcx:// links
vrcxd-agent run          # or start it now
vrcxd-agent status       # settings, and what the running agent is doing
```

`setup` changes only the options given; `vrcxd-agent setup --help` lists them (log folder, VRChat
folder and launch arguments, PC name, web client address). A running agent picks up new settings
within 30 seconds. `uninstall` undoes `install`.

| What | Where |
|---|---|
| Settings | `%APPDATA%\VRCX\client-service.json` if it exists, else `%ProgramData%\VRCX\client-service.json`; `$VRCX_CLIENT_CONFIG` overrides |
| Logs, status | `%LOCALAPPDATA%\VRCXD\logs`, `%LOCALAPPDATA%\VRCXD\status.json` |
| Starts at login | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `VRCXD Agent` |
| `vrcx://` links | `HKCU\Software\Classes\vrcx` |

On Linux the settings are in `$XDG_CONFIG_HOME/vrcx/`, the logs in `$XDG_STATE_HOME/vrcxd/`, and
`install` adds a `systemd --user` unit and a `vrcxd-agent.desktop` handler for `vrcx://` links. The
log folder defaults to Steam's Proton prefix for VRChat, and VRChat's registry is the
`[Software\\VRChat\\VRChat]` section of that prefix's `user.reg` (written only while VRChat is
closed, as wineserver writes the file back while it runs). The app launcher starts `.desktop` files
and executables from its folder, and Steam games from `.desktop` or `.url` shortcuts. The VRCX
named pipe and the game's URL pipe live inside Wine, so those two stay Windows only.

`VRCXD_LOG=debug` makes the logs more detailed.

## Footprint

One thread for its own work. While VRChat runs it waits on the game's process instead of
checking for it, and reads the log once a second; otherwise it looks for the game every 5
seconds and reads the log every 10. A ping goes to the server every 30 seconds. While the
SteamVR overlay setting is on and SteamVR runs, a second thread joins SteamVR every 100 ms for the
headset's proximity sensor and the device batteries.

## Build and test

```powershell
cargo build --release    # target/release/vrcxd-agent.exe
cargo test
cargo clippy --all-targets -- -D warnings
```

`build.rs` compiles the proto with `protox`, so no `protoc` is needed, and copies OpenVR's
`openvr_api.dll` (or `libopenvr_api.so`) from `assets/` beside the exe, where the agent loads it;
a release ships it the same way. The tests are the Qt app's
parser, watcher and launcher tests, case for case, plus the pipe's framing and JSON mapping and the
screenshot chunk.

## Files

- `src/log_parser.rs`, `src/log_watcher.rs`: log lines to `LogEntry`s, and tailing the files.
- `src/live.rs`: the server connection, batches and acknowledgements, actions, the game monitor.
- `src/game.rs`, `src/screenshot.rs`: the actions.
- `src/ipc.rs`: the VRCX named pipe.
- `src/vr/`: OpenVR through its C function tables: HMD AFK and the device rows.
- `src/config.rs`, `src/status.rs`, `src/platform.rs`: settings, the status file, and what differs
  between Windows and Linux.
