//! VRChat's PlayerPrefs in `HKCU\SOFTWARE\VRChat\VRChat` (the original's `RegistryPlayerPrefs.cs`).
//! Unity names each value `<key>_h<hash>`, keeps strings as REG_BINARY and floats as 8-byte REG_DWORD
//! values; the JSON here is the original's: {"<key>": {"data": .., "type": 3 | 4 | 100}}, 100 being
//! such a float. The conversions are plain functions; only Windows has the registry itself.

// The value conversions are only called from the Windows registry code (and the tests).
#![cfg_attr(not(windows), allow(dead_code))]

#[cfg(windows)]
use serde_json::Map;
use serde_json::{Value, json};

use super::Answer;
#[cfg(windows)]
use super::blocking;
#[cfg_attr(not(windows), allow(unused_imports))]
use crate::proto::{
    ActionStatus, Capability, Confirm, ReadVrchatRegistryKey, SetVrchatRegistryKey, WriteVrchatRegistry,
};

pub const CAPABILITIES: [Capability; 1] = [Capability::Registry];

pub const REG_BINARY: u32 = 3;
pub const REG_DWORD: u32 = 4;
/// The original's own type number for an 8-byte REG_DWORD holding a double.
pub const FLOAT: i32 = 100;

/// AddHashToKeyName: djb2 with xor over the UTF-16 units, wrapping as a uint.
pub fn hashed_name(key: &str) -> String {
    let hash = key.encode_utf16().fold(5381u32, |h, c| h.wrapping_mul(33) ^ u32::from(c));
    format!("{key}_h{hash}")
}

/// The key a value name belongs to (everything before the last `_h`), as GetVRChatRegistry.
pub fn key_of(name: &str) -> Option<&str> {
    name.rfind("_h").filter(|&i| i > 0).map(|i| &name[..i])
}

/// Encoding.ASCII.GetString: bytes above 127 become '?'.
fn ascii(bytes: &[u8]) -> String {
    bytes.iter().map(|&b| if b < 128 { b as char } else { '?' }).collect()
}

/// A value as the original reads it: (data, type), None for kinds it skips.
pub fn decode(kind: u32, bytes: &[u8]) -> Option<(Value, i32)> {
    match kind {
        REG_BINARY => Some((json!(ascii(bytes)), REG_BINARY as i32)),
        REG_DWORD if bytes.len() > 4 => {
            let raw = <[u8; 8]>::try_from(bytes.get(..8)?).ok()?;
            Some((json!(f64::from_le_bytes(raw)), FLOAT))
        }
        REG_DWORD => {
            let mut raw = [0u8; 4];
            raw[..bytes.len()].copy_from_slice(bytes);
            Some((json!(i32::from_le_bytes(raw)), REG_DWORD as i32))
        }
        _ => None,
    }
}

/// The text .NET's ToString gives a JSON value: a string's content, anything else as written.
fn text_of(value: &Value) -> String {
    match value {
        Value::String(s) => s.clone(),
        other => other.to_string(),
    }
}

/// A value to write as the original's SetVRChatRegistryKey and SetVRChatRegistry would: the
/// registry kind and its bytes.
pub fn encode(kind: i32, value: &Value) -> Result<(u32, Vec<u8>), String> {
    match kind {
        FLOAT => {
            let n = value.as_f64().or_else(|| text_of(value).trim().parse().ok()).ok_or("A float needs a number")?;
            Ok((REG_DWORD, n.to_le_bytes().to_vec()))
        }
        4 => {
            let n = match value {
                Value::Number(n) => n.as_i64().and_then(|n| i32::try_from(n).ok()),
                Value::String(s) => s.trim().parse::<i32>().ok(),
                Value::Bool(b) => Some(i32::from(*b)),
                _ => None,
            }
            .ok_or("A DWORD needs a whole number")?;
            Ok((REG_DWORD, n.to_le_bytes().to_vec()))
        }
        3 => Ok((REG_BINARY, text_of(value).chars().map(|c| if c.is_ascii() { c as u8 } else { b'?' }).collect())),
        other => Err(format!("Unknown type: {other}")),
    }
}

/// A backup as SetVRChatRegistry reads it, checked the way the original's restore-from-file is.
pub fn parse_backup(json: &str) -> Result<Vec<(String, i32, Value)>, String> {
    let Ok(Value::Object(map)) = serde_json::from_str::<Value>(json) else {
        return Err("Not a registry backup".into());
    };
    map.into_iter()
        .map(|(key, entry)| {
            let kind = entry.get("type").and_then(Value::as_i64).and_then(|t| i32::try_from(t).ok());
            match (kind, entry.get("data")) {
                (Some(kind), Some(data)) => Ok((key, kind, data.clone())),
                _ => Err(format!("{key} is not a registry value")),
            }
        })
        .collect()
}

#[cfg(windows)]
mod win {
    use windows_sys::Win32::Foundation::{ERROR_FILE_NOT_FOUND, ERROR_MORE_DATA, ERROR_NO_MORE_ITEMS, ERROR_SUCCESS};
    use windows_sys::Win32::System::Registry::{
        HKEY, HKEY_CURRENT_USER, KEY_READ, KEY_WRITE, REG_OPTION_NON_VOLATILE, RegCloseKey, RegCreateKeyExW,
        RegDeleteTreeW, RegEnumValueW, RegOpenKeyExW, RegQueryValueExW, RegSetValueExW,
    };

    const PATH: &str = r"SOFTWARE\VRChat\VRChat";

    fn wide(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(Some(0)).collect()
    }

    pub struct Key(HKEY);

    impl Drop for Key {
        fn drop(&mut self) {
            // SAFETY: the handle came from RegOpenKeyExW or RegCreateKeyExW.
            unsafe { RegCloseKey(self.0) };
        }
    }

    /// The VRChat key; None when VRChat never ran under this user.
    pub fn open(write: bool) -> std::io::Result<Option<Key>> {
        let mut key: HKEY = std::ptr::null_mut();
        let access = if write { KEY_READ | KEY_WRITE } else { KEY_READ };
        // SAFETY: the path is NUL-terminated; the handle is closed by Key.
        let status = unsafe { RegOpenKeyExW(HKEY_CURRENT_USER, wide(PATH).as_ptr(), 0, access, &mut key) };
        match status {
            ERROR_SUCCESS => Ok(Some(Key(key))),
            ERROR_FILE_NOT_FOUND => Ok(None),
            e => Err(std::io::Error::from_raw_os_error(e as i32)),
        }
    }

    /// CreateVRChatRegistryFolder, then the key open for writing.
    pub fn create() -> std::io::Result<Key> {
        let mut key: HKEY = std::ptr::null_mut();
        // SAFETY: as above.
        let status = unsafe {
            RegCreateKeyExW(
                HKEY_CURRENT_USER,
                wide(PATH).as_ptr(),
                0,
                std::ptr::null(),
                REG_OPTION_NON_VOLATILE,
                KEY_READ | KEY_WRITE,
                std::ptr::null(),
                &mut key,
                std::ptr::null_mut(),
            )
        };
        if status == ERROR_SUCCESS { Ok(Key(key)) } else { Err(std::io::Error::from_raw_os_error(status as i32)) }
    }

    /// DeleteVRChatRegistryFolder; true when there was one.
    pub fn delete() -> std::io::Result<bool> {
        if open(false)?.is_none() {
            return Ok(false);
        }
        // SAFETY: the path is NUL-terminated.
        let status = unsafe { RegDeleteTreeW(HKEY_CURRENT_USER, wide(PATH).as_ptr()) };
        if status == ERROR_SUCCESS { Ok(true) } else { Err(std::io::Error::from_raw_os_error(status as i32)) }
    }

    impl Key {
        /// Every value: (name, kind, bytes).
        pub fn values(&self) -> std::io::Result<Vec<(String, u32, Vec<u8>)>> {
            let mut out = Vec::new();
            let (mut name, mut data) = (vec![0u16; 1024], vec![0u8; 64 * 1024]);
            let mut index = 0;
            loop {
                let (mut name_len, mut data_len, mut kind) = (name.len() as u32, data.len() as u32, 0u32);
                // SAFETY: the buffers are as long as the lengths passed.
                let status = unsafe {
                    RegEnumValueW(
                        self.0,
                        index,
                        name.as_mut_ptr(),
                        &mut name_len,
                        std::ptr::null(),
                        &mut kind,
                        data.as_mut_ptr(),
                        &mut data_len,
                    )
                };
                match status {
                    ERROR_SUCCESS => {
                        out.push((
                            String::from_utf16_lossy(&name[..name_len as usize]),
                            kind,
                            data[..data_len as usize].to_vec(),
                        ));
                        index += 1;
                    }
                    ERROR_MORE_DATA => {
                        // A longer value than the buffer: grow it and read this one again.
                        data.resize(data.len() * 4, 0);
                        name.resize(name.len() * 2, 0);
                    }
                    ERROR_NO_MORE_ITEMS => return Ok(out),
                    e => return Err(std::io::Error::from_raw_os_error(e as i32)),
                }
            }
        }

        pub fn value(&self, name: &str) -> std::io::Result<Option<(u32, Vec<u8>)>> {
            let wname = wide(name);
            let (mut kind, mut len) = (0u32, 0u32);
            // SAFETY: a size query first, then a read into a buffer of that size.
            let status = unsafe {
                RegQueryValueExW(self.0, wname.as_ptr(), std::ptr::null(), &mut kind, std::ptr::null_mut(), &mut len)
            };
            if status == ERROR_FILE_NOT_FOUND {
                return Ok(None);
            }
            if status != ERROR_SUCCESS {
                return Err(std::io::Error::from_raw_os_error(status as i32));
            }
            let mut data = vec![0u8; len as usize];
            let status = unsafe {
                RegQueryValueExW(self.0, wname.as_ptr(), std::ptr::null(), &mut kind, data.as_mut_ptr(), &mut len)
            };
            if status != ERROR_SUCCESS {
                return Err(std::io::Error::from_raw_os_error(status as i32));
            }
            data.truncate(len as usize);
            Ok(Some((kind, data)))
        }

        pub fn set(&self, name: &str, kind: u32, bytes: &[u8]) -> std::io::Result<()> {
            // SAFETY: the name is NUL-terminated and the data as long as passed.
            let status =
                unsafe { RegSetValueExW(self.0, wide(name).as_ptr(), 0, kind, bytes.as_ptr(), bytes.len() as u32) };
            if status == ERROR_SUCCESS { Ok(()) } else { Err(std::io::Error::from_raw_os_error(status as i32)) }
        }
    }
}

#[cfg(windows)]
fn read_all() -> std::io::Result<Option<Map<String, Value>>> {
    let Some(key) = win::open(false)? else { return Ok(None) };
    let mut out = Map::new();
    for (name, kind, bytes) in key.values()? {
        if let (Some(key), Some((data, kind))) = (key_of(&name), decode(kind, &bytes)) {
            out.insert(key.to_owned(), json!({ "data": data, "type": kind }));
        }
    }
    Ok(Some(out))
}

/// GetVRChatRegistry: the whole key as JSON; NOT_FOUND when VRChat has none (HasVRChatRegistryFolder).
pub async fn read() -> Answer {
    #[cfg(windows)]
    return blocking(|| match read_all() {
        Ok(Some(map)) => Answer::json(Value::Object(map)),
        Ok(None) => Answer::status(ActionStatus::NotFound, "VRChat has no registry key on this PC"),
        Err(e) => Answer::failed(e.to_string()),
    })
    .await;
    #[cfg(not(windows))]
    Answer::unsupported()
}

/// SetVRChatRegistry: restores a backup, creating the key if VRChat has none.
pub async fn write(write: WriteVrchatRegistry) -> Answer {
    if !write.confirm {
        return Answer::not_confirmed();
    }
    let entries = match parse_backup(&write.json) {
        Ok(entries) => entries,
        Err(e) => return Answer::failed(e),
    };
    #[cfg(windows)]
    return blocking(move || {
        let key = match win::create() {
            Ok(key) => key,
            Err(e) => return Answer::failed(e.to_string()),
        };
        for (name, kind, data) in entries {
            let written = encode(kind, &data).map_err(|e| format!("{name}: {e}")).and_then(|(kind, bytes)| {
                key.set(&hashed_name(&name), kind, &bytes).map_err(|e| format!("{name}: {e}"))
            });
            if let Err(e) = written {
                return Answer::failed(e);
            }
        }
        Answer::ok()
    })
    .await;
    #[cfg(not(windows))]
    {
        drop(entries);
        Answer::unsupported()
    }
}

/// DeleteVRChatRegistryFolder.
pub async fn delete(confirm: Confirm) -> Answer {
    if !confirm.confirm {
        return Answer::not_confirmed();
    }
    #[cfg(windows)]
    return blocking(|| win::delete().map(|_| Answer::ok()).into()).await;
    #[cfg(not(windows))]
    Answer::unsupported()
}

/// GetVRChatRegistryKey: {"value": <the value or null>}.
pub async fn read_key(read: ReadVrchatRegistryKey) -> Answer {
    #[cfg(windows)]
    return blocking(move || {
        let value = win::open(false).and_then(|key| match key {
            Some(key) => key.value(&hashed_name(&read.key)),
            None => Ok(None),
        });
        match value {
            Ok(value) => Answer::json(
                json!({ "value": value.and_then(|(kind, bytes)| decode(kind, &bytes)).map(|(data, _)| data) }),
            ),
            Err(e) => Answer::failed(e.to_string()),
        }
    })
    .await;
    #[cfg(not(windows))]
    {
        drop(read);
        Answer::unsupported()
    }
}

/// SetVRChatRegistryKey: only into an existing key, as the original (false when VRChat has none).
pub async fn set_key(set: SetVrchatRegistryKey) -> Answer {
    let value: Value = match serde_json::from_str(&set.value_json) {
        Ok(value) => value,
        Err(_) => return Answer::failed("The value is not JSON"),
    };
    let (kind, bytes) = match encode(set.r#type, &value) {
        Ok(encoded) => encoded,
        Err(e) => return Answer::failed(e),
    };
    #[cfg(windows)]
    return blocking(move || match win::open(true) {
        Ok(Some(key)) => key.set(&hashed_name(&set.key), kind, &bytes).map(|()| Answer::ok()).into(),
        Ok(None) => Answer::status(ActionStatus::NotFound, "VRChat has no registry key on this PC"),
        Err(e) => Answer::failed(e.to_string()),
    })
    .await;
    #[cfg(not(windows))]
    {
        drop((kind, bytes, set));
        Answer::unsupported()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn key_names_carry_unitys_hash() {
        // 5381 * 33 ^ 'a' for a one-letter key, as AddHashToKeyName computes it.
        assert_eq!(hashed_name("a"), format!("a_h{}", 5381u32.wrapping_mul(33) ^ 97));
        assert_eq!(key_of(&hashed_name("LOGGING_ENABLED")), Some("LOGGING_ENABLED"));
        assert_eq!(key_of("VRC_INPUT_h_h123"), Some("VRC_INPUT_h"));
        assert_eq!(key_of("_h1"), None);
    }

    #[test]
    fn values_read_as_the_original_does() {
        assert_eq!(decode(REG_BINARY, b"hello\0"), Some((json!("hello\0"), 3)));
        assert_eq!(decode(REG_BINARY, &[0xC3, b'a']), Some((json!("?a"), 3)));
        assert_eq!(decode(REG_DWORD, &1i32.to_le_bytes()), Some((json!(1), 4)));
        assert_eq!(decode(REG_DWORD, &0.75f64.to_le_bytes()), Some((json!(0.75), FLOAT)));
        assert_eq!(decode(11, &[0; 8]), None);
    }

    #[test]
    fn values_write_back_to_the_same_bytes() {
        for (kind, bytes) in [
            (REG_BINARY, b"text\0".to_vec()),
            (REG_DWORD, 7i32.to_le_bytes().to_vec()),
            (REG_DWORD, 1.5f64.to_le_bytes().to_vec()),
        ] {
            let (data, kind_out) = decode(kind, &bytes).unwrap();
            assert_eq!(encode(kind_out, &data).unwrap(), (kind, bytes));
        }
        // The debug-logging fix writes "1" as a DWORD.
        assert_eq!(encode(4, &json!("1")).unwrap(), (REG_DWORD, 1i32.to_le_bytes().to_vec()));
        assert!(encode(4, &json!("x")).is_err());
        assert!(encode(7, &json!(1)).is_err());
    }

    #[test]
    fn a_backup_is_checked_before_anything_is_written() {
        assert_eq!(parse_backup(r#"{"A":{"data":1,"type":4}}"#).unwrap(), vec![("A".to_owned(), 4, json!(1))]);
        assert!(parse_backup(r#"{"A":{"data":1}}"#).is_err());
        assert!(parse_backup("[]").is_err());
        assert!(parse_backup("nope").is_err());
    }
}
