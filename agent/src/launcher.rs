//! The app launcher (the original's `AutoAppLaunchManager`): when VRChat starts, the shortcuts in
//! `<agent data>/startup` start too, with those in `startup/vr` or `startup/desktop` by whether
//! SteamVR runs, and Steam `.url` shortcuts as `steam://launch/<appid>`. What it started is
//! followed down its process tree and, with "close on exit", closed when VRChat closes.

use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use tokio::sync::watch;
use tracing::{info, warn};

use crate::actions::folders;
use crate::game::GameStatus;
use crate::settings::Settings;

/// ShellLinkHeader's first bytes.
const LNK_SIGNATURE: [u8; 4] = [0x4C, 0x00, 0x00, 0x00];
/// An Internet shortcut's first line.
const URL_HEADER: &[u8] = b"[{000214A0-0000-0000-C000-000000000046}]";

/// The startup folder and its two platform folders, made if missing.
pub fn folders() -> (PathBuf, PathBuf, PathBuf) {
    let root = folders::shortcuts();
    let (desktop, vr) = (root.join("desktop"), root.join("vr"));
    for dir in [&root, &desktop, &vr] {
        let _ = std::fs::create_dir_all(dir);
    }
    (root, desktop, vr)
}

fn starts_with(path: &Path, header: &[u8]) -> bool {
    use std::io::Read;
    let mut head = vec![0u8; header.len()];
    std::fs::File::open(path).and_then(|mut f| f.read_exact(&mut head)).is_ok() && head == header
}

/// FindShortcutFiles: the .lnk-style shortcuts, and the app ids of Steam shortcuts.
pub fn shortcuts_in(dir: &Path) -> (Vec<PathBuf>, Vec<String>) {
    let (mut files, mut steam) = (Vec::new(), Vec::new());
    let Ok(entries) = std::fs::read_dir(dir) else { return (files, steam) };
    let mut paths: Vec<_> = entries.filter_map(Result::ok).map(|e| e.path()).filter(|p| p.is_file()).collect();
    paths.sort();
    for path in paths {
        if starts_with(&path, &LNK_SIGNATURE) {
            files.push(path);
        } else if starts_with(&path, URL_HEADER) {
            let text = std::fs::read_to_string(&path).unwrap_or_default();
            if let Some(id) = text.lines().find_map(|l| l.strip_prefix("URL=steam://rungameid/")) {
                steam.push(id.trim().to_string());
            }
        }
    }
    (files, steam)
}

/// The quoted values on a VDF/ACF line: `"path"  "D:\\Steam"` gives ["path", "D:\\Steam"].
fn quoted(line: &str) -> Vec<&str> {
    line.split('"').skip(1).step_by(2).collect()
}

/// GetPathWithAppId: `<library>/steamapps/common/<installdir>` of an installed Steam app.
pub fn steam_app_dir(steam: &Path, app_id: &str) -> Option<PathBuf> {
    let vdf = std::fs::read_to_string(steam.join("config").join("libraryfolders.vdf")).ok()?;
    let libraries = vdf.lines().filter_map(|line| match quoted(line).as_slice() {
        ["path", path] => Some(PathBuf::from(path.replace("\\\\", "\\")).join("steamapps")),
        _ => None,
    });
    for library in libraries.filter(|l| l.is_dir()) {
        let Ok(acf) = std::fs::read_to_string(library.join(format!("appmanifest_{app_id}.acf"))) else { continue };
        let value = |key: &str| {
            acf.lines()
                .find_map(|l| matches!(quoted(l).as_slice(), [k, _] if *k == key).then(|| quoted(l)[1].to_string()))
        };
        if value("appid").as_deref() != Some(app_id) {
            continue;
        }
        let dir = library.join("common").join(value("installdir")?);
        if dir.is_dir() {
            return Some(dir);
        }
    }
    None
}

/// What the launcher started: by shortcut path, the processes of its tree still running.
#[derive(Default)]
struct Started(HashMap<String, HashSet<u32>>);

impl Started {
    /// UpdateChildProcesses: children join their parent's set, ended processes leave it.
    fn update(&mut self) {
        let table = proc::table();
        self.0.retain(|_, pids| {
            let recursive = pids.len() == 1;
            for pid in pids.clone() {
                pids.extend(proc::children(&table, pid, recursive));
                if !table.iter().any(|p| p.pid == pid) {
                    pids.remove(&pid);
                }
            }
            !pids.is_empty()
        });
    }

    /// KillChildProcesses.
    fn kill_all(&mut self) {
        self.update();
        let table = proc::table();
        for pid in self.0.values().flatten() {
            proc::kill_tree(&table, *pid);
        }
        self.0.clear();
    }
}

/// OnProcessStarted: the shortcuts not running already, then the Steam games.
fn launch(started: &Arc<Mutex<Started>>, settings: &Settings, steamvr: bool) -> bool {
    let (root, desktop, vr) = folders();
    let (mut files, mut steam) = shortcuts_in(&root);
    let (more_files, more_steam) = shortcuts_in(if steamvr { &vr } else { &desktop });
    files.extend(more_files);
    steam.extend(more_steam);
    {
        let mut started = started.lock().unwrap_or_else(|e| e.into_inner());
        if settings.app_launcher_kill_on_exit {
            started.kill_all();
        } else {
            started.update();
        }
        for file in &files {
            let key = file.display().to_string();
            let stem = file.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
            if settings.app_launcher_run_once && proc::running_named(&stem) {
                continue;
            }
            if started.0.contains_key(&key) {
                continue;
            }
            match proc::start(file) {
                Some(pid) => {
                    info!("app launcher started {key}");
                    started.0.insert(key, HashSet::from([pid]));
                }
                None => warn!("app launcher could not start {key}"),
            }
        }
    }
    for id in steam.iter().cloned() {
        let started = started.clone();
        tokio::spawn(start_steam_game(id, started));
    }
    !(files.is_empty() && steam.is_empty())
}

/// StartSteamGame: steam://launch, then up to 10 seconds to find its process under the game's folder.
async fn start_steam_game(app_id: String, started: Arc<Mutex<Started>>) {
    if let Err(e) = crate::platform::open_url(&format!("steam://launch/{app_id}")) {
        warn!("app launcher could not start Steam game {app_id}: {e}");
    }
    let Some(dir) = crate::platform::steam_install_dir().and_then(|s| steam_app_dir(&s, &app_id)) else {
        warn!("app launcher: no install folder for Steam game {app_id}");
        return;
    };
    for _ in 0..10 {
        let found = tokio::task::spawn_blocking({
            let dir = dir.clone();
            move || proc::running_under(&dir)
        })
        .await
        .ok()
        .flatten();
        if let Some((pid, path)) = found {
            info!("app launcher found Steam game {app_id}: {path} ({pid})");
            started.lock().unwrap_or_else(|e| e.into_inner()).0.insert(path, HashSet::from([pid]));
            return;
        }
        tokio::time::sleep(Duration::from_secs(1)).await;
    }
    warn!("app launcher did not find Steam game {app_id} running after starting it");
}

/// Follows VRChat: starts the shortcuts when it starts (not when it already ran before the agent),
/// closes or keeps following them when it stops, and looks at the tree every second for five
/// seconds after a launch, then every minute.
pub async fn run(mut game: watch::Receiver<GameStatus>) {
    let started = Arc::new(Mutex::new(Started::default()));
    // The game watcher's first look comes at once; a VRChat running by then started before the
    // agent, which the original leaves alone.
    tokio::time::sleep(Duration::from_secs(3)).await;
    let mut running = game.borrow_and_update().running;
    let mut fast_ticks = 0u32;
    loop {
        let wait = if fast_ticks > 0 { Duration::from_secs(1) } else { Duration::from_secs(60) };
        tokio::select! {
            changed = game.changed() => {
                if changed.is_err() {
                    return;
                }
                let status = *game.borrow_and_update();
                if status.running == running {
                    continue;
                }
                running = status.running;
                let settings = Settings::load();
                let started = started.clone();
                let launched = tokio::task::spawn_blocking(move || {
                    if status.running {
                        settings.app_launcher_enabled && launch(&started, &settings, status.steamvr_running)
                    } else {
                        let mut started = started.lock().unwrap_or_else(|e| e.into_inner());
                        if settings.app_launcher_kill_on_exit { started.kill_all() } else { started.update() }
                        false
                    }
                })
                .await
                .unwrap_or(false);
                if launched {
                    fast_ticks = 5;
                }
            }
            () = tokio::time::sleep(wait) => {
                fast_ticks = fast_ticks.saturating_sub(1);
                let started = started.clone();
                let _ = tokio::task::spawn_blocking(move || started.lock().unwrap_or_else(|e| e.into_inner()).update()).await;
            }
        }
    }
}

#[cfg(windows)]
mod proc {
    use std::path::Path;

    use std::os::windows::ffi::OsStrExt;
    use windows_sys::Win32::Foundation::{CloseHandle, INVALID_HANDLE_VALUE};
    use windows_sys::Win32::System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, PROCESSENTRY32W, Process32FirstW, Process32NextW, TH32CS_SNAPPROCESS,
    };
    use windows_sys::Win32::System::Threading::{
        GetProcessId, OpenProcess, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION, QueryFullProcessImageNameW,
    };
    use windows_sys::Win32::UI::Shell::{SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW, ShellExecuteExW};
    use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;

    pub struct Entry {
        pub pid: u32,
        pub parent: u32,
        pub name: String,
    }

    pub fn table() -> Vec<Entry> {
        let mut out = Vec::new();
        // SAFETY: the snapshot handle is closed; entry is sized as the API asks.
        unsafe {
            let snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if snap == INVALID_HANDLE_VALUE {
                return out;
            }
            let mut entry: PROCESSENTRY32W = std::mem::zeroed();
            entry.dwSize = size_of::<PROCESSENTRY32W>() as u32;
            let mut ok = Process32FirstW(snap, &mut entry);
            while ok != 0 {
                let len = entry.szExeFile.iter().position(|&c| c == 0).unwrap_or(entry.szExeFile.len());
                out.push(Entry {
                    pid: entry.th32ProcessID,
                    parent: entry.th32ParentProcessID,
                    name: String::from_utf16_lossy(&entry.szExeFile[..len]),
                });
                ok = Process32NextW(snap, &mut entry);
            }
            CloseHandle(snap);
        }
        out
    }

    /// FindChildProcesses.
    pub fn children(table: &[Entry], pid: u32, recursive: bool) -> Vec<u32> {
        let mut found = Vec::new();
        for e in table.iter().filter(|e| e.parent == pid && e.pid != pid) {
            found.push(e.pid);
            if recursive {
                found.extend(children(table, e.pid, true));
            }
        }
        found
    }

    /// KillProcessTree: the children, then the process.
    pub fn kill_tree(table: &[Entry], pid: u32) {
        for child in children(table, pid, true).into_iter().chain([pid]) {
            let _ = crate::platform::terminate(child);
        }
    }

    /// IsProcessRunning: any process named like the shortcut.
    pub fn running_named(stem: &str) -> bool {
        let exe = format!("{stem}.exe");
        table().iter().any(|e| e.name.eq_ignore_ascii_case(&exe) || e.name.eq_ignore_ascii_case(stem))
    }

    fn image_path(pid: u32) -> Option<String> {
        // SAFETY: the handle is checked and closed; the buffer's length is passed.
        unsafe {
            let process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
            if process.is_null() {
                return None;
            }
            let mut buf = [0u16; 1024];
            let mut len = buf.len() as u32;
            let ok = QueryFullProcessImageNameW(process, PROCESS_NAME_WIN32, buf.as_mut_ptr(), &mut len);
            CloseHandle(process);
            (ok != 0).then(|| String::from_utf16_lossy(&buf[..len as usize]))
        }
    }

    /// A process whose program is inside `dir`.
    pub fn running_under(dir: &Path) -> Option<(u32, String)> {
        let prefix = dir.display().to_string().to_lowercase();
        table()
            .into_iter()
            .find_map(|e| image_path(e.pid).filter(|p| p.to_lowercase().starts_with(&prefix)).map(|p| (e.pid, p)))
    }

    /// Process.Start with UseShellExecute: the started process's id, when the shell gives one.
    pub fn start(path: &Path) -> Option<u32> {
        let file: Vec<u16> = path.as_os_str().encode_wide().chain(Some(0)).collect();
        let verb: Vec<u16> = "open".encode_utf16().chain(Some(0)).collect();
        // SAFETY: the strings are NUL-terminated and outlive the call; the process handle is closed.
        unsafe {
            let mut info: SHELLEXECUTEINFOW = std::mem::zeroed();
            info.cbSize = size_of::<SHELLEXECUTEINFOW>() as u32;
            info.fMask = SEE_MASK_NOCLOSEPROCESS;
            info.lpVerb = verb.as_ptr();
            info.lpFile = file.as_ptr();
            info.nShow = SW_SHOWNORMAL;
            if ShellExecuteExW(&mut info) == 0 || info.hProcess.is_null() {
                return None;
            }
            let pid = GetProcessId(info.hProcess);
            CloseHandle(info.hProcess);
            (pid != 0).then_some(pid)
        }
    }
}

/// The app launcher is the original's on Windows only.
#[cfg(not(windows))]
mod proc {
    use std::path::Path;

    pub struct Entry {
        pub pid: u32,
    }

    pub fn table() -> Vec<Entry> {
        Vec::new()
    }

    pub fn children(_table: &[Entry], _pid: u32, _recursive: bool) -> Vec<u32> {
        Vec::new()
    }

    pub fn kill_tree(_table: &[Entry], _pid: u32) {}

    pub fn running_named(_stem: &str) -> bool {
        false
    }

    pub fn running_under(_dir: &Path) -> Option<(u32, String)> {
        None
    }

    pub fn start(_path: &Path) -> Option<u32> {
        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shortcuts_are_told_by_their_first_bytes() {
        let dir = tempfile::tempdir().unwrap();
        std::fs::write(dir.path().join("app.lnk"), [0x4C, 0, 0, 0, 1, 2]).unwrap();
        std::fs::write(
            dir.path().join("game.url"),
            "[{000214A0-0000-0000-C000-000000000046}]\r\nProp3=19,0\r\n[InternetShortcut]\r\nURL=steam://rungameid/438100\r\n",
        )
        .unwrap();
        std::fs::write(dir.path().join("notes.txt"), "hello").unwrap();
        let (files, steam) = shortcuts_in(dir.path());
        assert_eq!(files, vec![dir.path().join("app.lnk")]);
        assert_eq!(steam, vec!["438100".to_string()]);
    }

    #[test]
    fn a_steam_apps_folder_is_found_from_the_library_list() {
        let dir = tempfile::tempdir().unwrap();
        let steam = dir.path().join("Steam");
        let library = dir.path().join("Lib");
        std::fs::create_dir_all(steam.join("config")).unwrap();
        std::fs::create_dir_all(library.join("steamapps").join("common").join("VRChat")).unwrap();
        let escaped = library.display().to_string().replace('\\', "\\\\");
        std::fs::write(
            steam.join("config").join("libraryfolders.vdf"),
            format!("\"libraryfolders\"\n{{\n\t\"0\"\n\t{{\n\t\t\"path\"\t\t\"{escaped}\"\n\t}}\n}}\n"),
        )
        .unwrap();
        std::fs::write(
            library.join("steamapps").join("appmanifest_438100.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\t\"438100\"\n\t\"installdir\"\t\t\"VRChat\"\n}\n",
        )
        .unwrap();
        assert_eq!(steam_app_dir(&steam, "438100"), Some(library.join("steamapps").join("common").join("VRChat")));
        assert_eq!(steam_app_dir(&steam, "1"), None);
    }
}
