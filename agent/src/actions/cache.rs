//! VRChat's asset bundle cache (the original's `AssetBundleManager.cs`): an entry's size and lock,
//! deleting one, clearing all of it, and sweeping out old versions.
//!
//! VRChat keeps a bundle at `<cache>/<asset id>/<version folder>/__data`, the asset id being the
//! first 16 hex digits of SHA-256(file id + variant) and the version folder 32 hex digits holding
//! the variant version's and the version's little-endian bytes.

use std::fs;
use std::path::{Path, PathBuf};

use serde_json::json;

use super::{Answer, blocking, folders};
use crate::config::Config;
use crate::proto::{ActionStatus, CacheEntry, Capability, Confirm};

pub const CAPABILITIES: [Capability; 1] = [Capability::Cache];

/// GetAssetId.
pub fn asset_id(id: &str, variant: &str) -> String {
    let hash = ring::digest::digest(&ring::digest::SHA256, format!("{id}{variant}").as_bytes());
    hash.as_ref().iter().take(8).map(|b| format!("{b:02X}")).collect()
}

/// GetAssetVersion.
pub fn asset_version(version: i32, variant_version: i32) -> String {
    let hex: String =
        variant_version.to_le_bytes().iter().chain(version.to_le_bytes().iter()).map(|b| format!("{b:02x}")).collect();
    format!("{hex:0>32}")
}

/// ReverseHexToDecimal: (version, variant version) from a version folder's name; (0, 0) when it is not one.
pub fn version_of(folder: &str) -> (i32, i32) {
    if folder.len() != 32 || !folder.is_ascii() {
        return (0, 0);
    }
    let bytes = |s: &str| -> Option<[u8; 4]> {
        let mut out = [0u8; 4];
        for (i, b) in out.iter_mut().enumerate() {
            *b = u8::from_str_radix(s.get(i * 2..i * 2 + 2)?, 16).ok()?;
        }
        Some(out)
    };
    match (bytes(&folder[24..32]), bytes(&folder[0..8])) {
        (Some(v), Some(vv)) => (i32::from_le_bytes(v), i32::from_le_bytes(vv)),
        _ => (0, 0),
    }
}

/// GetVRChatCacheFullLocation: the folder of that version, the highest variant version when VRChat kept several.
pub fn full_location(cache: &Path, id: &str, version: i32, variant: &str, variant_version: i32) -> PathBuf {
    let top = cache.join(asset_id(id, variant));
    let wanted = asset_version(version, variant_version);
    // Any variant version of this version: the folder names end the same.
    let suffix = &wanted[16..];
    let found = fs::read_dir(&top).ok().and_then(|dirs| {
        dirs.filter_map(Result::ok)
            .filter(|d| d.path().is_dir())
            .filter_map(|d| d.file_name().into_string().ok())
            .filter(|name| name.ends_with(suffix))
            .max_by_key(|name| version_of(name).1)
    });
    top.join(found.unwrap_or(wanted))
}

/// CheckVRChatCache: {"size": bytes or -1, "locked": bool, "path": the folder or ""}.
pub fn check(cache: &Path, entry: &CacheEntry) -> serde_json::Value {
    let mut folder = full_location(cache, &entry.id, entry.version, "", 0);
    if !folder.is_dir() {
        folder = full_location(cache, &entry.id, entry.version, &entry.variant, entry.variant_version);
    }
    let data = fs::metadata(folder.join("__data")).ok();
    json!({
        "size": data.as_ref().map_or(-1, |m| m.len() as i64),
        "locked": folder.join("__lock").exists(),
        "path": if data.is_some() { folder.display().to_string() } else { String::new() },
    })
}

/// DeleteCache: the plain and the variant folder of that version.
pub fn delete(cache: &Path, entry: &CacheEntry) -> std::io::Result<()> {
    for folder in [
        full_location(cache, &entry.id, entry.version, "", 0),
        full_location(cache, &entry.id, entry.version, &entry.variant, entry.variant_version),
    ] {
        if folder.is_dir() {
            fs::remove_dir_all(folder)?;
        }
    }
    Ok(())
}

fn is_empty(dir: &Path) -> bool {
    fs::read_dir(dir).is_ok_and(|mut d| d.next().is_none())
}

/// SweepCache: for every asset, all but its newest version (by write time) unless locked, and empty
/// folders. The removed ones as "<asset>\<version>".
pub fn sweep(cache: &Path) -> std::io::Result<Vec<String>> {
    let mut removed = Vec::new();
    let Ok(assets) = fs::read_dir(cache) else { return Ok(removed) };
    for asset in assets.filter_map(Result::ok).filter(|d| d.path().is_dir()) {
        let mut versions: Vec<_> = fs::read_dir(asset.path())?
            .filter_map(Result::ok)
            .filter(|d| d.path().is_dir())
            .map(|d| (d.metadata().and_then(|m| m.modified()).ok(), d))
            .collect();
        versions.sort_by_key(|(modified, _)| *modified);
        let last = versions.len().saturating_sub(1);
        for (index, (_, version)) in versions.iter().enumerate() {
            let path = version.path();
            if is_empty(&path) {
                fs::remove_dir(&path)?;
                continue;
            }
            if index == last || path.join("__lock").exists() {
                continue;
            }
            fs::remove_dir_all(&path)?;
            removed.push(format!("{}\\{}", asset.file_name().to_string_lossy(), version.file_name().to_string_lossy()));
        }
        if is_empty(&asset.path()) {
            fs::remove_dir(asset.path())?;
        }
    }
    Ok(removed)
}

/// GetCacheSize: every file's length under the cache folder.
pub fn size(dir: &Path) -> u64 {
    let Ok(entries) = fs::read_dir(dir) else { return 0 };
    entries
        .filter_map(Result::ok)
        .map(|e| match e.file_type() {
            Ok(t) if t.is_dir() => size(&e.path()),
            Ok(_) => e.metadata().map_or(0, |m| m.len()),
            Err(_) => 0,
        })
        .sum()
}

fn cache_dir(config: &Config) -> Option<PathBuf> {
    folders::cache(config)
}

fn not_known() -> Answer {
    Answer::status(ActionStatus::NotFound, "VRChat's cache folder is not known on this PC")
}

pub async fn info(config: &Config) -> Answer {
    let config = config.clone();
    blocking(move || match cache_dir(&config) {
        Some(dir) => Answer::json(json!({ "size": size(&dir), "location": dir.display().to_string() })),
        None => not_known(),
    })
    .await
}

pub async fn check_entry(entry: CacheEntry, config: &Config) -> Answer {
    let config = config.clone();
    blocking(move || match cache_dir(&config) {
        Some(dir) => Answer::json(check(&dir, &entry)),
        None => Answer::json(json!({ "size": -1, "locked": false, "path": "" })),
    })
    .await
}

pub async fn delete_entry(entry: CacheEntry, config: &Config) -> Answer {
    let config = config.clone();
    blocking(move || match cache_dir(&config) {
        Some(dir) => delete(&dir, &entry).map(|()| Answer::ok()).into(),
        None => not_known(),
    })
    .await
}

/// DeleteAllCache: the cache folder emptied, the folder itself kept.
pub async fn clear(confirm: Confirm, config: &Config) -> Answer {
    if !confirm.confirm {
        return Answer::not_confirmed();
    }
    let config = config.clone();
    blocking(move || match cache_dir(&config) {
        Some(dir) => fs::remove_dir_all(&dir).and_then(|()| fs::create_dir_all(&dir)).map(|()| Answer::ok()).into(),
        None => not_known(),
    })
    .await
}

pub async fn sweep_cache(config: &Config) -> Answer {
    let config = config.clone();
    blocking(move || match cache_dir(&config) {
        Some(dir) => match sweep(&dir) {
            Ok(removed) => Answer::json(json!(removed)),
            Err(e) => Answer::failed(e.to_string()),
        },
        None => Answer::json(json!([])),
    })
    .await
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn names_are_vrchats() {
        assert_eq!(asset_id("file_x", "security").len(), 16);
        assert!(asset_id("file_x", "").chars().all(|c| c.is_ascii_hexdigit() && !c.is_ascii_lowercase()));
        // variant version 2, version 5: 02000000 then 05000000, padded to 32.
        assert_eq!(asset_version(5, 2), "00000000000000000200000005000000");
        // ReverseHexToDecimal reads the variant version from the first 8 digits, which GetAssetVersion
        // pads with zeros, so for VRChat's own folder names it is always 0.
        assert_eq!(version_of("00000000000000000200000005000000"), (5, 0));
        // The original reads the variant version from the first 8 and the version from the last 8 digits.
        assert_eq!(version_of("02000000000000000000000005000000"), (5, 2));
        assert_eq!(version_of("short"), (0, 0));
    }

    #[test]
    fn check_delete_and_sweep_work_on_a_cache_folder() {
        let dir = tempfile::tempdir().unwrap();
        let cache = dir.path();
        let entry = CacheEntry { id: "file_a".into(), version: 3, variant: "security".into(), variant_version: 0 };
        let folder = cache.join(asset_id("file_a", "")).join(asset_version(3, 0));
        fs::create_dir_all(&folder).unwrap();
        fs::write(folder.join("__data"), [0u8; 10]).unwrap();
        let found = check(cache, &entry);
        assert_eq!(found["size"], json!(10));
        assert_eq!(found["locked"], json!(false));
        assert_eq!(size(cache), 10);

        // An older version and a locked one: the sweep keeps the newest and the locked.
        let asset = cache.join(asset_id("file_a", ""));
        for (name, lock) in [(asset_version(1, 0), false), (asset_version(2, 0), true)] {
            fs::create_dir_all(asset.join(&name)).unwrap();
            fs::write(asset.join(&name).join("__data"), [0u8; 1]).unwrap();
            if lock {
                fs::write(asset.join(&name).join("__lock"), []).unwrap();
            }
            std::thread::sleep(std::time::Duration::from_millis(20));
        }
        let removed = sweep(cache).unwrap();
        assert!(removed.iter().any(|r| r.ends_with(&asset_version(1, 0))));
        assert!(asset.join(asset_version(2, 0)).is_dir());

        delete(cache, &entry).unwrap();
        assert_eq!(check(cache, &entry)["size"], json!(-1));
    }
}
