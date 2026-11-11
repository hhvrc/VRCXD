//! VRChat's own files in its app data folder: config.json (the original's `VrcConfigFile.cs`) and the
//! signed-in user's local show/hide avatar list (`LocalPlayerModerations.cs`).

use std::path::PathBuf;

use serde_json::{Map, Value, json};

use super::{Answer, blocking, folders};
use crate::config::Config;
use crate::proto::{ActionStatus, Capability, ReadLocalModerations, SetLocalModeration, WriteVrchatConfig};

pub const CAPABILITIES: [Capability; 2] = [Capability::VrchatConfig, Capability::LocalModerations];

fn config_file(config: &Config) -> Option<PathBuf> {
    Some(folders::app_data(config)?.join("config.json"))
}

/// ReadConfigFile: {"exists": bool, "json": "<the file as it is>"}.
pub async fn read_config(config: &Config) -> Answer {
    let config = config.clone();
    blocking(move || match config_file(&config).map(std::fs::read_to_string) {
        Some(Ok(text)) => Answer::json(json!({ "exists": true, "json": text })),
        Some(Err(e)) if e.kind() == std::io::ErrorKind::NotFound => {
            Answer::json(json!({ "exists": false, "json": "" }))
        }
        Some(Err(e)) => Answer::failed(format!("Could not read config.json: {e}")),
        None => Answer::status(ActionStatus::NotFound, "VRChat's folder is not known on this PC"),
    })
    .await
}

/// WriteConfigFile, only with a JSON object (the original's dialog only ever writes one).
pub async fn write_config(write: WriteVrchatConfig, config: &Config) -> Answer {
    if !matches!(serde_json::from_str::<Value>(&write.json), Ok(Value::Object(_))) {
        return Answer::failed("config.json must be a JSON object");
    }
    let config = config.clone();
    blocking(move || {
        let Some(path) = config_file(&config) else {
            return Answer::status(ActionStatus::NotFound, "VRChat's folder is not known on this PC");
        };
        let written =
            path.parent().map_or(Ok(()), std::fs::create_dir_all).and_then(|()| std::fs::write(&path, &write.json));
        written.map(|()| Answer::ok()).into()
    })
    .await
}

/// A user id as VRChat names it: nothing that could leave the folder.
fn is_user_id(id: &str) -> bool {
    !id.is_empty() && id.chars().all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-')
}

fn moderations_file(config: &Config, current_user_id: &str) -> Option<PathBuf> {
    is_user_id(current_user_id).then_some(())?;
    Some(
        folders::app_data(config)?
            .join("LocalPlayerModerations")
            .join(format!("{current_user_id}-show-hide-user.vrcset")),
    )
}

/// GetVRChatModerations' parsing: "<user id><spaces><type, 3 digits>" per line.
pub fn parse_moderations(text: &str) -> Map<String, Value> {
    let mut out = Map::new();
    for line in text.lines() {
        let Some(space) = line.find(' ').filter(|&i| i > 0) else { continue };
        let Some(kind) = line.get(line.len().saturating_sub(3)..).and_then(|t| t.parse::<i16>().ok()) else { continue };
        out.insert(line[..space].to_owned(), json!(kind));
    }
    out
}

/// SetVRChatUserModeration's rewrite: drops the first line starting with the user id, and adds
/// "<user id padded to 64><type as 3 digits>" unless the type is 0.
pub fn set_moderation(text: &str, user_id: &str, kind: i32) -> String {
    let mut lines: Vec<String> = text.lines().map(str::to_owned).collect();
    if let Some(i) = lines.iter().position(|l| l.starts_with(user_id)) {
        lines.remove(i);
    }
    if kind != 0 {
        lines.push(format!("{user_id:<64}{kind:03}"));
    }
    // File.WriteAllLines ends every line, the last one too.
    lines.iter().map(|l| format!("{l}{}", if cfg!(windows) { "\r\n" } else { "\n" })).collect()
}

/// GetVRChatModerations: {"<user id>": type}; null when VRChat has no list for that user.
pub async fn read_moderations(read: ReadLocalModerations, config: &Config) -> Answer {
    let config = config.clone();
    blocking(move || {
        let Some(path) = moderations_file(&config, &read.current_user_id) else {
            return Answer::failed("Not a user id");
        };
        match std::fs::read_to_string(path) {
            Ok(text) => Answer::json(Value::Object(parse_moderations(&text))),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Answer::json(Value::Null),
            Err(e) => Answer::failed(e.to_string()),
        }
    })
    .await
}

/// SetVRChatUserModeration: only into a list VRChat already made, as the original.
pub async fn write_moderation(set: SetLocalModeration, config: &Config) -> Answer {
    if !is_user_id(&set.user_id) || !(0..=999).contains(&set.r#type) {
        return Answer::failed("Not a user id or moderation type");
    }
    let config = config.clone();
    blocking(move || {
        let Some(path) = moderations_file(&config, &set.current_user_id) else {
            return Answer::failed("Not a user id");
        };
        let text = match std::fs::read_to_string(&path) {
            Ok(text) => text,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
                return Answer::status(ActionStatus::NotFound, "VRChat has no local moderation list for this user yet");
            }
            Err(e) => return Answer::failed(e.to_string()),
        };
        std::fs::write(&path, set_moderation(&text, &set.user_id, set.r#type)).map(|()| Answer::ok()).into()
    })
    .await
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn moderation_lines_read_and_rewrite_as_the_original() {
        let text = format!("{:<64}004\n{:<64}005\nbroken\n", "usr_a", "usr_b");
        let parsed = parse_moderations(&text);
        assert_eq!(parsed.get("usr_a"), Some(&json!(4)));
        assert_eq!(parsed.get("usr_b"), Some(&json!(5)));
        assert_eq!(parsed.len(), 2);

        let changed = set_moderation(&text, "usr_a", 5);
        let parsed = parse_moderations(&changed);
        assert_eq!(parsed.get("usr_a"), Some(&json!(5)));
        assert!(changed.lines().last().unwrap().starts_with("usr_a "));
        assert_eq!(changed.lines().last().unwrap().len(), 67);

        let removed = set_moderation(&text, "usr_b", 0);
        assert!(!parse_moderations(&removed).contains_key("usr_b"));
    }

    #[test]
    fn only_user_ids_name_a_file() {
        assert!(is_user_id("usr_c1644b5b-3ca4-45b4-97c6-a2a0de70d469"));
        assert!(!is_user_id("../x"));
        assert!(!is_user_id(""));
    }

    #[tokio::test]
    async fn config_json_reads_and_only_objects_are_written() {
        let dir = tempfile::tempdir().unwrap();
        let config = Config { vrchat_log_dir: dir.path().display().to_string(), ..Default::default() };
        let missing = read_config(&config).await;
        assert_eq!(serde_json::from_str::<Value>(&missing.json).unwrap()["exists"], json!(false));

        assert_eq!(write_config(WriteVrchatConfig { json: "[1]".into() }, &config).await.status, ActionStatus::Failed);
        let body = "{\n  \"cache_size\": 30\n}";
        assert_eq!(write_config(WriteVrchatConfig { json: body.into() }, &config).await.status, ActionStatus::Ok);
        let read = serde_json::from_str::<Value>(&read_config(&config).await.json).unwrap();
        assert_eq!(read["json"], json!(body));
    }
}
