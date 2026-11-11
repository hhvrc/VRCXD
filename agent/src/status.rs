//! A small `status.json` next to the agent's logs, so `vrcxd-agent status` can say what the running
//! agent is doing without a window of its own.

use serde::{Deserialize, Serialize};

use crate::platform;

#[derive(Debug, Default, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Status {
    pub pid: u32,
    pub state: String,
    pub since: String,
    pub entries_sent: u64,
    pub last_sent_at: String,
}

pub fn path() -> std::path::PathBuf {
    platform::data_dir().join("status.json")
}

impl Status {
    pub fn read() -> Option<Self> {
        serde_json::from_slice(&std::fs::read(path()).ok()?).ok()
    }

    fn write(&self) {
        let _ = std::fs::create_dir_all(platform::data_dir());
        if let Ok(json) = serde_json::to_vec_pretty(self) {
            let _ = std::fs::write(path(), json);
        }
    }

    pub fn set_state(&mut self, state: &str) {
        if self.state == state {
            return;
        }
        self.pid = std::process::id();
        self.state = state.to_owned();
        self.since = chrono::Local::now().to_rfc3339();
        self.write();
    }

    pub fn sent(&mut self, entries: usize) {
        self.entries_sent += entries as u64;
        self.last_sent_at = chrono::Local::now().to_rfc3339();
        self.write();
    }
}
