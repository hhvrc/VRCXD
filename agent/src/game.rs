//! Starting and stopping VRChat: the original's `GameHandler.StartGame`, `StartGameFromPath`,
//! `QuitGame` and `VRCIPC` (handing an instance to the running game), as the Qt app ported them.

use std::path::{Path, PathBuf};
use std::sync::LazyLock;
use std::time::Duration;

use regex::Regex;
use tokio::sync::watch;
use tracing::{info, warn};

use crate::actions::Answer;
use crate::config::Config;
use crate::platform;
use crate::proto::{ActionStatus, LaunchVrchat};

pub const VRCHAT_STEAM_APP_ID: &str = "438100";
pub const GAME_PROCESS: &str = "VRChat.exe";

/// A `vrchat://launch` link and nothing else. The server checks this too, but the agent is what
/// runs the process, so it does not take the server's word for it.
pub fn is_valid_launch_url(url: &str) -> bool {
    static PATTERN: LazyLock<Regex> = LazyLock::new(|| {
        Regex::new(r"^vrchat://launch\?ref=vrcx\.app&id=[A-Za-z0-9_:~().\-]+(&shortName=[A-Za-z0-9_\-]+)?$").unwrap()
    });
    PATTERN.is_match(url)
}

/// Splits launch arguments the way Qt's `QProcess::splitCommand` does: whitespace separates,
/// double quotes group, and three quotes in a row are one literal quote.
pub fn split_command(command: &str) -> Vec<String> {
    let mut args = Vec::new();
    let mut current = String::new();
    let mut quote_count = 0;
    let mut in_quote = false;
    for c in command.chars() {
        if c == '"' {
            quote_count += 1;
            if quote_count == 3 {
                quote_count = 0;
                current.push(c);
            }
            continue;
        }
        if quote_count > 0 {
            if quote_count == 1 {
                in_quote = !in_quote;
            }
            quote_count = 0;
        }
        if !in_quote && c.is_whitespace() {
            if !current.is_empty() {
                args.push(std::mem::take(&mut current));
            }
        } else {
            current.push(c);
        }
    }
    if !current.is_empty() {
        args.push(current);
    }
    args
}

/// launch.exe's arguments, in the original's order. `profile` is set for a relaunch after a crash:
/// the VRChat profile the crashed game ran in (`Some(None)`: it had none, so profile 0), which
/// replaces any `--profile` in the PC's launch arguments (upstream #645).
pub fn game_arguments(
    url: &str,
    launch_arguments: &str,
    desktop_mode: bool,
    profile: Option<Option<u32>>,
) -> Vec<String> {
    let mut args = vec![url.to_owned()];
    let configured = split_command(launch_arguments);
    match profile {
        None => args.extend(configured),
        Some(profile) => {
            args.extend(configured.into_iter().filter(|a| !is_profile_argument(a)));
            args.extend(profile.map(|p| format!("--profile={p}")));
        }
    }
    if desktop_mode {
        args.push("--no-vr".into());
    }
    args
}

fn is_profile_argument(arg: &str) -> bool {
    arg == "--profile" || arg.starts_with("--profile=")
}

/// The VRChat profile in a game process's arguments (`--profile=N`, VRChat's default being 0).
/// Only a number is taken, so nothing else of the command line is ever passed on.
pub fn profile_in(args: &[String]) -> Option<u32> {
    args.iter().rev().find_map(|a| a.strip_prefix("--profile=")).and_then(|n| n.parse().ok())
}

/// What the agent knows of the profiles the game ran in: read from the process when it is first
/// seen, and handed on once it exits, so a second VRChat (`--allowMultiple`) seen afterwards does
/// not replace the crashed one's. The game's command line, not its log: worlds can write into
/// the log, only whoever starts the process sets its arguments (upstream PR #1953).
#[derive(Debug, Default, Clone, Copy, PartialEq, Eq)]
pub struct Profiles {
    /// The running game's process and its profile (`None` when its arguments could not be read).
    running: Option<(u32, Option<Option<u32>>)>,
    /// The profile of the game that exited last; `None` when not known.
    exited: Option<Option<u32>>,
}

impl Profiles {
    /// The game process now watched (`None`: no game runs), and a way to read a process's arguments.
    pub fn observe(&mut self, pid: Option<u32>, arguments: impl FnOnce(u32) -> Option<Vec<String>>) {
        if self.running.map(|(p, _)| p) == pid {
            return;
        }
        if let Some((_, profile)) = self.running.take() {
            self.exited = profile;
        }
        self.running = pid.map(|pid| (pid, arguments(pid).map(|args| profile_in(&args))));
    }

    /// The profile a relaunch after a crash uses.
    pub fn after_crash(&self) -> Option<Option<u32>> {
        self.exited
    }
}

static PROFILES: std::sync::Mutex<Profiles> = std::sync::Mutex::new(Profiles { running: None, exited: None });

fn profiles() -> std::sync::MutexGuard<'static, Profiles> {
    PROFILES.lock().unwrap_or_else(std::sync::PoisonError::into_inner)
}

/// The launch.exe a custom path means (the folder or the exe itself), if it exists.
pub fn launch_exe_from_path(path: &str) -> Option<PathBuf> {
    let path = Path::new(path);
    let exe = if path.extension().is_some_and(|e| e.eq_ignore_ascii_case("exe")) {
        path.to_owned()
    } else {
        path.join("launch.exe")
    };
    let named_launch = exe.file_name().and_then(|n| n.to_str()).is_some_and(|n| n.eq_ignore_ascii_case("launch.exe"));
    (named_launch && exe.is_file()).then(|| std::path::absolute(&exe).unwrap_or(exe))
}

#[derive(Debug, PartialEq, Eq)]
pub enum StartResult {
    Started,
    /// Neither Steam nor a VRChat install is registered.
    NotFound,
    /// The custom path is not a launch.exe that exists.
    InvalidOverride,
    /// The process would not start.
    Failed,
}

fn start_process(program: &Path, args: &[String]) -> bool {
    info!("starting {} {args:?}", program.display());
    let mut command = std::process::Command::new(program);
    command.args(args);
    if let Some(dir) = program.parent() {
        command.current_dir(dir);
    }
    platform::detach(&mut command);
    match command.spawn() {
        Ok(_) => true,
        Err(e) => {
            warn!("failed to start {}: {e}", program.display());
            false
        }
    }
}

pub fn start(url: &str, config: &Config, desktop_mode: bool, profile: Option<Option<u32>>) -> StartResult {
    let args = game_arguments(url, &config.launch_arguments, desktop_mode, profile);
    if !config.vrc_launch_path_override.trim().is_empty() {
        return match launch_exe_from_path(config.vrc_launch_path_override.trim()) {
            None => StartResult::InvalidOverride,
            Some(exe) if start_process(&exe, &args) => StartResult::Started,
            Some(_) => StartResult::Failed,
        };
    }

    // Steam first: "C:\Program Files (x86)\Steam\steam.exe" -- "%1"
    static STEAM: LazyLock<Regex> = LazyLock::new(|| Regex::new(r#"(?i)^"(.+?\\steam\.exe)""#).unwrap());
    if let Some(steam) =
        platform::url_handler_command("steam").and_then(|c| STEAM.captures(&c).map(|m| PathBuf::from(&m[1])))
        && steam.is_file()
    {
        let mut steam_args = vec!["-applaunch".to_owned(), VRCHAT_STEAM_APP_ID.to_owned()];
        steam_args.extend(args.iter().cloned());
        if start_process(&steam, &steam_args) {
            return StartResult::Started;
        }
        warn!("failed to start VRChat from Steam");
    }

    // "C:\Program Files (x86)\Steam\steamapps\common\VRChat\launch.exe" "%1" %*
    static LAUNCH: LazyLock<Regex> = LazyLock::new(|| Regex::new(r#"(?i)^"?(.+?\\launch\.exe)"?"#).unwrap());
    if let Some(exe) = platform::url_handler_command("VRChat")
        .and_then(|c| LAUNCH.captures(&c).map(|m| m[1].to_owned()))
        .and_then(|p| launch_exe_from_path(&p))
    {
        return if start_process(&exe, &args) { StartResult::Started } else { StartResult::Failed };
    }
    StartResult::NotFound
}

/// How many VRChat processes run on this PC.
pub fn running_game_processes() -> usize {
    platform::processes_named(GAME_PROCESS).len()
}

/// The original's quit fix: ends VRChat only when exactly one copy runs, rather than guess which
/// one is hung.
pub fn quit() {
    let pids = platform::processes_named(GAME_PROCESS);
    info!("quit-fix: found {} VRChat process(es)", pids.len());
    if let [pid] = pids[..]
        && let Err(e) = platform::terminate(pid)
    {
        warn!("quit-fix: couldn't end VRChat: {e}");
    }
}

/// Hands the link to the running game over its URL pipe; whether it accepted.
pub async fn open_in_running_game(url: &str) -> bool {
    open_in_running_game_on(url, platform::GAME_URL_PIPE).await
}

pub async fn open_in_running_game_on(url: &str, pipe: &str) -> bool {
    match tokio::time::timeout(Duration::from_secs(5), platform::pipe_request(pipe, url.as_bytes())).await {
        Ok(Ok(answer)) => answer == 1,
        Ok(Err(e)) => {
            warn!("game pipe failed: {e}");
            false
        }
        Err(_) => {
            warn!("game pipe did not answer");
            false
        }
    }
}

/// The open_instance action.
pub async fn open_instance(url: &str) -> Answer {
    if !is_valid_launch_url(url) {
        warn!("refusing a launch link that is not one: {url}");
        Answer::failed("Not a VRChat launch link")
    } else if open_in_running_game(url).await {
        Answer::ok()
    } else {
        Answer::failed("VRChat did not accept the instance")
    }
}

/// The launch_vrchat action.
pub async fn launch(launch: &LaunchVrchat, config: &Config) -> Answer {
    if !is_valid_launch_url(&launch.launch_url) {
        warn!("refusing a launch link that is not one: {}", launch.launch_url);
        return Answer::failed("Not a VRChat launch link");
    }
    if !launch.allow_second_instance && running_game_processes() > 0 {
        return Answer::status(ActionStatus::GameRunning, "");
    }
    let (url, config, desktop) = (launch.launch_url.clone(), config.clone(), launch.desktop_mode);
    let profile = if launch.after_crash { profiles().after_crash() } else { None };
    match tokio::task::spawn_blocking(move || start(&url, &config, desktop, profile)).await {
        Ok(StartResult::Started) => Answer::ok(),
        Ok(StartResult::NotFound) => {
            Answer::failed("VRChat was not found; set its folder with `vrcxd-agent setup --vrchat-path <folder>`")
        }
        Ok(StartResult::InvalidOverride) => {
            Answer::failed("The VRChat folder in the agent's settings has no launch.exe")
        }
        Ok(StartResult::Failed) | Err(_) => Answer::failed("VRChat would not start"),
    }
}

/// Whether VRChat and SteamVR run (the original's ProcessMonitor, which watched vrchat and vrserver).
#[derive(Debug, Default, Clone, Copy, PartialEq, Eq)]
pub struct GameStatus {
    pub running: bool,
    pub steamvr_running: bool,
    /// VRChat ran at some point since the agent started, so a stop means it closed.
    pub ran: bool,
    /// Nobody wears the headset, while OpenVR is on (set by the vr module).
    pub hmd_afk: bool,
}

#[cfg(windows)]
const STEAMVR_PROCESS: &str = "vrserver.exe";
#[cfg(not(windows))]
const STEAMVR_PROCESS: &str = "vrserver";

/// While VRChat runs, waits on its process (waking every 10 seconds to look at SteamVR); while it
/// does not, looks every 5 seconds.
pub async fn watch(tx: std::sync::Arc<watch::Sender<GameStatus>>) {
    loop {
        let (game, steamvr) = tokio::task::spawn_blocking(|| {
            let game = platform::processes_named(GAME_PROCESS);
            // Before the state goes out: a crash relaunch follows it.
            profiles().observe(game.first().copied(), platform::process_arguments);
            (game, !platform::processes_named(STEAMVR_PROCESS).is_empty())
        })
        .await
        .unwrap_or_default();
        tx.send_if_modified(|status| {
            let next = GameStatus {
                running: !game.is_empty(),
                steamvr_running: steamvr,
                ran: status.ran || !game.is_empty(),
                hmd_afk: status.hmd_afk && steamvr,
            };
            std::mem::replace(status, next) != next
        });
        match game.first() {
            Some(&pid) => {
                let _ =
                    tokio::task::spawn_blocking(move || platform::wait_for_exit(pid, Duration::from_secs(10))).await;
            }
            None => tokio::time::sleep(Duration::from_secs(5)).await,
        }
    }
}

#[cfg(test)]
mod tests {
    //! The Qt app's `test_game_launcher.cpp`.
    use super::*;

    #[test]
    fn only_vrchat_launch_links_are_valid() {
        for (url, valid) in [
            ("vrchat://launch?ref=vrcx.app&id=wrld_1:123~private(usr_1)~region(eu)", true),
            ("vrchat://launch?ref=vrcx.app&id=wrld_1:1&shortName=abc_D-1", true),
            ("vrchat://launch?ref=vrcx.app&id=wrld_1:1&shortName=a b", false),
            ("vrchat://launch?ref=vrcx.app&id=wrld 1", false),
            ("vrchat://launch?id=wrld_1:1", false),
            ("https://vrchat.com/home/launch?worldId=wrld_1", false),
            ("vrchat://launch?ref=vrcx.app&id=wrld_1:1\" --evil", false),
        ] {
            assert_eq!(is_valid_launch_url(url), valid, "{url}");
        }
    }

    #[test]
    fn arguments_follow_the_original_order() {
        let url = "vrchat://launch?ref=vrcx.app&id=a";
        assert_eq!(game_arguments(url, "", false, None), vec![url]);
        assert_eq!(game_arguments(url, "--fps=144 \"--a b\"", true, None), vec![url, "--fps=144", "--a b", "--no-vr"]);
    }

    #[test]
    fn upstream_645_a_relaunch_after_a_crash_keeps_the_crashed_games_profile() {
        let url = "vrchat://launch?ref=vrcx.app&id=a";
        // The crashed game ran in profile 2; the PC's own arguments name profile 1.
        assert_eq!(
            game_arguments(url, "--fps=144 --profile=1", true, Some(Some(2))),
            vec![url, "--fps=144", "--profile=2", "--no-vr"]
        );
        // It ran with none (profile 0): none is added, and the PC's is left out.
        assert_eq!(game_arguments(url, "--profile=1 --fps=144", false, Some(None)), vec![url, "--fps=144"]);
        // Not known: the PC's arguments as they are.
        assert_eq!(game_arguments(url, "--profile=1", false, None), vec![url, "--profile=1"]);
    }

    #[test]
    fn upstream_645_the_profile_is_only_a_number_from_the_process_arguments() {
        let args = |a: &[&str]| a.iter().map(|s| (*s).to_owned()).collect::<Vec<_>>();
        assert_eq!(profile_in(&args(&["VRChat.exe", "vrchat://launch?id=x", "--profile=3", "--no-vr"])), Some(3));
        assert_eq!(profile_in(&args(&["VRChat.exe", "--no-vr"])), None);
        assert_eq!(profile_in(&args(&["VRChat.exe", "--profile=1 --evil"])), None);
        assert_eq!(profile_in(&args(&["VRChat.exe", "--profile=-1"])), None);
    }

    #[test]
    fn upstream_645_the_crashed_games_profile_is_kept_until_another_game_exits() {
        let read = |profile: &'static str| move |_| Some(vec!["VRChat.exe".to_owned(), profile.to_owned()]);
        let mut profiles = Profiles::default();
        assert_eq!(profiles.after_crash(), None);
        profiles.observe(Some(10), read("--profile=2"));
        // Still running: nothing to relaunch with yet.
        assert_eq!(profiles.after_crash(), None);
        profiles.observe(Some(10), |_| panic!("read once per process"));
        profiles.observe(None, |_| unreachable!());
        assert_eq!(profiles.after_crash(), Some(Some(2)));

        // A second VRChat starting does not replace it; its own exit does.
        profiles.observe(Some(11), read("--fps=90"));
        assert_eq!(profiles.after_crash(), Some(Some(2)));
        profiles.observe(None, |_| unreachable!());
        assert_eq!(profiles.after_crash(), Some(None));

        // Arguments that could not be read: not known.
        profiles.observe(Some(12), |_| None);
        profiles.observe(None, |_| unreachable!());
        assert_eq!(profiles.after_crash(), None);
    }

    #[test]
    fn this_process_arguments_can_be_read() {
        let args = platform::process_arguments(std::process::id()).expect("readable");
        assert!(!args.is_empty());
    }

    #[test]
    fn split_command_matches_qt() {
        assert_eq!(split_command("  a   b  "), vec!["a", "b"]);
        assert_eq!(split_command(r#"say """hi""" "x y""#), vec!["say", "\"hi\"", "x y"]);
        assert_eq!(split_command(r#"a "" b"#), vec!["a", "b"]);
    }

    #[test]
    fn a_custom_path_must_be_a_launch_exe() {
        let dir = tempfile::tempdir().unwrap();
        let folder = dir.path().to_string_lossy().into_owned();
        assert!(launch_exe_from_path(&folder).is_none());
        let exe = dir.path().join("launch.exe");
        let other = dir.path().join("other.exe");
        std::fs::write(&exe, b"").unwrap();
        std::fs::write(&other, b"").unwrap();
        let expected = std::path::absolute(&exe).unwrap();
        assert_eq!(launch_exe_from_path(&folder), Some(expected.clone()));
        assert_eq!(launch_exe_from_path(&exe.to_string_lossy()), Some(expected));
        assert!(launch_exe_from_path(&other.to_string_lossy()).is_none());
    }

    #[cfg(windows)]
    #[tokio::test]
    async fn the_game_pipe_gets_the_link_and_its_answer_counts() {
        use tokio::io::{AsyncReadExt, AsyncWriteExt};
        use tokio::net::windows::named_pipe::ServerOptions;
        for accepted in [true, false] {
            let name = format!(r"\\.\pipe\vrcxd-agent-test-{}-{accepted}", std::process::id());
            let mut server = ServerOptions::new().first_pipe_instance(true).create(&name).unwrap();
            let game = tokio::spawn(async move {
                server.connect().await.unwrap();
                let mut buf = [0u8; 64];
                let n = server.read(&mut buf).await.unwrap();
                server.write_all(&[u8::from(accepted)]).await.unwrap();
                buf[..n].to_vec()
            });
            assert_eq!(open_in_running_game_on("vrchat://launch?ref=vrcx.app&id=a", &name).await, accepted);
            assert_eq!(game.await.unwrap(), b"vrchat://launch?ref=vrcx.app&id=a");
        }
        assert!(!open_in_running_game_on("x", r"\\.\pipe\vrcxd-agent-test-nobody").await);
    }
}
