//! Tails VRChat's `output_log_*.txt` files and parses their new complete lines. A port of the Qt
//! app's `log_watcher.cpp`: oldest file first, complete lines only (a partial one is read again once
//! VRChat finishes it), a file shorter than what was read starts over, and files last written before
//! the server's resume point are skipped.
//!
//! Sizes come from `std::fs::metadata`, which on Windows asks the open file rather than the folder
//! listing, so a log VRChat still holds open shows its real length.

use std::collections::HashMap;
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::time::SystemTime;

use chrono::{DateTime, Utc};
use tracing::warn;

use crate::log_parser::{LogContext, parse_line};
use crate::proto::LogEntry;

#[derive(Debug, Default)]
struct FileState {
    length: u64,
    position: u64,
    waiting_for_complete_line: bool,
    ctx: LogContext,
}

struct LogFile {
    name: String,
    path: PathBuf,
    length: u64,
    modified: DateTime<Utc>,
    created: SystemTime,
}

pub struct LogWatcher {
    dir: PathBuf,
    till: DateTime<Utc>,
    files: HashMap<String, FileState>,
    caught_up: bool,
}

fn list_log_files(dir: &Path) -> Vec<LogFile> {
    let Ok(entries) = std::fs::read_dir(dir) else { return Vec::new() };
    let mut files: Vec<LogFile> = entries
        .filter_map(Result::ok)
        .filter_map(|entry| {
            let name = entry.file_name().into_string().ok()?;
            if !name.starts_with("output_log_") || !name.ends_with(".txt") {
                return None;
            }
            let path = entry.path();
            let meta = std::fs::metadata(&path).ok().filter(|m| m.is_file())?;
            let modified = meta.modified().ok()?;
            Some(LogFile {
                name,
                length: meta.len(),
                modified: modified.into(),
                // Creation time isn't on every filesystem.
                created: meta.created().unwrap_or(modified),
                path,
            })
        })
        .collect();
    files.sort_by_key(|f| f.created);
    files
}

/// Appends the entries of the complete lines after `state.position`, reading at most about
/// `remaining` bytes (a line longer than that is still read whole).
fn read_new_lines(
    file: &LogFile,
    state: &mut FileState,
    till: DateTime<Utc>,
    out: &mut Vec<LogEntry>,
    remaining: &mut u64,
) {
    let mut f = match File::open(&file.path) {
        Ok(f) => f,
        Err(e) => {
            warn!("failed to open {}: {e}", file.path.display());
            return;
        }
    };
    if f.seek(SeekFrom::Start(state.position)).is_err() {
        return;
    }
    let now = Utc::now();
    state.waiting_for_complete_line = false;
    let mut buf = Vec::new();
    let mut chunk = vec![0u8; 64 * 1024];
    let mut start = 0;
    while *remaining > 0 {
        let n = match f.read(&mut chunk) {
            Ok(0) => break,
            Ok(n) => n,
            Err(e) => {
                warn!("failed to read {}: {e}", file.path.display());
                break;
            }
        };
        buf.extend_from_slice(&chunk[..n]);
        while let Some(nl) = buf[start..].iter().position(|&b| b == b'\n') {
            let end = start + nl + 1;
            let raw = &buf[start..end];
            start = end;
            *remaining = remaining.saturating_sub(raw.len() as u64);
            state.position += raw.len() as u64;
            let line = String::from_utf8_lossy(raw);
            let line = line.trim_end_matches(['\n', '\r']);
            if line.is_empty() {
                continue;
            }
            if let Some((dt, entry)) = parse_line(&mut state.ctx, line, till, now) {
                out.push(LogEntry {
                    created_at: Some(prost_types::Timestamp { seconds: dt.timestamp(), nanos: 0 }),
                    file_name: file.name.clone(),
                    entry: Some(entry),
                });
            }
            if *remaining == 0 {
                return;
            }
        }
    }
    // Bytes after the last newline are a line VRChat is still writing.
    if start < buf.len() {
        state.waiting_for_complete_line = true;
    }
}

impl LogWatcher {
    /// Lines at or before `till` are skipped: pass the newest entry the server already has.
    pub fn new(dir: PathBuf, till: DateTime<Utc>) -> Self {
        Self { dir, till, files: HashMap::new(), caught_up: false }
    }

    /// Whether the last poll read everything there was.
    pub fn caught_up(&self) -> bool {
        self.caught_up
    }

    /// Reads what was appended since the last call, oldest file first.
    pub fn poll(&mut self, max_bytes: u64) -> Vec<LogEntry> {
        let mut entries = Vec::new();
        let mut remaining = max_bytes;
        if remaining == 0 {
            return entries;
        }
        let files = list_log_files(&self.dir);
        self.files.retain(|name, _| files.iter().any(|f| &f.name == name));

        for file in &files {
            if remaining == 0 {
                break;
            }
            if file.modified < self.till {
                continue;
            }
            let state = self.files.entry(file.name.clone()).or_default();
            if file.length < state.position {
                // Truncated or replaced: start over.
                *state = FileState::default();
            }
            if state.length == file.length && (state.position >= file.length || state.waiting_for_complete_line) {
                continue;
            }
            state.length = file.length;
            read_new_lines(file, state, self.till, &mut entries, &mut remaining);
        }

        self.caught_up = files
            .iter()
            .filter(|f| f.modified >= self.till && f.length > 0)
            .all(|f| self.files.get(&f.name).is_some_and(|s| s.position >= f.length || s.waiting_for_complete_line));
        entries
    }
}

#[cfg(test)]
mod tests {
    //! The Qt app's `test_log_watcher.cpp`.
    use std::io::Write;

    use super::*;
    use crate::proto::log_entry::Entry;

    fn append(path: &Path, text: &str) {
        std::fs::OpenOptions::new().create(true).append(true).open(path).unwrap().write_all(text.as_bytes()).unwrap();
    }

    #[test]
    fn tails_complete_lines_across_polls() {
        let dir = tempfile::tempdir().unwrap();
        let log = dir.path().join("output_log_2025-01-01_00-00-00.txt");
        File::create(&log).unwrap();
        // Only output_log_*.txt is VRChat's.
        append(&dir.path().join("unrelated.txt"), "2021.06.23 12:02:56 Log        -  VR Disabled\n");

        let mut watcher = LogWatcher::new(dir.path().into(), DateTime::UNIX_EPOCH);
        assert!(watcher.poll(u64::MAX).is_empty());

        append(
            &log,
            "2021.06.23 12:02:56 Log        -  [Behaviour] Entering Room: Home\r\n\
             2021.06.23 12:02:57 Log        -  [Behaviour] Joining wrld_x:1\r\n\
             2021.06.23 12:02:58 Log        -  VR Dis",
        );
        let entries = watcher.poll(u64::MAX);
        assert_eq!(entries.len(), 1);
        assert_eq!(entries[0].file_name, "output_log_2025-01-01_00-00-00.txt");
        assert!(
            matches!(&entries[0].entry, Some(Entry::Location(l)) if l.world_name == "Home" && l.location == "wrld_x:1")
        );
        assert!(watcher.caught_up());

        // The partial line is picked up once it's finished, not dropped or halved.
        append(&log, "abled\n");
        let entries = watcher.poll(u64::MAX);
        assert_eq!(entries.len(), 1);
        assert!(matches!(entries[0].entry, Some(Entry::DesktopMode(_))));
        assert!(watcher.poll(u64::MAX).is_empty());
    }

    #[test]
    fn restarts_when_a_file_is_truncated() {
        let dir = tempfile::tempdir().unwrap();
        let log = dir.path().join("output_log_2025-01-02_00-00-00.txt");
        File::create(&log).unwrap();
        let mut watcher = LogWatcher::new(dir.path().into(), DateTime::UNIX_EPOCH);
        append(
            &log,
            "2021.06.23 12:02:56 Log        -  [Behaviour] Entering Room: Home\r\n\
             2021.06.23 12:02:57 Log        -  [Behaviour] Joining wrld_x:1\r\n\
             2021.06.23 12:02:58 Log        -  [Behaviour] OnPlayerJoined Bob (usr_bob)\r\n",
        );
        assert_eq!(watcher.poll(u64::MAX).len(), 2);

        // Rewritten shorter than what was read: the only truncation signal there is.
        File::create(&log).unwrap();
        append(
            &log,
            "2021.06.23 13:00:00 Log        -  [Behaviour] Entering Room: Other\r\n\
             2021.06.23 13:00:01 Log        -  [Behaviour] Joining wrld_y:2\r\n",
        );
        let entries = watcher.poll(u64::MAX);
        assert_eq!(entries.len(), 1);
        assert!(
            matches!(&entries[0].entry, Some(Entry::Location(l)) if l.world_name == "Other" && l.location == "wrld_y:2")
        );
    }

    #[test]
    fn a_byte_budget_stops_between_lines_and_resumes() {
        let dir = tempfile::tempdir().unwrap();
        let log = dir.path().join("output_log_2025-01-03_00-00-00.txt");
        let line = "2021.06.23 12:02:56 Log        -  [Behaviour] OnPlayerJoined Bob (usr_bob)\n";
        append(&log, &line.repeat(10));
        let mut watcher = LogWatcher::new(dir.path().into(), DateTime::UNIX_EPOCH);
        assert_eq!(watcher.poll(line.len() as u64 * 3).len(), 3);
        assert!(!watcher.caught_up());
        assert_eq!(watcher.poll(u64::MAX).len(), 7);
        assert!(watcher.caught_up());
    }
}
