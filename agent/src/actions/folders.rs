//! Where VRChat keeps its files (the original's `Folders.cs`), opening them in the file manager
//! (`Open*Folder`, `OpenFolderAndSelectItem`) and the native pickers (`OpenFolderSelectorDialog`,
//! `OpenFileSelectorDialog`). The server names a folder, never a path.

use std::path::{Component, Path, PathBuf};

use serde_json::{Value, json};

use super::{Answer, blocking};
use crate::config::Config;
use crate::platform;
use crate::proto::{
    ActionStatus, AgentFolders, Capability, DeleteScreenshotMetadata, Folder, OpenFolder, PickPath, PickPurpose,
};
use crate::screenshot;
use crate::settings::Settings;

pub const CAPABILITIES: [Capability; 2] = [Capability::Folders, Capability::PickPath];

/// VRChat's own folder (`%LocalAppData%Low\VRChat\VRChat`), where its logs and config.json are.
pub fn app_data(config: &Config) -> Option<PathBuf> {
    config.log_dir()
}

/// VRChat's config.json, parsed, if there is one.
pub fn vrchat_config(config: &Config) -> Option<Value> {
    let text = std::fs::read_to_string(app_data(config)?.join("config.json")).ok()?;
    serde_json::from_str(&text).ok()
}

fn config_dir(config: &Config, key: &str) -> Option<PathBuf> {
    let dir = vrchat_config(config)?.get(key)?.as_str()?.trim().to_owned();
    (!dir.is_empty() && Path::new(&dir).is_dir()).then(|| dir.into())
}

/// `cache_directory` from config.json when that folder exists, else the default, plus Cache-WindowsPlayer.
pub fn cache(config: &Config) -> Option<PathBuf> {
    let base = config_dir(config, "cache_directory").or_else(|| app_data(config))?;
    Some(base.join("Cache-WindowsPlayer"))
}

/// `picture_output_folder` from config.json when it exists, else Pictures\VRChat.
pub fn photos(config: &Config) -> Option<PathBuf> {
    config_dir(config, "picture_output_folder").or_else(|| platform::pictures_dir().map(|p| p.join("VRChat")))
}

/// The UGC override when set (made if missing, as the original does), else the photos folder.
pub fn ugc_photos(config: &Config, settings: &Settings) -> Option<PathBuf> {
    let custom = settings.ugc_folder.trim();
    if !custom.is_empty() && std::fs::create_dir_all(custom).is_ok() {
        return Some(custom.into());
    }
    photos(config)
}

/// Steam's screenshot folder for VRChat: the most recently written one over Steam's user folders.
pub fn screenshots() -> Option<PathBuf> {
    let userdata = platform::steam_install_dir()?.join("userdata");
    std::fs::read_dir(userdata)
        .ok()?
        .filter_map(Result::ok)
        .map(|user| ["760", "remote", "438100", "screenshots"].iter().fold(user.path(), |p, c| p.join(c)))
        .filter_map(|dir| Some((std::fs::metadata(&dir).ok()?.modified().ok()?, dir)))
        .max_by_key(|(written, _)| *written)
        .map(|(_, dir)| dir)
}

pub fn crash_dumps() -> PathBuf {
    std::env::temp_dir().join("VRChat").join("VRChat").join("Crashes")
}

/// The app launcher's shortcut folder (the original's AppShortcutDirectory).
pub fn shortcuts() -> PathBuf {
    platform::data_dir().join("startup")
}

fn display(path: Option<PathBuf>) -> String {
    path.map(|p| p.display().to_string()).unwrap_or_default()
}

/// For the hello.
pub fn report(config: &Config) -> AgentFolders {
    let settings = Settings::load();
    AgentFolders {
        app_data: display(app_data(config)),
        photos: display(photos(config)),
        screenshots: display(screenshots()),
        cache: display(cache(config)),
        ugc_photos: display(ugc_photos(config, &settings)),
        crash_dumps: display(Some(crash_dumps())),
    }
}

pub fn resolve(folder: Folder, config: &Config) -> Option<PathBuf> {
    match folder {
        Folder::Unspecified => None,
        Folder::Photos => photos(config),
        Folder::Screenshots => screenshots(),
        Folder::AppData => app_data(config),
        Folder::UgcPhotos => ugc_photos(config, &Settings::load()),
        Folder::CrashDumps => Some(crash_dumps()),
        Folder::AgentData => Some(platform::data_dir()),
        // OpenShortcutFolder: made with its desktop and vr folders first.
        Folder::Shortcuts => Some(crate::launcher::folders().0),
        Folder::Cache => cache(config),
    }
}

/// `relative` under `base`, refused when it climbs out or is absolute.
pub fn inside(base: &Path, relative: &str) -> Option<PathBuf> {
    let relative = Path::new(relative);
    if relative.components().any(|c| !matches!(c, Component::Normal(_) | Component::CurDir)) {
        return None;
    }
    Some(base.join(relative))
}

pub async fn open(open: OpenFolder, config: &Config) -> Answer {
    let folder = open.folder();
    let config = config.clone();
    blocking(move || {
        let Some(base) = resolve(folder, &config).filter(|p| p.exists()) else {
            return Answer::status(ActionStatus::NotFound, "That folder is not on this PC");
        };
        if open.relative_path.is_empty() {
            return platform::open_folder(&base, None).map(|()| Answer::ok()).into();
        }
        match inside(&base, &open.relative_path).filter(|p| p.exists()) {
            Some(item) => platform::open_folder(&base, Some(&item)).map(|()| Answer::ok()).into(),
            None => Answer::status(ActionStatus::NotFound, "That file is not in the folder"),
        }
    })
    .await
}

/// Shows the picker on the PC and keeps the choice as the agent's setting for that purpose.
pub async fn pick(pick: PickPath, config: &Config) -> Answer {
    let purpose = pick.purpose();
    let start = pick.start.clone();
    let config = config.clone();
    blocking(move || {
        let chosen = match purpose {
            PickPurpose::VrchatPath => rfd::FileDialog::new()
                .set_title("VRChat's launch.exe")
                .add_filter("launch.exe", &["exe"])
                .set_directory(config.vrc_launch_path_override.trim())
                .pick_file(),
            PickPurpose::UgcFolder => rfd::FileDialog::new()
                .set_title("Folder for saved prints, stickers and emoji")
                .set_directory(display(ugc_photos(&config, &Settings::load())))
                .pick_folder(),
            PickPurpose::Folder => {
                let start = start.trim();
                let dialog = rfd::FileDialog::new().set_title("Choose a folder");
                if start.is_empty() { dialog } else { dialog.set_directory(start) }.pick_folder()
            }
            PickPurpose::ShortcutFolder | PickPurpose::Unspecified => return Answer::unsupported(),
        };
        let Some(path) = chosen else { return Answer::json(json!({ "path": "" })) };
        // The launch path is this PC's own setting; the UGC folder is the account's, which the web
        // client saves and the server sends back as AgentSettings.
        if purpose == PickPurpose::VrchatPath {
            if crate::game::launch_exe_from_path(&path.to_string_lossy()).is_none() {
                return Answer::failed("That is not VRChat's launch.exe");
            }
            let mut config = Config::load();
            config.vrc_launch_path_override = path.display().to_string();
            if let Err(e) = config.save() {
                return Answer::failed(format!("Could not keep the choice: {e}"));
            }
        }
        Answer::json(json!({ "path": path.display().to_string() }))
    })
    .await
}

/// DeleteScreenshotMetadata for one picture in the photos folder, or with `all` (confirmed)
/// DeleteAllScreenshotMetadata over every .png in it. Answers {"changed": n}.
pub async fn delete_screenshot_metadata(delete: DeleteScreenshotMetadata, config: &Config) -> Answer {
    if delete.all && !delete.confirm {
        return Answer::not_confirmed();
    }
    let config = config.clone();
    blocking(move || {
        let Some(base) = photos(&config).filter(|p| p.is_dir()) else {
            return Answer::status(ActionStatus::NotFound, "VRChat's photos folder is not on this PC");
        };
        let files = if delete.all {
            screenshot::pngs_under(&base)
        } else {
            match inside(&base, &delete.relative_path)
                .filter(|p| p.is_file() && p.extension().is_some_and(|e| e == "png"))
            {
                Some(file) => vec![file],
                None => return Answer::status(ActionStatus::NotFound, "That picture is not in the photos folder"),
            }
        };
        let mut changed = 0;
        for file in &files {
            match screenshot::delete_metadata(file) {
                Ok(true) => changed += 1,
                Ok(false) => {}
                Err(e) => tracing::warn!("could not take the metadata out of {}: {e}", file.display()),
            }
        }
        Answer::json(json!({ "changed": changed }))
    })
    .await
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn relative_paths_stay_inside_the_folder() {
        let base = Path::new("base");
        assert_eq!(inside(base, "2025-01/VRChat_a.png"), Some(base.join("2025-01/VRChat_a.png")));
        assert_eq!(inside(base, "./a.png"), Some(base.join("./a.png")));
        assert!(inside(base, "../secret").is_none());
        assert!(inside(base, "a/../../b").is_none());
        assert!(inside(base, "/etc/passwd").is_none());
        #[cfg(windows)]
        assert!(inside(base, r"C:\Windows").is_none());
    }

    #[test]
    fn config_json_moves_the_cache_and_photos() {
        let dir = tempfile::tempdir().unwrap();
        let app = dir.path().join("VRChat");
        let cache_dir = dir.path().join("cachehere");
        let photos_dir = dir.path().join("photoshere");
        for d in [&app, &cache_dir, &photos_dir] {
            std::fs::create_dir_all(d).unwrap();
        }
        let config = Config { vrchat_log_dir: app.display().to_string(), ..Default::default() };
        assert_eq!(cache(&config), Some(app.join("Cache-WindowsPlayer")));
        std::fs::write(
            app.join("config.json"),
            json!({ "cache_directory": cache_dir, "picture_output_folder": photos_dir }).to_string(),
        )
        .unwrap();
        assert_eq!(cache(&config), Some(cache_dir.join("Cache-WindowsPlayer")));
        assert_eq!(photos(&config), Some(photos_dir.clone()));
        // A folder that does not exist is ignored, as the original does.
        std::fs::write(app.join("config.json"), json!({ "cache_directory": dir.path().join("gone") }).to_string())
            .unwrap();
        assert_eq!(cache(&config), Some(app.join("Cache-WindowsPlayer")));
    }
}
