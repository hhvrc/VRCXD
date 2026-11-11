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

/// FindShortcutFiles: the .lnk-style shortcuts (on Linux also `.desktop` files and executables),
/// and the app ids of Steam shortcuts.
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
        } else {
            // Ok: a program to start; Err: a Steam app id.
            match proc::shortcut(&path) {
                Some(Ok(())) => files.push(path),
                Some(Err(id)) => steam.push(id),
                None => {}
            }
        }
    }
    (files, steam)
}

/// The quoted values on a VDF/ACF line: `"path"  "D:\\Steam"` gives ["path", "D:\\Steam"].
fn quoted(line: &str) -> Vec<&str> {
    line.split('"').skip(1).step_by(2).collect()
}

/// The `steamapps` folders of Steam's libraries, from `config/libraryfolders.vdf`.
pub fn steam_libraries(steam: &Path) -> Vec<PathBuf> {
    let vdf = std::fs::read_to_string(steam.join("config").join("libraryfolders.vdf")).unwrap_or_default();
    vdf.lines()
        .filter_map(|line| match quoted(line).as_slice() {
            ["path", path] => Some(PathBuf::from(path.replace("\\\\", "\\")).join("steamapps")),
            _ => None,
        })
        .filter(|l| l.is_dir())
        .collect()
}

/// GetPathWithAppId: `<library>/steamapps/common/<installdir>` of an installed Steam app.
pub fn steam_app_dir(steam: &Path, app_id: &str) -> Option<PathBuf> {
    for library in steam_libraries(steam) {
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

    pub use super::{Entry, children, kill_tree};

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

    /// Only .lnk and .url shortcuts.
    pub fn shortcut(_path: &Path) -> Option<Result<(), String>> {
        None
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

/// A running process: its id, its parent's, and its program's name.
pub struct Entry {
    pub pid: u32,
    pub parent: u32,
    pub name: String,
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

#[cfg(any(not(windows), test))]
/// A `.desktop` file's command line, its field codes dropped (none apply to a plain start).
pub fn desktop_exec(text: &str) -> Option<String> {
    let mut in_entry = false;
    for line in text.lines().map(str::trim) {
        if line.starts_with('[') {
            in_entry = line == "[Desktop Entry]";
        } else if let Some(exec) = line.strip_prefix("Exec=").filter(|_| in_entry) {
            let mut out = String::new();
            let mut chars = exec.chars();
            while let Some(c) = chars.next() {
                if c != '%' {
                    out.push(c);
                } else if chars.next() == Some('%') {
                    out.push('%');
                }
            }
            return Some(out.split_whitespace().collect::<Vec<_>>().join(" "));
        }
    }
    None
}

#[cfg(any(not(windows), test))]
/// The app id a Steam shortcut's command line starts (`steam steam://rungameid/<id>`).
fn steam_id_in(command: &str) -> Option<String> {
    let id = command.split("steam://rungameid/").nth(1)?;
    let id: String = id.chars().take_while(char::is_ascii_digit).collect();
    (!id.is_empty()).then_some(id)
}

/// On Linux: `.desktop` files and executables.
#[cfg(not(windows))]
mod proc {
    use std::path::{Path, PathBuf};
    use std::process::Command;

    pub use super::{Entry, children, kill_tree};

    /// `/proc/<pid>/stat`: the name in parentheses, then the state and the parent's id.
    fn stat(pid: u32) -> Option<Entry> {
        let stat = std::fs::read_to_string(format!("/proc/{pid}/stat")).ok()?;
        let (head, tail) = stat.rsplit_once(')')?;
        let name = head.split_once('(')?.1.to_owned();
        let mut fields = tail.split_whitespace();
        let state = fields.next()?;
        let parent = fields.next()?.parse().ok()?;
        (state != "Z").then_some(Entry { pid, parent, name })
    }

    pub fn table() -> Vec<Entry> {
        let Ok(dir) = std::fs::read_dir("/proc") else { return Vec::new() };
        dir.filter_map(Result::ok).filter_map(|e| e.file_name().to_str()?.parse().ok()).filter_map(stat).collect()
    }

    /// The kernel keeps 15 bytes of a process's name.
    fn same_name(comm: &str, name: &str) -> bool {
        let name = name.get(..15).unwrap_or(name);
        comm.eq_ignore_ascii_case(name)
    }

    pub fn running_named(stem: &str) -> bool {
        let exe = format!("{stem}.exe");
        table().iter().any(|e| same_name(&e.name, stem) || same_name(&e.name, &exe))
    }

    /// The program of a process: its executable, or for a Wine program its Windows path
    /// (`Z:\home\...` is the Linux root).
    fn programs(pid: u32) -> Vec<PathBuf> {
        let mut out: Vec<PathBuf> = std::fs::read_link(format!("/proc/{pid}/exe")).into_iter().collect();
        let cmdline = std::fs::read(format!("/proc/{pid}/cmdline")).unwrap_or_default();
        if let Some(first) = cmdline.split(|&b| b == 0).next() {
            let first = String::from_utf8_lossy(first);
            match first.get(..2) {
                Some(drive) if drive.eq_ignore_ascii_case("z:") => out.push(first[2..].replace('\\', "/").into()),
                _ => out.push(PathBuf::from(&*first)),
            }
        }
        out
    }

    pub fn running_under(dir: &Path) -> Option<(u32, String)> {
        table().into_iter().find_map(|e| {
            let program = programs(e.pid).into_iter().find(|p| p.starts_with(dir))?;
            Some((e.pid, program.display().to_string()))
        })
    }

    /// Starts the shortcut on its own; a thread waits for it so it does not linger as a zombie.
    pub fn start(path: &Path) -> Option<u32> {
        let mut command = if path.extension().is_some_and(|e| e == "desktop") {
            let text = std::fs::read_to_string(path).ok()?;
            let exec = super::desktop_exec(&text)?;
            let mut command = Command::new("sh");
            command.arg("-c").arg(format!("exec {exec}"));
            let dir = text.lines().find_map(|l| l.trim().strip_prefix("Path=").map(PathBuf::from));
            if let Some(dir) = dir.filter(|d| d.is_dir()) {
                command.current_dir(dir);
            }
            command
        } else {
            let mut command = Command::new(path);
            if let Some(dir) = path.parent() {
                command.current_dir(dir);
            }
            command
        };
        crate::platform::detach(&mut command);
        let mut child = command.spawn().ok()?;
        let pid = child.id();
        std::thread::spawn(move || child.wait());
        Some(pid)
    }

    /// A `.desktop` file (its Steam app id if it starts a Steam game) or an executable.
    pub fn shortcut(path: &Path) -> Option<Result<(), String>> {
        use std::os::unix::fs::PermissionsExt;
        if path.extension().is_some_and(|e| e == "desktop") {
            let exec = super::desktop_exec(&std::fs::read_to_string(path).ok()?)?;
            return Some(super::steam_id_in(&exec).map_or(Ok(()), Err));
        }
        let executable = std::fs::metadata(path).is_ok_and(|m| m.permissions().mode() & 0o111 != 0);
        executable.then_some(Ok(()))
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

    #[test]
    fn desktop_files_give_their_command_without_field_codes() {
        let text = "[Desktop Action x]\nExec=nope\n[Desktop Entry]\nName=OBS\nExec=obs --minimize-to-tray %U 100%%\n";
        assert_eq!(desktop_exec(text).as_deref(), Some("obs --minimize-to-tray 100%"));
        assert_eq!(desktop_exec("[Desktop Entry]\nName=x\n"), None);
        assert_eq!(steam_id_in("steam steam://rungameid/1009850").as_deref(), Some("1009850"));
        assert_eq!(steam_id_in("obs"), None);
    }

    #[test]
    fn a_process_tree_is_followed_down() {
        let entry = |pid, parent| Entry { pid, parent, name: String::new() };
        let table = [entry(1, 0), entry(2, 1), entry(3, 2), entry(4, 1), entry(5, 9)];
        assert_eq!(children(&table, 1, false), vec![2, 4]);
        assert_eq!(children(&table, 1, true), vec![2, 3, 4]);
    }

    #[cfg(unix)]
    #[test]
    fn linux_shortcuts_are_desktop_files_and_programs() {
        use std::os::unix::fs::PermissionsExt;
        let dir = tempfile::tempdir().unwrap();
        std::fs::write(dir.path().join("a.desktop"), "[Desktop Entry]\nExec=true\n").unwrap();
        std::fs::write(dir.path().join("game.desktop"), "[Desktop Entry]\nExec=steam steam://rungameid/42\n").unwrap();
        let script = dir.path().join("run.sh");
        std::fs::write(&script, "#!/bin/sh\n").unwrap();
        std::fs::set_permissions(&script, std::fs::Permissions::from_mode(0o755)).unwrap();
        std::fs::write(dir.path().join("notes.txt"), "hello").unwrap();
        let (files, steam) = shortcuts_in(dir.path());
        assert_eq!(files, vec![dir.path().join("a.desktop"), script]);
        assert_eq!(steam, vec!["42".to_string()]);
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn started_programs_are_found_in_the_process_table() {
        let me = std::process::id();
        let table = proc::table();
        let parent = table.iter().find(|e| e.pid == me).map(|e| e.parent).unwrap();
        assert!(children(&table, parent, false).contains(&me));
        let dir = tempfile::tempdir().unwrap();
        let desktop = dir.path().join("sleep.desktop");
        std::fs::write(&desktop, "[Desktop Entry]\nExec=sleep 30\n").unwrap();
        let pid = proc::start(&desktop).unwrap();
        std::thread::sleep(Duration::from_millis(200));
        assert!(proc::table().iter().any(|e| e.pid == pid && e.name == "sleep"));
        kill_tree(&proc::table(), pid);
        assert!(crate::platform::wait_for_exit(pid, Duration::from_secs(5)));
    }
}
