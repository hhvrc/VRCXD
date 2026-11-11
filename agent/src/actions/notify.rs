//! Notifications on the PC (the original's `DesktopNotification`, `XSNotification` and
//! `OVRTNotification`): the desktop toast, an XSOverlay message over UDP, and OVR Toolkit's HUD
//! and wrist notifications over its local websocket. The server sends the image's bytes.

use std::path::PathBuf;
use std::time::{Duration, SystemTime};

use futures_util::SinkExt;
use serde_json::json;
use tokio_tungstenite::tungstenite::Message;
use tracing::warn;

use super::Answer;
use crate::platform;
use crate::proto::{Capability, ShowNotification};

pub const CAPABILITIES: [Capability; 1] = [Capability::Notifications];

/// The original's VRCX icon, shown when a notification has no image.
const VRCX_ICON: &[u8] = include_bytes!("../../assets/vrcx-icon.png");

const XSOVERLAY: &str = "127.0.0.1:42069";
const OVR_TOOLKIT: &str = "ws://127.0.0.1:11450/api";

pub fn base64(data: &[u8]) -> String {
    const TABLE: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut out = String::with_capacity(data.len().div_ceil(3) * 4);
    for chunk in data.chunks(3) {
        let b = [chunk[0], *chunk.get(1).unwrap_or(&0), *chunk.get(2).unwrap_or(&0)];
        let n = (u32::from(b[0]) << 16) | (u32::from(b[1]) << 8) | u32::from(b[2]);
        for i in 0..4 {
            if i <= chunk.len() {
                out.push(TABLE[(n >> (18 - 6 * i)) as usize & 63] as char);
            } else {
                out.push('=');
            }
        }
    }
    out
}

/// XSNotification's message: taller for longer text, the VRCX icon when there is no image.
pub fn xsoverlay_message(title: &str, content: &str, timeout: i32, opacity: f64, image: &[u8]) -> serde_json::Value {
    let length = content.chars().count();
    let height = if length > 300 {
        250.0
    } else if length > 200 {
        200.0
    } else if length > 100 {
        150.0
    } else {
        110.0
    };
    let icon = if image.is_empty() { VRCX_ICON } else { image };
    json!({
        "messageType": 1,
        "index": 0,
        "volume": 0.0,
        "audioPath": "",
        "timeout": timeout as f32,
        "title": title,
        "content": content,
        "icon": base64(icon),
        "height": height,
        "opacity": opacity as f32,
        "useBase64Icon": true,
        "sourceApp": "VRCX",
    })
}

/// OVRTNotification's messages: the wrist one as "title - body", the HUD one with the icon.
pub fn ovrt_messages(hud: bool, wrist: bool, title: &str, body: &str, image: &[u8]) -> Vec<String> {
    let icon = if image.is_empty() { VRCX_ICON } else { image };
    let mut messages = Vec::new();
    if wrist {
        let inner = json!({ "body": format!("{title} - {body}") }).to_string();
        messages.push(json!({ "messageType": "SendWristNotification", "json": inner }).to_string());
    }
    if hud {
        let inner = json!({ "title": title, "body": body, "icon": base64(icon) }).to_string();
        messages.push(json!({ "messageType": "SendNotification", "json": inner }).to_string());
    }
    messages
}

fn xml_escape(text: &str) -> String {
    text.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;").replace('"', "&quot;").replace('\'', "&apos;")
}

/// The toast's XML: the image as the app logo, then the bold line and the text, as ToastContentBuilder makes it.
pub fn toast_xml(title: &str, body: &str, image: Option<&std::path::Path>) -> String {
    let mut binding = String::new();
    if let Some(image) = image {
        binding.push_str(&format!(
            "<image placement=\"appLogoOverride\" src=\"{}\"/>",
            xml_escape(&format!("file:///{}", image.display()))
        ));
    }
    for text in [title, body] {
        if !text.is_empty() {
            binding.push_str(&format!("<text>{}</text>", xml_escape(text)));
        }
    }
    format!("<toast><visual><binding template=\"ToastGeneric\">{binding}</binding></visual></toast>")
}

/// The image as a file for the toast, named by its hash; images older than a day are cleared.
fn image_file(image: &[u8]) -> Option<PathBuf> {
    if image.is_empty() {
        return None;
    }
    let dir = platform::data_dir().join("notification-images");
    std::fs::create_dir_all(&dir).ok()?;
    if let Ok(entries) = std::fs::read_dir(&dir) {
        let day = Duration::from_secs(24 * 60 * 60);
        for entry in entries.filter_map(Result::ok) {
            let old =
                entry.metadata().and_then(|m| m.modified()).ok().and_then(|t| SystemTime::now().duration_since(t).ok());
            if old.is_some_and(|age| age > day) {
                let _ = std::fs::remove_file(entry.path());
            }
        }
    }
    let hash = ring::digest::digest(&ring::digest::SHA256, image);
    let name: String = hash.as_ref().iter().take(8).map(|b| format!("{b:02x}")).collect();
    let path = dir.join(format!("{name}.png"));
    if !path.exists() {
        std::fs::write(&path, image).ok()?;
    }
    Some(path)
}

#[cfg(windows)]
fn show_toast(title: &str, body: &str, image: Option<&std::path::Path>) -> std::io::Result<()> {
    use std::os::windows::process::CommandExt;
    // Windows PowerShell shows the toast through WinRT under its own registered app id, so nothing
    // has to be installed; it runs only while a toast is shown.
    const SCRIPT: &str = "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null; \
        [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null; \
        $x = New-Object Windows.Data.Xml.Dom.XmlDocument; $x.LoadXml($env:VRCXD_TOAST); \
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe').Show([Windows.UI.Notifications.ToastNotification]::new($x))";
    const CREATE_NO_WINDOW: u32 = 0x0800_0000;
    std::process::Command::new("powershell.exe")
        .args(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", SCRIPT])
        .env("VRCXD_TOAST", toast_xml(title, body, image))
        .creation_flags(CREATE_NO_WINDOW)
        .spawn()
        .map(drop)
}

#[cfg(not(windows))]
fn show_toast(title: &str, body: &str, image: Option<&std::path::Path>) -> std::io::Result<()> {
    let mut command = std::process::Command::new("notify-send");
    if let Some(image) = image {
        command.arg("-i").arg(image);
    }
    command.arg(title).arg(body).spawn().map(drop)
}

async fn send_xsoverlay(message: serde_json::Value) -> std::io::Result<()> {
    let socket = tokio::net::UdpSocket::bind("127.0.0.1:0").await?;
    socket.send_to(message.to_string().as_bytes(), XSOVERLAY).await.map(drop)
}

async fn send_ovrt(messages: Vec<String>) -> Result<(), String> {
    if messages.is_empty() {
        return Ok(());
    }
    let (mut socket, _) = tokio::time::timeout(Duration::from_secs(3), tokio_tungstenite::connect_async(OVR_TOOLKIT))
        .await
        .map_err(|_| "OVR Toolkit did not answer".to_string())?
        .map_err(|e| e.to_string())?;
    for message in messages {
        socket.send(Message::text(message)).await.map_err(|e| e.to_string())?;
    }
    let _ = socket.close(None).await;
    Ok(())
}

pub async fn show(n: ShowNotification) -> Answer {
    let mut errors = Vec::new();
    if n.desktop {
        let (title, body, image) = (n.title.clone(), n.body.clone(), n.image.clone());
        let shown = tokio::task::spawn_blocking(move || show_toast(&title, &body, image_file(&image).as_deref())).await;
        if let Ok(Err(e)) = shown {
            errors.push(format!("desktop: {e}"));
        }
    }
    if n.xsoverlay {
        // XSOverlay listens only on Windows; nothing listening is not an error for UDP.
        if let Err(e) = send_xsoverlay(xsoverlay_message("VRCX", &n.overlay_text, n.timeout, n.opacity, &n.image)).await
        {
            errors.push(format!("XSOverlay: {e}"));
        }
    }
    // The original logs and carries on when OVR Toolkit is not running.
    if (n.ovrtoolkit_hud || n.ovrtoolkit_wrist)
        && let Err(e) =
            send_ovrt(ovrt_messages(n.ovrtoolkit_hud, n.ovrtoolkit_wrist, "VRCX", &n.overlay_text, &n.image)).await
    {
        warn!("OVR Toolkit notification not sent: {e}");
    }
    if n.steamvr_overlay {
        let style = crate::vr::hmd::Style { position: n.position.clone(), dark: n.dark, opacity: n.opacity };
        crate::vr::hmd::push(
            n.title.clone(),
            n.body.clone(),
            n.image.clone(),
            Duration::from_secs(n.timeout.max(1) as u64),
            style,
        );
    }
    if errors.is_empty() { Answer::ok() } else { Answer::failed(errors.join("; ")) }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn base64_matches_the_standard() {
        assert_eq!(base64(b""), "");
        assert_eq!(base64(b"f"), "Zg==");
        assert_eq!(base64(b"fo"), "Zm8=");
        assert_eq!(base64(b"foobar"), "Zm9vYmFy");
    }

    #[test]
    fn xsoverlay_gets_the_originals_message() {
        let short = xsoverlay_message("VRCX", "hi", 3, 0.5, &[]);
        assert_eq!(short["height"], json!(110.0));
        assert_eq!(short["useBase64Icon"], json!(true));
        assert_eq!(short["icon"], json!(base64(VRCX_ICON)));
        assert_eq!(xsoverlay_message("VRCX", &"x".repeat(201), 3, 1.0, &[1])["height"], json!(200.0));
    }

    #[test]
    fn ovr_toolkit_gets_wrist_and_hud_messages() {
        let messages = ovrt_messages(true, true, "VRCX", "Bee is online", &[]);
        assert_eq!(messages.len(), 2);
        let wrist: serde_json::Value = serde_json::from_str(&messages[0]).unwrap();
        assert_eq!(wrist["messageType"], "SendWristNotification");
        assert_eq!(
            serde_json::from_str::<serde_json::Value>(wrist["json"].as_str().unwrap()).unwrap()["body"],
            "VRCX - Bee is online"
        );
        assert!(ovrt_messages(false, false, "a", "b", &[]).is_empty());
    }

    #[test]
    fn toast_text_is_escaped() {
        let xml = toast_xml("A & B", "<hi>", None);
        assert!(xml.contains("<text>A &amp; B</text><text>&lt;hi&gt;</text>"));
        assert!(!toast_xml("t", "", None).contains("<text></text>"));
    }
}
