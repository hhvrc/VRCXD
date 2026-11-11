//! VRChat's PlayerPrefs in `HKCU\SOFTWARE\VRChat\VRChat` (the original's `RegistryPlayerPrefs.cs`).
//! Unity names each value `<key>_h<hash>`, keeps strings as REG_BINARY and floats as 8-byte REG_DWORD
//! values; the JSON here is the original's: {"<key>": {"data": .., "type": 3 | 4 | 100}}, 100 being
//! such a float. The conversions are plain functions; Windows has the registry itself, and on Linux
//! the key is a section of the Proton prefix's `user.reg`.

use serde_json::{Map, Value, json};

use super::{Answer, blocking};
use crate::proto::{
    ActionStatus, Capability, Confirm, ReadVrchatRegistryKey, SetVrchatRegistryKey, WriteVrchatRegistry,
};

#[cfg(windows)]
use win as store;
#[cfg(not(windows))]
use wine as store;

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

        /// Registry writes land as they are made.
        pub fn save(&self) -> std::io::Result<()> {
            Ok(())
        }

        pub fn set(&self, name: &str, kind: u32, bytes: &[u8]) -> std::io::Result<()> {
            // SAFETY: the name is NUL-terminated and the data as long as passed.
            let status =
                unsafe { RegSetValueExW(self.0, wide(name).as_ptr(), 0, kind, bytes.as_ptr(), bytes.len() as u32) };
            if status == ERROR_SUCCESS { Ok(()) } else { Err(std::io::Error::from_raw_os_error(status as i32)) }
        }
    }
}

/// VRChat under Proton: the `[Software\\VRChat\\VRChat]` section of the prefix's `user.reg`.
/// wineserver loads the file when the prefix starts and writes it back while it runs, so the
/// file is read as last saved and written only while VRChat is closed.
#[cfg(any(not(windows), test))]
// On Windows only its parser is built, for the tests.
#[cfg_attr(windows, allow(dead_code))]
mod wine {
    use std::cell::RefCell;
    use std::io;
    use std::path::{Path, PathBuf};

    /// The section's name as `user.reg` writes it, backslashes escaped.
    const SECTION: &str = r"Software\\VRChat\\VRChat";

    /// The prefix's `user.reg`: beside the configured log folder's `drive_c`, else VRChat's
    /// compatdata in any Steam library.
    fn user_reg() -> Option<PathBuf> {
        let from_logs = crate::config::Config::load().log_dir().and_then(|dir| {
            let drive_c = dir.ancestors().find(|a| a.file_name().is_some_and(|n| n == "drive_c"))?;
            Some(drive_c.parent()?.join("user.reg"))
        });
        let steam = crate::platform::steam_install_dir();
        let libraries = steam.iter().flat_map(|s| crate::launcher::steam_libraries(s));
        let from_steam =
            libraries.map(|l| l.join("compatdata").join(crate::game::VRCHAT_STEAM_APP_ID).join("pfx/user.reg"));
        from_logs.into_iter().chain(from_steam).find(|p| p.is_file())
    }

    pub struct Key {
        path: PathBuf,
        file: RefCell<File>,
    }

    /// `user.reg` around VRChat's section: the section's header and `#` lines are kept as they
    /// were, its values as (name, the text after `=`).
    #[derive(Debug, Default, Clone, PartialEq)]
    pub struct File {
        before: String,
        header: Vec<String>,
        values: Vec<(String, String)>,
        after: String,
        found: bool,
    }

    fn is_header(line: &str) -> bool {
        line.strip_prefix('[')
            .and_then(|l| l.split_once(']'))
            .is_some_and(|(name, _)| name.eq_ignore_ascii_case(SECTION))
    }

    /// A quoted name from the start of `text` and what follows it.
    fn unquote(text: &str) -> Option<(String, &str)> {
        let mut chars = text.strip_prefix('"')?.char_indices();
        let mut out = String::new();
        while let Some((i, c)) = chars.next() {
            match c {
                '"' => return Some((out, &text[i + 2..])),
                '\\' => match chars.next()?.1 {
                    'n' => out.push('\n'),
                    'r' => out.push('\r'),
                    't' => out.push('\t'),
                    '0' => out.push('\0'),
                    'x' => {
                        let rest = &text[i + 3..];
                        let len = rest.chars().take(4).take_while(char::is_ascii_hexdigit).count();
                        out.push(char::from_u32(u32::from_str_radix(&rest[..len], 16).ok()?)?);
                        for _ in 0..len {
                            chars.next();
                        }
                    }
                    other => out.push(other),
                },
                c => out.push(c),
            }
        }
        None
    }

    fn quote(name: &str) -> String {
        let mut out = String::from('"');
        for c in name.chars() {
            match c {
                '"' | '\\' => {
                    out.push('\\');
                    out.push(c);
                }
                ' '..='~' => out.push(c),
                c => {
                    for unit in c.encode_utf16(&mut [0; 2]) {
                        out.push_str(&format!("\\x{unit:04x}"));
                    }
                }
            }
        }
        out.push('"');
        out
    }

    impl File {
        pub fn parse(text: &str) -> Self {
            let mut file = File::default();
            let mut lines = text.split_inclusive('\n').peekable();
            for line in lines.by_ref() {
                if is_header(line) {
                    file.found = true;
                    file.header.push(line.trim_end().to_owned());
                    break;
                }
                file.before.push_str(line);
            }
            if !file.found {
                return file;
            }
            while let Some(line) = lines.next_if(|l| !l.starts_with('[')) {
                let mut line = line.trim_end().to_owned();
                // A value continues on the next line after a trailing backslash.
                while line.ends_with('\\') && lines.peek().is_some_and(|l| !l.starts_with('[')) {
                    line.pop();
                    line.push_str(lines.next().unwrap_or_default().trim());
                }
                if line.starts_with('#') {
                    file.header.push(line);
                } else if let Some((name, data)) =
                    unquote(&line).and_then(|(n, rest)| Some((n, rest.strip_prefix('=')?)))
                {
                    file.values.push((name, data.to_owned()));
                }
            }
            file.after = lines.collect();
            file
        }

        pub fn render(&self) -> String {
            let mut out = self.before.clone();
            if self.found {
                for line in &self.header {
                    out.push_str(line);
                    out.push('\n');
                }
                for (name, data) in &self.values {
                    out.push_str(&format!("{}={data}\n", quote(name)));
                }
                out.push('\n');
            }
            out.push_str(&self.after);
            out
        }

        /// Adds VRChat's section, stamped now as wineserver stamps it.
        pub fn add_section(&mut self) {
            if self.found {
                return;
            }
            let now = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default();
            // FILETIME: 100 ns since 1601.
            let filetime = (now.as_secs() + 11_644_473_600) * 10_000_000 + u64::from(now.subsec_nanos() / 100);
            if !self.before.is_empty() && !self.before.ends_with("\n\n") {
                self.before.push('\n');
            }
            self.header = vec![format!("[{SECTION}] {}", now.as_secs()), format!("#time={filetime:x}")];
            self.found = true;
        }

        pub fn remove_section(&mut self) {
            *self = File { before: std::mem::take(&mut self.before) + &self.after, ..File::default() };
        }

        pub fn values(&self) -> Vec<(String, u32, Vec<u8>)> {
            self.values.iter().filter_map(|(n, d)| decode(d).map(|(k, b)| (n.clone(), k, b))).collect()
        }

        pub fn value(&self, name: &str) -> Option<(u32, Vec<u8>)> {
            self.values.iter().find(|(n, _)| n.eq_ignore_ascii_case(name)).and_then(|(_, d)| decode(d))
        }

        pub fn set(&mut self, name: &str, kind: u32, bytes: &[u8]) {
            let data = encode(kind, bytes);
            match self.values.iter_mut().find(|(n, _)| n.eq_ignore_ascii_case(name)) {
                Some(entry) => entry.1 = data,
                None => self.values.push((name.to_owned(), data)),
            }
        }
    }

    /// A value's text as (kind, bytes): `dword:`, `hex:` and `hex(<kind>):`; strings are not
    /// read, as VRChat writes none.
    fn decode(data: &str) -> Option<(u32, Vec<u8>)> {
        if let Some(hex) = data.strip_prefix("dword:") {
            return Some((super::REG_DWORD, u32::from_str_radix(hex.trim(), 16).ok()?.to_le_bytes().to_vec()));
        }
        let (kind, bytes) = if let Some(bytes) = data.strip_prefix("hex:") {
            (super::REG_BINARY, bytes)
        } else {
            let (kind, bytes) = data.strip_prefix("hex(")?.split_once("):")?;
            (u32::from_str_radix(kind, 16).ok()?, bytes)
        };
        let bytes = bytes.split(',').map(str::trim).filter(|b| !b.is_empty());
        Some((kind, bytes.map(|b| u8::from_str_radix(b, 16).ok()).collect::<Option<_>>()?))
    }

    fn encode(kind: u32, bytes: &[u8]) -> String {
        if kind == super::REG_DWORD && bytes.len() == 4 {
            return format!("dword:{:08x}", u32::from_le_bytes(bytes.try_into().unwrap_or_default()));
        }
        let hex: Vec<String> = bytes.iter().map(|b| format!("{b:02x}")).collect();
        let prefix = if kind == super::REG_BINARY { "hex:".to_owned() } else { format!("hex({kind:x}):") };
        prefix + &hex.join(",")
    }

    fn load(path: &Path) -> io::Result<File> {
        Ok(File::parse(&std::fs::read_to_string(path)?))
    }

    fn no_prefix() -> io::Error {
        io::Error::new(io::ErrorKind::NotFound, "no Proton prefix for VRChat on this PC")
    }

    /// The VRChat key; None when there is no prefix or VRChat never ran in it.
    pub fn open(_write: bool) -> io::Result<Option<Key>> {
        let Some(path) = user_reg() else { return Ok(None) };
        let file = load(&path)?;
        Ok(file.found.then(|| Key { path, file: RefCell::new(file) }))
    }

    pub fn create() -> io::Result<Key> {
        let path = user_reg().ok_or_else(no_prefix)?;
        let mut file = load(&path)?;
        file.add_section();
        Ok(Key { path, file: RefCell::new(file) })
    }

    pub fn delete() -> io::Result<bool> {
        let Some(key) = open(true)? else { return Ok(false) };
        key.file.borrow_mut().remove_section();
        key.save().map(|()| true)
    }

    impl Key {
        pub fn values(&self) -> io::Result<Vec<(String, u32, Vec<u8>)>> {
            Ok(self.file.borrow().values())
        }

        pub fn value(&self, name: &str) -> io::Result<Option<(u32, Vec<u8>)>> {
            Ok(self.file.borrow().value(name))
        }

        pub fn set(&self, name: &str, kind: u32, bytes: &[u8]) -> io::Result<()> {
            self.file.borrow_mut().set(name, kind, bytes);
            Ok(())
        }

        /// Writes the file back through a temporary file beside it.
        pub fn save(&self) -> io::Result<()> {
            let temp = self.path.with_extension("reg.vrcxd");
            std::fs::write(&temp, self.file.borrow().render())?;
            std::fs::rename(&temp, &self.path)
        }
    }
}

/// Under Proton, wineserver writes the registry back while VRChat runs, which would undo a write.
fn refuse_write() -> Option<Answer> {
    (!cfg!(windows) && !crate::platform::processes_named("VRChat.exe").is_empty()).then(|| {
        Answer::status(ActionStatus::GameRunning, "Close VRChat first: Proton writes its registry back as it runs")
    })
}

fn read_all() -> std::io::Result<Option<Map<String, Value>>> {
    let Some(key) = store::open(false)? else { return Ok(None) };
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
    blocking(|| match read_all() {
        Ok(Some(map)) => Answer::json(Value::Object(map)),
        Ok(None) => Answer::status(ActionStatus::NotFound, "VRChat has no registry key on this PC"),
        Err(e) => Answer::failed(e.to_string()),
    })
    .await
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
    blocking(move || {
        if let Some(refused) = refuse_write() {
            return refused;
        }
        let key = match store::create() {
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
        key.save().map(|()| Answer::ok()).into()
    })
    .await
}

/// DeleteVRChatRegistryFolder.
pub async fn delete(confirm: Confirm) -> Answer {
    if !confirm.confirm {
        return Answer::not_confirmed();
    }
    blocking(|| refuse_write().unwrap_or_else(|| store::delete().map(|_| Answer::ok()).into())).await
}

/// GetVRChatRegistryKey: {"value": <the value or null>}.
pub async fn read_key(read: ReadVrchatRegistryKey) -> Answer {
    blocking(move || {
        let value = store::open(false).and_then(|key| match key {
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
    .await
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
    blocking(move || {
        if let Some(refused) = refuse_write() {
            return refused;
        }
        match store::open(true) {
            Ok(Some(key)) => {
                key.set(&hashed_name(&set.key), kind, &bytes).and_then(|()| key.save()).map(|()| Answer::ok()).into()
            }
            Ok(None) => Answer::status(ActionStatus::NotFound, "VRChat has no registry key on this PC"),
            Err(e) => Answer::failed(e.to_string()),
        }
    })
    .await
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

    #[test]
    fn protons_user_reg_round_trips_around_vrchats_section() {
        let text = "WINE REGISTRY Version 2\n;; All keys relative to \\\\User\\\\S-1-5-21-0-0-0-1000\n\n#arch=win64\n\n\
[Software\\\\Valve] 1700000000\n#time=1da0000\n\"A\"=dword:00000001\n\n\
[Software\\\\VRChat\\\\VRChat] 1700000001\n#time=1da0001\n\"LOGGING_ENABLED_h123\"=dword:00000001\n\
\"name_h1\"=hex:68,69,\\\n  00\n\"f_h2\"=hex(4):00,00,00,00,00,00,e8,3f\n\"we\\\"ird_h3\"=hex:\n\n\
[Software\\\\Wine] 1700000002\n\"B\"=\"x\"\n";
        let file = wine::File::parse(text);
        assert_eq!(
            file.values(),
            vec![
                ("LOGGING_ENABLED_h123".to_owned(), REG_DWORD, 1u32.to_le_bytes().to_vec()),
                ("name_h1".to_owned(), REG_BINARY, b"hi\0".to_vec()),
                ("f_h2".to_owned(), REG_DWORD, 0.75f64.to_le_bytes().to_vec()),
                ("we\"ird_h3".to_owned(), REG_BINARY, vec![]),
            ]
        );
        assert_eq!(file.value("LOGGING_ENABLED_H123").unwrap().1, 1u32.to_le_bytes().to_vec());

        // Untouched values come back as they were, apart from the joined continuation line.
        let rendered = file.render();
        assert!(rendered.starts_with(&text[..text.find("[Software\\\\VRChat").unwrap()]));
        assert!(rendered.contains("\"name_h1\"=hex:68,69,00\n"));
        assert!(rendered.ends_with("\n\n[Software\\\\Wine] 1700000002\n\"B\"=\"x\"\n"));
        assert_eq!(wine::File::parse(&rendered), file);

        let mut file = file;
        file.set("new_h9", REG_DWORD, &(-1i32).to_le_bytes());
        file.set("f_h2", REG_DWORD, &1.5f64.to_le_bytes());
        let again = wine::File::parse(&file.render());
        assert_eq!(again.value("new_h9"), Some((REG_DWORD, (-1i32).to_le_bytes().to_vec())));
        assert_eq!(again.value("f_h2"), Some((REG_DWORD, 1.5f64.to_le_bytes().to_vec())));
        assert!(file.render().contains("\"new_h9\"=dword:ffffffff\n"));

        let mut removed = again.clone();
        removed.remove_section();
        assert!(!removed.render().contains("VRChat"));
        assert!(removed.render().contains("[Software\\\\Wine]"));
        assert!(wine::File::parse(&removed.render()).values().is_empty());
    }

    #[test]
    fn a_prefix_without_vrchat_gets_its_section() {
        let mut file = wine::File::parse("WINE REGISTRY Version 2\n\n[Software\\\\Wine] 1\n\"B\"=\"x\"\n");
        assert!(file.values().is_empty());
        file.add_section();
        file.set("a_h1", REG_BINARY, b"x");
        let text = file.render();
        assert!(text.contains("\"B\"=\"x\"\n\n[Software\\\\VRChat\\\\VRChat] "));
        assert_eq!(wine::File::parse(&text).value("a_h1"), Some((REG_BINARY, b"x".to_vec())));
    }
}
