//! The agent's settings: the same `client-service.json` the Qt desktop app reads and writes, with the
//! same keys and locations, so an existing install keeps working.
//!
//! Location: `$VRCX_CLIENT_CONFIG` if set. Otherwise the per-user file if it exists
//! (`%APPDATA%\VRCX\client-service.json`, `$XDG_CONFIG_HOME/vrcx/client-service.json`), else the shared
//! one (`%ProgramData%\VRCX\client-service.json`). Saving falls back to the per-user file when the
//! shared one cannot be written.

use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Config {
    /// The server's http(s) base, e.g. `http://nas:5280`; the socket is `/1/ws`.
    pub server_address: String,
    /// The server's API token (`VRCX:Auth:Token`).
    pub bearer_token: String,
    /// Keeps this PC's log stream apart from other PCs'; the machine name when unset.
    pub client_id: String,
    /// The VRChat log folder; empty means the platform default.
    pub vrchat_log_dir: String,
    /// Extra arguments VRChat is started with.
    pub launch_arguments: String,
    /// The VRChat folder or its launch.exe; empty means find it.
    pub vrc_launch_path_override: String,
    /// Where the web client is, for `vrcx://` commands; empty means the server address, which is
    /// right when the web image serves both (its Caddyfile proxies `/1/*` to the server).
    #[serde(skip_serializing_if = "String::is_empty")]
    pub web_address: String,
}

fn env(name: &str) -> Option<String> {
    std::env::var(name).ok().filter(|v| !v.is_empty())
}

fn home() -> PathBuf {
    env("USERPROFILE").or_else(|| env("HOME")).map(PathBuf::from).unwrap_or_default()
}

fn shared_path() -> PathBuf {
    if let Some(path) = env("VRCX_CLIENT_CONFIG") {
        return path.into();
    }
    if cfg!(windows) {
        PathBuf::from(env("ProgramData").unwrap_or_else(|| "C:/ProgramData".into())).join("VRCX/client-service.json")
    } else {
        user_path()
    }
}

fn user_path() -> PathBuf {
    if cfg!(windows) {
        env("APPDATA")
            .map(PathBuf::from)
            .unwrap_or_else(|| home().join("AppData/Roaming"))
            .join("VRCX/client-service.json")
    } else {
        env("XDG_CONFIG_HOME")
            .map(PathBuf::from)
            .unwrap_or_else(|| home().join(".config"))
            .join("vrcx/client-service.json")
    }
}

/// The file `load` reads.
pub fn path() -> PathBuf {
    let user = user_path();
    if env("VRCX_CLIENT_CONFIG").is_none() && user.exists() { user } else { shared_path() }
}

fn machine_name() -> String {
    env("COMPUTERNAME")
        .or_else(|| env("HOSTNAME"))
        .or_else(|| {
            std::fs::read_to_string("/etc/hostname").ok().map(|s| s.trim().to_owned()).filter(|s| !s.is_empty())
        })
        .unwrap_or_else(|| "client".into())
}

impl Config {
    pub fn load() -> Self {
        Self::load_from(&path())
    }

    pub fn load_from(path: &Path) -> Self {
        let mut config: Config =
            std::fs::read(path).ok().and_then(|bytes| serde_json::from_slice(&bytes).ok()).unwrap_or_default();
        if config.client_id.trim().is_empty() {
            config.client_id = machine_name();
        }
        config
    }

    /// Writes the settings, trimmed; returns where they went.
    pub fn save(&self) -> std::io::Result<PathBuf> {
        let mut clean = self.clone();
        for field in [
            &mut clean.server_address,
            &mut clean.client_id,
            &mut clean.vrchat_log_dir,
            &mut clean.launch_arguments,
            &mut clean.vrc_launch_path_override,
            &mut clean.web_address,
        ] {
            *field = field.trim().to_owned();
        }
        let json = serde_json::to_vec_pretty(&clean)?;
        if env("VRCX_CLIENT_CONFIG").is_some() {
            return write_atomic(&shared_path(), &json).map(|()| shared_path());
        }
        let user = user_path();
        let primary = if user.exists() { user.clone() } else { shared_path() };
        match write_atomic(&primary, &json) {
            Ok(()) => Ok(primary),
            Err(_) if primary != user => write_atomic(&user, &json).map(|()| user),
            Err(e) => Err(e),
        }
    }

    pub fn is_configured(&self) -> bool {
        !self.server_address.trim().is_empty()
    }

    /// `vrchat_log_dir` if set, else `%LocalAppData%Low\VRChat\VRChat` (where the original's
    /// GetVRChatAppDataLocation looks); on Linux the Steam Proton prefix.
    pub fn log_dir(&self) -> Option<PathBuf> {
        if !self.vrchat_log_dir.trim().is_empty() {
            return Some(self.vrchat_log_dir.trim().into());
        }
        if cfg!(windows) {
            env("LOCALAPPDATA").map(|local| PathBuf::from(format!("{local}Low")).join("VRChat/VRChat"))
        } else {
            let data = env("XDG_DATA_HOME").map(PathBuf::from).unwrap_or_else(|| home().join(".local/share"));
            let dir = data
                .join("Steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat");
            dir.exists().then_some(dir)
        }
    }

    /// The web client's base, without a trailing slash.
    pub fn web_base(&self) -> String {
        let web = if self.web_address.trim().is_empty() { &self.server_address } else { &self.web_address };
        web.trim().trim_end_matches('/').to_owned()
    }
}

fn write_atomic(path: &Path, bytes: &[u8]) -> std::io::Result<()> {
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let tmp = path.with_extension("json.tmp");
    std::fs::write(&tmp, bytes)?;
    std::fs::rename(&tmp, path).inspect_err(|_| {
        let _ = std::fs::remove_file(&tmp);
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_desktop_apps_keys() {
        let dir = tempfile::tempdir().unwrap();
        let file = dir.path().join("client-service.json");
        std::fs::write(
            &file,
            r#"{"serverAddress":"http://nas:5280","bearerToken":"t","clientId":"pc","vrchatLogDir":"D:/logs","launchArguments":"--fps=144","vrcLaunchPathOverride":"","unknown":1}"#,
        )
        .unwrap();
        let config = Config::load_from(&file);
        assert_eq!(config.server_address, "http://nas:5280");
        assert_eq!(config.bearer_token, "t");
        assert_eq!(config.client_id, "pc");
        assert_eq!(config.log_dir(), Some(PathBuf::from("D:/logs")));
        assert_eq!(config.launch_arguments, "--fps=144");
        assert_eq!(config.web_base(), "http://nas:5280");
    }

    #[test]
    fn a_missing_file_still_gets_a_client_id() {
        let config = Config::load_from(Path::new("does/not/exist.json"));
        assert!(!config.client_id.is_empty());
        assert!(!config.is_configured());
    }

    #[test]
    fn the_web_address_wins_when_set() {
        let config = Config {
            server_address: "http://nas:5280/".into(),
            web_address: "https://vrcx.example/".into(),
            ..Default::default()
        };
        assert_eq!(config.web_base(), "https://vrcx.example");
    }
}
