//! Discord Rich Presence (the original's `Discord.cs` with DiscordRPC): the server builds the
//! presence and sends it; this keeps a connection to the Discord app over its local IPC
//! (`discord-ipc-N`), reconnecting when the app id changes and closing it when the presence is
//! inactive, as the original's 3-second timer did.

use std::sync::LazyLock;
use std::time::{Duration, Instant};

use serde_json::{Value, json};
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};
use tokio::sync::watch;
use tracing::{debug, info, warn};

use crate::proto::{Capability, DiscordPresence};

pub const CAPABILITIES: [Capability; 1] = [Capability::Discord];

/// The original's Assets.LargeImageUrl.
const VRCX_URL: &str = "https://vrcx.app";
const OP_HANDSHAKE: u32 = 0;
const OP_FRAME: u32 = 1;
const OP_CLOSE: u32 = 2;
const OP_PING: u32 = 3;
const OP_PONG: u32 = 4;

static PRESENCE: LazyLock<watch::Sender<DiscordPresence>> =
    LazyLock::new(|| watch::channel(DiscordPresence::default()).0);

/// The server's latest presence; the loop shows it.
pub fn set(presence: DiscordPresence) {
    PRESENCE.send_replace(presence);
}

fn some(text: &str) -> Option<&str> {
    (!text.is_empty()).then_some(text)
}

/// The activity as DiscordRPC sends it: empty fields left out, the party only with a size, the
/// button only with a link.
pub fn activity(p: &DiscordPresence) -> Value {
    let mut a = serde_json::Map::new();
    if let Some(details) = some(&p.details) {
        a.insert("details".into(), json!(details));
    }
    if let Some(url) = some(&p.details_url) {
        a.insert("details_url".into(), json!(url));
    }
    if let Some(state) = some(&p.state) {
        a.insert("state".into(), json!(state));
    }
    if p.start_time > 0 {
        let mut t = json!({ "start": p.start_time });
        if p.end_time > 0 {
            t["end"] = json!(p.end_time);
        }
        a.insert("timestamps".into(), t);
    }
    if !p.large_image_key.is_empty() || !p.small_image_key.is_empty() {
        let mut assets = serde_json::Map::new();
        for (key, value) in [
            ("large_image", &p.large_image_key),
            ("large_text", &p.large_image_text),
            ("small_image", &p.small_image_key),
            ("small_text", &p.small_image_text),
        ] {
            if !value.is_empty() {
                assets.insert(key.into(), json!(value));
            }
        }
        assets.insert("large_url".into(), json!(VRCX_URL));
        a.insert("assets".into(), Value::Object(assets));
    }
    if p.party_max > 0 {
        a.insert("party".into(), json!({ "id": p.party_id, "size": [p.party_size, p.party_max] }));
    }
    if !p.button_url.is_empty() {
        a.insert("buttons".into(), json!([{ "label": p.button_text, "url": p.button_url }]));
    }
    a.insert("type".into(), json!(p.activity_type));
    a.insert("status_display_type".into(), json!(p.status_display_type));
    Value::Object(a)
}

pub fn frame(op: u32, payload: &Value) -> Vec<u8> {
    let body = payload.to_string();
    let mut out = Vec::with_capacity(8 + body.len());
    out.extend_from_slice(&op.to_le_bytes());
    out.extend_from_slice(&(body.len() as u32).to_le_bytes());
    out.extend_from_slice(body.as_bytes());
    out
}

trait Stream: AsyncRead + AsyncWrite + Unpin + Send {}
impl<T: AsyncRead + AsyncWrite + Unpin + Send> Stream for T {}

struct Connection {
    app_id: String,
    stream: Box<dyn Stream>,
    nonce: u64,
}

#[cfg(windows)]
async fn open_pipe() -> std::io::Result<Box<dyn Stream>> {
    use tokio::net::windows::named_pipe::ClientOptions;
    let mut last = std::io::Error::from(std::io::ErrorKind::NotFound);
    for n in 0..10 {
        match ClientOptions::new().open(format!(r"\\.\pipe\discord-ipc-{n}")) {
            Ok(pipe) => return Ok(Box::new(pipe)),
            Err(e) => last = e,
        }
    }
    Err(last)
}

#[cfg(unix)]
async fn open_pipe() -> std::io::Result<Box<dyn Stream>> {
    let mut dirs: Vec<std::path::PathBuf> =
        ["XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP"].iter().filter_map(std::env::var_os).map(Into::into).collect();
    dirs.push("/tmp".into());
    let mut last = std::io::Error::from(std::io::ErrorKind::NotFound);
    for dir in dirs {
        // The Flatpak and Snap Discord apps put the socket in their own folder.
        for sub in ["", "app/com.discordapp.Discord", "snap.discord"] {
            for n in 0..10 {
                match tokio::net::UnixStream::connect(dir.join(sub).join(format!("discord-ipc-{n}"))).await {
                    Ok(socket) => return Ok(Box::new(socket)),
                    Err(e) => last = e,
                }
            }
        }
    }
    Err(last)
}

impl Connection {
    async fn open(app_id: &str) -> Result<Self, String> {
        let stream = open_pipe().await.map_err(|e| format!("Discord is not running: {e}"))?;
        let mut c = Self { app_id: app_id.to_string(), stream, nonce: 0 };
        c.write(OP_HANDSHAKE, &json!({ "v": 1, "client_id": app_id })).await?;
        let (op, ready) = c.read().await?;
        if op != OP_FRAME || ready["evt"] != "READY" {
            return Err(format!("Discord refused the connection: {ready}"));
        }
        let user = ready["data"]["user"]["global_name"].as_str().or(ready["data"]["user"]["username"].as_str());
        info!("Discord Rich Presence connected: {}", user.unwrap_or("?"));
        Ok(c)
    }

    async fn write(&mut self, op: u32, payload: &Value) -> Result<(), String> {
        self.stream.write_all(&frame(op, payload)).await.map_err(|e| e.to_string())
    }

    async fn read(&mut self) -> Result<(u32, Value), String> {
        let read = async {
            let mut head = [0u8; 8];
            self.stream.read_exact(&mut head).await.map_err(|e| e.to_string())?;
            let op = u32::from_le_bytes(head[..4].try_into().unwrap_or_default());
            let len = u32::from_le_bytes(head[4..].try_into().unwrap_or_default()) as usize;
            if len > 1 << 20 {
                return Err("Discord sent an oversized frame".to_string());
            }
            let mut body = vec![0u8; len];
            self.stream.read_exact(&mut body).await.map_err(|e| e.to_string())?;
            Ok((op, serde_json::from_slice(&body).unwrap_or(Value::Null)))
        };
        tokio::time::timeout(Duration::from_secs(5), read).await.map_err(|_| "Discord did not answer".to_string())?
    }

    async fn set_activity(&mut self, p: &DiscordPresence) -> Result<(), String> {
        self.nonce += 1;
        let command = json!({
            "cmd": "SET_ACTIVITY",
            "args": { "pid": std::process::id(), "activity": activity(p) },
            "nonce": self.nonce.to_string(),
        });
        self.write(OP_FRAME, &command).await?;
        loop {
            match self.read().await? {
                (OP_PING, body) => self.write(OP_PONG, &body).await?,
                (OP_CLOSE, body) => return Err(format!("Discord closed the connection: {body}")),
                (_, answer) if answer["evt"] == "ERROR" => {
                    return Err(format!("Discord Rich Presence error: {}", answer["data"]["message"]));
                }
                (_, answer) if answer["nonce"] == command["nonce"] => return Ok(()),
                _ => {}
            }
        }
    }

    async fn close(mut self) {
        let _ = self.write(OP_CLOSE, &json!({})).await;
        let _ = self.stream.shutdown().await;
    }
}

/// Shows the latest presence: connects when it turns active, reconnects for another app id,
/// and sends it again every 30 seconds so a restarted Discord gets it back.
pub async fn run() {
    let mut rx = PRESENCE.subscribe();
    let mut tick = tokio::time::interval(Duration::from_secs(3));
    let mut connection: Option<Connection> = None;
    let mut shown: Option<(DiscordPresence, Instant)> = None;
    loop {
        tokio::select! {
            _ = tick.tick() => {}
            changed = rx.changed() => if changed.is_err() { return },
        }
        let presence = rx.borrow_and_update().clone();
        if !presence.active || presence.app_id.is_empty() {
            if let Some(c) = connection.take() {
                c.close().await;
            }
            shown = None;
            continue;
        }
        if connection.as_ref().is_some_and(|c| c.app_id != presence.app_id)
            && let Some(c) = connection.take()
        {
            c.close().await;
            shown = None;
        }
        if connection.is_none() {
            match Connection::open(&presence.app_id).await {
                Ok(c) => connection = Some(c),
                Err(e) => {
                    debug!("{e}");
                    continue;
                }
            }
        }
        let fresh = shown.as_ref().is_some_and(|(p, at)| *p == presence && at.elapsed() < Duration::from_secs(30));
        if fresh {
            continue;
        }
        if let Some(c) = connection.as_mut() {
            match c.set_activity(&presence).await {
                Ok(()) => shown = Some((presence, Instant::now())),
                Err(e) => {
                    warn!("{e}");
                    connection = None;
                    shown = None;
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_activity_leaves_out_what_is_empty() {
        let p = DiscordPresence {
            active: true,
            details: "The Black Cat".into(),
            large_image_key: "vrchat".into(),
            large_image_text: "Powered by VRCX".into(),
            small_image_key: "active".into(),
            small_image_text: "Active".into(),
            start_time: 1_700_000_000_000,
            party_id: "wrld_1:123".into(),
            party_size: 3,
            party_max: 40,
            app_id: "883308884863901717".into(),
            ..Default::default()
        };
        let a = activity(&p);
        assert_eq!(a["details"], "The Black Cat");
        assert!(a.get("state").is_none() && a.get("buttons").is_none() && a.get("details_url").is_none());
        assert_eq!(a["timestamps"], json!({ "start": 1_700_000_000_000i64 }));
        assert_eq!(a["party"]["size"], json!([3, 40]));
        assert_eq!(a["assets"]["large_url"], VRCX_URL);
        let hidden = activity(&DiscordPresence {
            party_max: 0,
            button_url: "https://x".into(),
            button_text: "Join".into(),
            ..p
        });
        assert!(hidden.get("party").is_none());
        assert_eq!(hidden["buttons"][0]["label"], "Join");
    }

    #[test]
    fn frames_carry_the_op_and_length() {
        let f = frame(OP_HANDSHAKE, &json!({ "v": 1 }));
        assert_eq!(&f[..4], &0u32.to_le_bytes());
        assert_eq!(u32::from_le_bytes(f[4..8].try_into().unwrap()) as usize, f.len() - 8);
    }
}
