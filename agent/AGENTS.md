# Agent: rules for coding agents

The root `AGENTS.md` applies here too. `README.md` describes what the agent does, where it keeps
its files and its modules.

## Commands

```sh
cargo fmt --check                                  # rustfmt.toml: edition 2024, width 120
cargo clippy --all-targets --locked -- -D warnings
cargo test --locked
cargo build --release --locked                     # what CI uploads
```

CI runs these on both `windows-latest` and `ubuntu-latest`. Code behind `cfg(windows)` does not
build on Linux; say so in the commit when you could not check it (`cargo check --target
x86_64-pc-windows-gnu` if that target is installed).

## Rules

- The proto is `server/Common/Protos/vrcx_live.proto`, compiled by `build.rs` with `protox`. A
  proto change is a server, client and agent change together.
- The agent is small and headless: one thread for its own work, waiting on the game rather than
  polling where it can (README, Footprint). Do not add busy loops or new threads without need.
- The agent does what needs the PC. Decisions (what to notify, what the wrist feed shows) belong
  on the server; the agent carries them out.
- Tests are `#[cfg(test)] mod tests` blocks at the bottom of the file they test. Ports of the
  original's tests say which file they come from (`//! The Qt app's test_log_parser.cpp`).
- Upstream regression tests are named `upstream_<n>_<what must hold>`
  (`upstream_1611_1758_...` for several), so `server/Testing/upstream-coverage.py` counts them.
- Say in a comment which part of the original a module ports (`Dotnet/IPC/IPCServer.cs`,
  `AutoAppLaunchManager`) and why it differs.
