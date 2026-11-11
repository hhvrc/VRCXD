//! The agent's own behaviour, set from the web client's settings (`apply_agent_settings`) and kept in
//! `agent-settings.json` next to its logs, so it holds while the browser is closed.

use serde::{Deserialize, Serialize};

use crate::platform;
use crate::proto::AgentSettings;

#[derive(Debug, Default, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Settings {
    pub app_launcher_enabled: bool,
    pub app_launcher_kill_on_exit: bool,
    pub app_launcher_run_once: bool,
    pub relaunch_after_crash: bool,
    pub auto_sweep_cache: bool,
    pub ugc_folder: String,
    pub open_vr: bool,
    pub open_vr_always: bool,
    pub wrist_overlay: bool,
    pub overlay_menu_button: bool,
    pub overlay_hand: i32,
}

fn path() -> std::path::PathBuf {
    platform::data_dir().join("agent-settings.json")
}

impl Settings {
    pub fn load() -> Self {
        std::fs::read(path()).ok().and_then(|b| serde_json::from_slice(&b).ok()).unwrap_or_default()
    }

    pub fn save(&self) -> std::io::Result<()> {
        std::fs::create_dir_all(platform::data_dir())?;
        std::fs::write(path(), serde_json::to_vec_pretty(self)?)
    }
}

impl From<AgentSettings> for Settings {
    fn from(s: AgentSettings) -> Self {
        Self {
            app_launcher_enabled: s.app_launcher_enabled,
            app_launcher_kill_on_exit: s.app_launcher_kill_on_exit,
            app_launcher_run_once: s.app_launcher_run_once,
            relaunch_after_crash: s.relaunch_after_crash,
            auto_sweep_cache: s.auto_sweep_cache,
            ugc_folder: s.ugc_folder.trim().to_owned(),
            open_vr: s.open_vr,
            open_vr_always: s.open_vr_always,
            wrist_overlay: s.wrist_overlay,
            overlay_menu_button: s.overlay_menu_button,
            overlay_hand: s.overlay_hand,
        }
    }
}
