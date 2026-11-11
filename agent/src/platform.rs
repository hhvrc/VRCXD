//! What differs between Windows and Linux: processes, file locks, the registry, named pipes,
//! opening links, and starting at login.

use std::io;
use std::path::{Path, PathBuf};
use std::process::Command;

pub use imp::*;

/// Where the agent keeps its logs and lock file.
pub fn data_dir() -> PathBuf {
    let env = |n: &str| std::env::var_os(n).filter(|v| !v.is_empty()).map(PathBuf::from);
    if cfg!(windows) {
        env("LOCALAPPDATA").unwrap_or_else(|| PathBuf::from(".")).join("VRCXD")
    } else {
        env("XDG_STATE_HOME")
            .or_else(|| env("HOME").map(|h| h.join(".local/state")))
            .unwrap_or_else(|| PathBuf::from("."))
            .join("vrcxd")
    }
}

fn current_exe() -> io::Result<PathBuf> {
    std::env::current_exe().map(|p| std::path::absolute(&p).unwrap_or(p))
}

#[cfg(windows)]
mod imp {
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;
    use std::os::windows::process::CommandExt;

    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::windows::named_pipe::ClientOptions;
    use windows_sys::Win32::Foundation::{
        CloseHandle, ERROR_PIPE_BUSY, GENERIC_READ, GENERIC_WRITE, INVALID_HANDLE_VALUE, WAIT_TIMEOUT,
    };
    use windows_sys::Win32::Storage::FileSystem::{CreateFileW, FILE_ATTRIBUTE_NORMAL, OPEN_EXISTING};
    use windows_sys::Win32::System::Console::{ATTACH_PARENT_PROCESS, AttachConsole};
    use windows_sys::Win32::System::Diagnostics::ToolHelp::{
        CreateToolhelp32Snapshot, PROCESSENTRY32W, Process32FirstW, Process32NextW, TH32CS_SNAPPROCESS,
    };
    use windows_sys::Win32::System::Registry::{
        HKEY, HKEY_CLASSES_ROOT, HKEY_CURRENT_USER, REG_SZ, RRF_RT_REG_EXPAND_SZ, RRF_RT_REG_SZ, RegDeleteKeyValueW,
        RegDeleteTreeW, RegGetValueW, RegSetKeyValueW,
    };
    use windows_sys::Win32::System::Threading::{
        CREATE_NEW_PROCESS_GROUP, DETACHED_PROCESS, INFINITE, OpenProcess, PROCESS_SYNCHRONIZE, PROCESS_TERMINATE,
        TerminateProcess, WaitForSingleObject,
    };
    use windows_sys::Win32::System::WindowsProgramming::GetUserNameW;
    use windows_sys::Win32::UI::Shell::ShellExecuteW;
    use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;

    use super::*;

    pub const GAME_URL_PIPE: &str = r"\\.\pipe\VRChatURLLaunchPipe";

    fn wide(s: impl AsRef<OsStr>) -> Vec<u16> {
        s.as_ref().encode_wide().chain(Some(0)).collect()
    }

    /// The process ids of every process with this executable name.
    pub fn processes_named(name: &str) -> Vec<u32> {
        let mut pids = Vec::new();
        // SAFETY: a snapshot handle is closed below; entry is sized as the API asks.
        unsafe {
            let snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if snap == INVALID_HANDLE_VALUE {
                return pids;
            }
            let mut entry: PROCESSENTRY32W = std::mem::zeroed();
            entry.dwSize = size_of::<PROCESSENTRY32W>() as u32;
            let mut ok = Process32FirstW(snap, &mut entry);
            while ok != 0 {
                let len = entry.szExeFile.iter().position(|&c| c == 0).unwrap_or(entry.szExeFile.len());
                if String::from_utf16_lossy(&entry.szExeFile[..len]).eq_ignore_ascii_case(name) {
                    pids.push(entry.th32ProcessID);
                }
                ok = Process32NextW(snap, &mut entry);
            }
            CloseHandle(snap);
        }
        pids
    }

    pub fn terminate(pid: u32) -> io::Result<()> {
        // SAFETY: the handle is checked and closed.
        unsafe {
            let process = OpenProcess(PROCESS_TERMINATE, 0, pid);
            if process.is_null() {
                return Err(io::Error::last_os_error());
            }
            let ok = TerminateProcess(process, 1);
            CloseHandle(process);
            if ok == 0 { Err(io::Error::last_os_error()) } else { Ok(()) }
        }
    }

    /// Blocks until the process ends (at once when it is already gone). No polling.
    /// Waits for the process to end, at most `timeout`; whether it ended (or was not there).
    pub fn wait_for_exit(pid: u32, timeout: std::time::Duration) -> bool {
        // SAFETY: the handle is checked and closed.
        unsafe {
            let process = OpenProcess(PROCESS_SYNCHRONIZE, 0, pid);
            if process.is_null() {
                return true;
            }
            let waited = WaitForSingleObject(process, u32::try_from(timeout.as_millis()).unwrap_or(INFINITE - 1));
            CloseHandle(process);
            waited != WAIT_TIMEOUT
        }
    }

    /// Whether the file opens with no sharing at all: true once whatever wrote it (VRChat) closed it.
    pub fn is_file_unlocked(path: &Path) -> bool {
        let name = wide(path);
        // SAFETY: name is NUL-terminated; the handle is closed.
        unsafe {
            let h = CreateFileW(
                name.as_ptr(),
                GENERIC_READ | GENERIC_WRITE,
                0,
                std::ptr::null(),
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL,
                std::ptr::null_mut(),
            );
            if h == INVALID_HANDLE_VALUE {
                return false;
            }
            CloseHandle(h);
            true
        }
    }

    fn reg_string(root: HKEY, key: &str, value: Option<&str>) -> Option<String> {
        let key = wide(key);
        let value = value.map(wide);
        let value_ptr = value.as_ref().map_or(std::ptr::null(), |v| v.as_ptr());
        let mut size = 0u32;
        let flags = RRF_RT_REG_SZ | RRF_RT_REG_EXPAND_SZ;
        // SAFETY: sizes come from the first call; the buffer is that large.
        unsafe {
            if RegGetValueW(root, key.as_ptr(), value_ptr, flags, std::ptr::null_mut(), std::ptr::null_mut(), &mut size)
                != 0
            {
                return None;
            }
            let mut buf = vec![0u16; (size as usize).div_ceil(2)];
            if RegGetValueW(
                root,
                key.as_ptr(),
                value_ptr,
                flags,
                std::ptr::null_mut(),
                buf.as_mut_ptr().cast(),
                &mut size,
            ) != 0
            {
                return None;
            }
            let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
            Some(String::from_utf16_lossy(&buf[..len]))
        }
    }

    fn reg_set(key: &str, value: Option<&str>, data: &str) -> io::Result<()> {
        let key = wide(key);
        let value = value.map(wide);
        let data = wide(data);
        // SAFETY: every string is NUL-terminated; the size is in bytes and includes the NUL.
        let status = unsafe {
            RegSetKeyValueW(
                HKEY_CURRENT_USER,
                key.as_ptr(),
                value.as_ref().map_or(std::ptr::null(), |v| v.as_ptr()),
                REG_SZ,
                data.as_ptr().cast(),
                (data.len() * 2) as u32,
            )
        };
        if status == 0 { Ok(()) } else { Err(io::Error::from_raw_os_error(status as i32)) }
    }

    /// The command a URL scheme opens with (`HKEY_CLASSES_ROOT\<scheme>\shell\open\command`).
    pub fn url_handler_command(scheme: &str) -> Option<String> {
        reg_string(HKEY_CLASSES_ROOT, &format!(r"{scheme}\shell\open\command"), None)
    }

    /// Lets a started program outlive the agent without a console of its own.
    pub fn detach(command: &mut Command) {
        command.creation_flags(DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP);
    }

    /// Writes `request` to a named pipe and reads a one-byte answer.
    pub async fn pipe_request(name: &str, request: &[u8]) -> io::Result<u8> {
        let mut pipe = loop {
            match ClientOptions::new().open(name) {
                Ok(pipe) => break pipe,
                Err(e) if e.raw_os_error() == Some(ERROR_PIPE_BUSY as i32) => {
                    tokio::time::sleep(std::time::Duration::from_millis(50)).await;
                }
                Err(e) => return Err(e),
            }
        };
        pipe.write_all(request).await?;
        let mut answer = [0u8; 1];
        pipe.read_exact(&mut answer).await?;
        Ok(answer[0])
    }

    pub fn open_url(url: &str) -> io::Result<()> {
        let (verb, target) = (wide("open"), wide(url));
        // SAFETY: both strings are NUL-terminated.
        let result = unsafe {
            ShellExecuteW(
                std::ptr::null_mut(),
                verb.as_ptr(),
                target.as_ptr(),
                std::ptr::null(),
                std::ptr::null(),
                SW_SHOWNORMAL,
            )
        };
        // ShellExecute reports success as a value above 32.
        if result as usize > 32 { Ok(()) } else { Err(io::Error::other(format!("could not open {url}"))) }
    }

    /// The original's IPC pipe name: `vrcx-ipc-<n>`, n the sum of the user name's UTF-16 code units.
    pub fn ipc_pipe_name() -> String {
        // For tests: a pipe name that is not the user's own.
        if let Some(name) = std::env::var("VRCXD_IPC_PIPE").ok().filter(|n| !n.is_empty()) {
            return name;
        }
        let mut buf = [0u16; 257];
        let mut len = buf.len() as u32;
        // SAFETY: len is the buffer's size in characters.
        let user: Vec<u16> = if unsafe { GetUserNameW(buf.as_mut_ptr(), &mut len) } != 0 {
            buf[..len.saturating_sub(1) as usize].to_vec()
        } else {
            std::env::var("USERNAME").unwrap_or_default().encode_utf16().collect()
        };
        let sum: u64 = user.iter().map(|&c| u64::from(c)).sum();
        format!(r"\\.\pipe\vrcx-ipc-{sum}")
    }

    /// Writes to the console the agent was started from, if any; whether there is one.
    pub fn attach_console() -> bool {
        use windows_sys::Win32::System::Console::{GetStdHandle, STD_ERROR_HANDLE};
        // SAFETY: no arguments to get wrong. A process that already has a console (a debug build,
        // or output redirected by its parent) keeps it; started at login it has none.
        unsafe {
            AttachConsole(ATTACH_PARENT_PROCESS);
            let err = GetStdHandle(STD_ERROR_HANDLE);
            !err.is_null() && err != INVALID_HANDLE_VALUE
        }
    }

    /// The user's Pictures folder (the known folder, wherever it was moved).
    pub fn pictures_dir() -> Option<PathBuf> {
        use windows_sys::Win32::System::Com::CoTaskMemFree;
        use windows_sys::Win32::UI::Shell::{FOLDERID_Pictures, KF_FLAG_DEFAULT, SHGetKnownFolderPath};
        let mut raw: *mut u16 = std::ptr::null_mut();
        // SAFETY: the returned string is freed with CoTaskMemFree, as the API asks.
        unsafe {
            let ok =
                SHGetKnownFolderPath(&FOLDERID_Pictures, KF_FLAG_DEFAULT as u32, std::ptr::null_mut(), &mut raw) == 0;
            let path = ok.then(|| {
                let len = (0..).take_while(|&i| *raw.add(i) != 0).count();
                PathBuf::from(String::from_utf16_lossy(std::slice::from_raw_parts(raw, len)))
            });
            CoTaskMemFree(raw.cast());
            path
        }
    }

    /// Steam's install folder (`HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`).
    pub fn steam_install_dir() -> Option<PathBuf> {
        use windows_sys::Win32::System::Registry::HKEY_LOCAL_MACHINE;
        reg_string(HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam", Some("InstallPath"))
            .map(PathBuf::from)
            .or_else(|| std::env::var_os("ProgramFiles(x86)").map(|p| PathBuf::from(p).join("Steam")))
            .filter(|p| p.is_dir())
    }

    /// Opens a folder in Explorer, with `item` selected when given.
    pub fn open_folder(folder: &Path, item: Option<&Path>) -> io::Result<()> {
        let mut explorer = Command::new("explorer.exe");
        match item {
            Some(item) => explorer.raw_arg(format!("/select,\"{}\"", item.display())),
            None => explorer.arg(folder),
        };
        // Explorer exits with 1 even when it opened the window.
        explorer.spawn().map(drop)
    }

    const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
    const RUN_VALUE: &str = "VRCXD Agent";
    const SCHEME_KEY: &str = r"Software\Classes\vrcx";

    /// Starts the agent at login (the current user's Run key) and makes it open `vrcx://` links.
    pub fn install() -> io::Result<Vec<String>> {
        let exe = current_exe()?.display().to_string();
        reg_set(RUN_KEY, Some(RUN_VALUE), &format!("\"{exe}\" run"))?;
        reg_set(SCHEME_KEY, None, "URL:vrcx")?;
        reg_set(SCHEME_KEY, Some("URL Protocol"), "")?;
        reg_set(&format!(r"{SCHEME_KEY}\DefaultIcon"), None, &format!("\"{exe}\",0"))?;
        reg_set(&format!(r"{SCHEME_KEY}\shell\open\command"), None, &format!("\"{exe}\" open-url \"%1\""))?;
        Ok(vec![
            format!(r"HKCU\{RUN_KEY}\{RUN_VALUE}: starts the agent at login"),
            format!(r"HKCU\{SCHEME_KEY}: vrcx:// links open the web client's launch page"),
        ])
    }

    pub fn uninstall() -> io::Result<Vec<String>> {
        let (run, value, scheme) = (wide(RUN_KEY), wide(RUN_VALUE), wide(SCHEME_KEY));
        // SAFETY: NUL-terminated names; a key or value that is not there is fine.
        unsafe {
            RegDeleteKeyValueW(HKEY_CURRENT_USER, run.as_ptr(), value.as_ptr());
            RegDeleteTreeW(HKEY_CURRENT_USER, scheme.as_ptr());
            let _ = windows_sys::Win32::System::Registry::RegDeleteKeyW(HKEY_CURRENT_USER, scheme.as_ptr());
        }
        Ok(vec![format!(r"removed HKCU\{RUN_KEY}\{RUN_VALUE} and HKCU\{SCHEME_KEY}")])
    }

    /// Whether the login entry points at this executable.
    pub fn installed() -> bool {
        reg_string(HKEY_CURRENT_USER, RUN_KEY, Some(RUN_VALUE)).is_some()
    }
}

#[cfg(not(windows))]
mod imp {
    use super::*;

    pub const GAME_URL_PIPE: &str = "";

    /// The process ids of every process with this name (`/proc/<pid>/comm`; Proton's VRChat shows
    /// as VRChat.exe there).
    pub fn processes_named(name: &str) -> Vec<u32> {
        let Ok(dir) = std::fs::read_dir("/proc") else { return Vec::new() };
        dir.filter_map(Result::ok)
            .filter_map(|e| e.file_name().to_str()?.parse::<u32>().ok())
            .filter(|pid| {
                std::fs::read_to_string(format!("/proc/{pid}/comm")).is_ok_and(|c| c.trim().eq_ignore_ascii_case(name))
            })
            .collect()
    }

    pub fn terminate(pid: u32) -> io::Result<()> {
        let status = Command::new("kill").arg("-TERM").arg(pid.to_string()).status()?;
        if status.success() { Ok(()) } else { Err(io::Error::other(format!("kill exited with {status}"))) }
    }

    /// Waits for the process to end, checking every five seconds.
    pub fn wait_for_exit(pid: u32, timeout: std::time::Duration) -> bool {
        let until = std::time::Instant::now() + timeout;
        while Path::new(&format!("/proc/{pid}")).exists() {
            if std::time::Instant::now() >= until {
                return false;
            }
            std::thread::sleep(std::time::Duration::from_secs(1).min(timeout));
        }
        true
    }

    pub fn is_file_unlocked(_path: &Path) -> bool {
        true
    }

    pub fn url_handler_command(_scheme: &str) -> Option<String> {
        None
    }

    pub fn detach(_command: &mut Command) {}

    pub async fn pipe_request(_name: &str, _request: &[u8]) -> io::Result<u8> {
        Err(io::Error::new(io::ErrorKind::Unsupported, "the game's URL pipe is Windows only"))
    }

    pub fn open_url(url: &str) -> io::Result<()> {
        Command::new("xdg-open").arg(url).spawn().map(drop)
    }

    #[allow(dead_code)] // The named pipe is Windows only.
    pub fn ipc_pipe_name() -> String {
        String::new()
    }

    pub fn attach_console() -> bool {
        true
    }

    pub fn pictures_dir() -> Option<PathBuf> {
        let out = Command::new("xdg-user-dir").arg("PICTURES").output().ok();
        out.and_then(|o| String::from_utf8(o.stdout).ok())
            .map(|s| PathBuf::from(s.trim()))
            .filter(|p| p.is_dir())
            .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join("Pictures")))
    }

    pub fn steam_install_dir() -> Option<PathBuf> {
        let home = PathBuf::from(std::env::var_os("HOME")?);
        [home.join(".local/share/Steam"), home.join(".steam/steam")].into_iter().find(|p| p.is_dir())
    }

    /// Opens the folder (file managers differ in selecting an item, so the folder is opened).
    pub fn open_folder(folder: &Path, item: Option<&Path>) -> io::Result<()> {
        let dir = item.and_then(|i| if i.is_dir() { Some(i) } else { i.parent() }).unwrap_or(folder);
        Command::new("xdg-open").arg(dir).spawn().map(drop)
    }

    fn unit_path() -> PathBuf {
        config_home().join("systemd/user/vrcxd-agent.service")
    }

    fn config_home() -> PathBuf {
        std::env::var_os("XDG_CONFIG_HOME")
            .filter(|v| !v.is_empty())
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(std::env::var_os("HOME").unwrap_or_default()).join(".config"))
    }

    fn desktop_path() -> PathBuf {
        let data = std::env::var_os("XDG_DATA_HOME")
            .filter(|v| !v.is_empty())
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(std::env::var_os("HOME").unwrap_or_default()).join(".local/share"));
        data.join("applications").join(DESKTOP_FILE)
    }

    const DESKTOP_FILE: &str = "vrcxd-agent.desktop";

    /// The `.desktop` file that makes the agent open `vrcx://` links.
    pub fn desktop_entry(exe: &Path) -> String {
        let exe = exe.display().to_string().replace('\\', "\\\\").replace('"', "\\\"");
        format!(
            "[Desktop Entry]\nType=Application\nName=VRCXD agent\nComment=Opens vrcx:// links in the VRCXD web client\nExec=\"{exe}\" open-url %u\nMimeType=x-scheme-handler/vrcx;\nNoDisplay=true\nTerminal=false\n"
        )
    }

    /// A `systemd --user` unit started at login, and a `.desktop` handler for `vrcx://` links.
    pub fn install() -> io::Result<Vec<String>> {
        let exe = current_exe()?;
        let unit = unit_path();
        std::fs::create_dir_all(unit.parent().unwrap())?;
        std::fs::write(
            &unit,
            format!(
                "[Unit]\nDescription=VRCXD agent: ships VRChat's logs to the VRCXD server\n\n[Service]\nExecStart=\"{}\" run\nRestart=on-failure\nRestartSec=10\n\n[Install]\nWantedBy=default.target\n",
                exe.display()
            ),
        )?;
        let status = Command::new("systemctl").args(["--user", "enable", "--now", "vrcxd-agent.service"]).status()?;
        if !status.success() {
            return Err(io::Error::other(format!("systemctl exited with {status}")));
        }
        let mut done = vec![format!("{}: enabled and started", unit.display())];

        let desktop = desktop_path();
        std::fs::create_dir_all(desktop.parent().unwrap())?;
        std::fs::write(&desktop, desktop_entry(&exe))?;
        // Neither tool is everywhere; the file alone is enough for desktops that read MimeType.
        let _ = Command::new("update-desktop-database").arg(desktop.parent().unwrap()).status();
        let linked = Command::new("xdg-mime")
            .args(["default", DESKTOP_FILE, "x-scheme-handler/vrcx"])
            .status()
            .is_ok_and(|s| s.success());
        done.push(format!(
            "{}: vrcx:// links open the web client's launch page{}",
            desktop.display(),
            if linked { "" } else { " (xdg-mime was not found; set it as the vrcx handler by hand)" }
        ));
        Ok(done)
    }

    pub fn uninstall() -> io::Result<Vec<String>> {
        let _ = Command::new("systemctl").args(["--user", "disable", "--now", "vrcxd-agent.service"]).status();
        let unit = unit_path();
        if unit.exists() {
            std::fs::remove_file(&unit)?;
        }
        let desktop = desktop_path();
        if desktop.exists() {
            std::fs::remove_file(&desktop)?;
            let _ = Command::new("update-desktop-database").arg(desktop.parent().unwrap()).status();
        }
        Ok(vec![format!("{}: disabled and removed", unit.display()), format!("{}: removed", desktop.display())])
    }

    pub fn installed() -> bool {
        unit_path().exists()
    }
}

#[cfg(all(test, not(windows)))]
mod tests {
    #[test]
    fn the_desktop_entry_hands_links_to_open_url() {
        let entry = super::desktop_entry(std::path::Path::new("/opt/my agent/vrcxd-agent"));
        assert!(entry.contains("Exec=\"/opt/my agent/vrcxd-agent\" open-url %u\n"));
        assert!(entry.contains("MimeType=x-scheme-handler/vrcx;\n"));
    }
}
