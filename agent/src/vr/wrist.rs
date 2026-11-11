//! The wrist overlay (the original vr.html's wrist half: Vr.vue's feed, device batteries and the
//! bottom bar), drawn as SVG and rasterised with resvg for `SetOverlayRaw`.

use std::sync::Mutex;

use crate::proto::WristFeed;

use super::Device;

/// `.wrist { width: 512px; height: 510px }`, drawn on a square texture.
pub const SIZE: u32 = 512;
const PANEL_HEIGHT: f32 = 510.0;
/// 18px at line-height 1.25.
const ROW_HEIGHT: f32 = 22.5;
const PAD_X: f32 = 10.0;
/// `.tracker-container { width: 48px }`.
const DEVICE_WIDTH: f32 = 48.0;
const DEVICE_HEIGHT: f32 = 40.0;
const STATUS_HEIGHT: f32 = 26.0;
/// Characters that fit on a row at 18px, for the original's ellipsis.
const ROW_CHARS: usize = 52;

const BG: &str = "#18181b";
const BORDER: &str = "rgba(255,255,255,0.12)";
const TEXT_SECONDARY: &str = "#a1a1aa";
const TEXT_MUTED: &str = "#71717a";
const TEXT_NAME: &str = "#d4d4d8";
const TEXT_FRIEND: &str = "#fafafa";
const TEXT_FAVORITE: &str = "#fbbf24";

struct State {
    feed: Option<WristFeed>,
    changed: bool,
}

static FEED: Mutex<State> = Mutex::new(State { feed: None, changed: true });

/// The browser's wristFeedUpdate.
pub fn set_feed(feed: WristFeed) {
    if let Ok(mut state) = FEED.lock() {
        state.feed = Some(feed);
        state.changed = true;
    }
}

/// The feed and whether it changed since the last call.
pub fn feed() -> (WristFeed, bool) {
    match FEED.lock() {
        Ok(mut state) => {
            let changed = std::mem::take(&mut state.changed);
            (state.feed.clone().unwrap_or_default(), changed)
        }
        Err(_) => (WristFeed::default(), false),
    }
}

/// The device row as the original's updateStatsLoop orders it: headset, left and right
/// controllers, other controllers, then trackers, the connected ones first, and every connected
/// base station folded into one entry whose number is how many there are.
pub fn device_row(devices: &[Device]) -> Vec<Device> {
    let rank = |d: &Device| match d.kind {
        "headset" => 0,
        "leftController" => 1,
        "rightController" => 2,
        "controller" => 3,
        "tracker" | "base" => 4,
        _ => 5,
    };
    let mut row: Vec<Device> = devices.iter().filter(|d| !(d.kind == "base" && d.connected)).cloned().collect();
    row.sort_by_key(|d| (rank(d), !d.connected));
    row.sort_by_key(|d| !d.connected);
    let bases = devices.iter().filter(|d| d.kind == "base" && d.connected).count();
    if bases > 0 {
        row.push(Device {
            kind: "base",
            connected: true,
            charging: false,
            battery: bases as f32,
            tracking: "Running_OK",
        });
    }
    row
}

/// The original's formatSeconds: mm:ss, with hh: in front past an hour.
pub fn format_seconds(seconds: f64) -> String {
    let t = seconds.max(0.0).floor() as u64;
    let (h, m, s) = (t / 3600, t / 60 % 60, t % 60);
    if h > 0 { format!("{h:02}:{m:02}:{s:02}") } else { format!("{m:02}:{s:02}") }
}

/// What changes on the wrist from second to second, beside the feed.
#[derive(Debug, Clone, PartialEq, Default)]
pub struct Live {
    /// The clock, as the original's currentTime.
    pub clock: String,
    pub now_ms: i64,
    /// The CPU load when vrOverlayCpuUsage is on.
    pub cpu: Option<u32>,
    /// The PC's uptime when pcUptimeOnFeed is on.
    pub pc_uptime_ms: Option<i64>,
    /// What plays: the name, seconds played and the length.
    pub playing: Option<(String, u64, u64)>,
    pub devices: Vec<Device>,
}

/// A colour safe to put in an attribute: #rgb, #rrggbb or #rrggbbaa.
pub fn is_colour(colour: &str) -> bool {
    colour
        .strip_prefix('#')
        .is_some_and(|hex| matches!(hex.len(), 3 | 4 | 6 | 8) && hex.chars().all(|c| c.is_ascii_hexdigit()))
}

fn escape(text: &str) -> String {
    text.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;").replace('"', "&quot;")
}

fn ellipsis(text: &str, max: usize) -> String {
    if text.chars().count() <= max {
        return text.to_owned();
    }
    let mut cut: String = text.chars().take(max.saturating_sub(1)).collect();
    cut.push('…');
    cut
}

/// The icon Vr.vue picks for a device row.
pub fn icon(device: &Device) -> &'static [u8] {
    macro_rules! png {
        ($($part:literal),+) => {
            include_bytes!(concat!(env!("CARGO_MANIFEST_DIR"), "/assets/vr/", $($part),+, ".png")).as_slice()
        };
    }
    macro_rules! states {
        ($prefix:literal) => {
            if !device.connected {
                png!($prefix, "_status_off")
            } else if device.charging {
                png!($prefix, "_status_ready_charging")
            } else if device.battery < 20.0 {
                png!($prefix, "_status_ready_low")
            } else {
                png!($prefix, "_status_ready")
            }
        };
    }
    match device.kind {
        "headset" => states!("headset_quest"),
        "leftController" => states!("left_controller"),
        "rightController" => states!("right_controller"),
        "controller" => states!("controller"),
        "tracker" => states!("tracker"),
        "base" if device.connected => png!("base_status_ready"),
        "base" => png!("base_status_off"),
        _ => states!("other"),
    }
}

/// The battery caption: a percentage, or a base station's "Nx" when it is not 100.
fn caption(device: &Device) -> String {
    let battery = device.battery.round() as i64;
    match device.kind {
        "base" if battery == 100 => String::new(),
        "base" => format!("{battery}x"),
        _ => format!("{battery}%"),
    }
}

/// The bottom bar's timers: in this instance, online for, the PC's uptime, joined by " | ".
fn timers(feed: &WristFeed, live: &Live) -> String {
    let since = |at: i64| (at > 0).then(|| super::system::time_to_text(live.now_ms - at));
    let parts: Vec<String> = [
        since(feed.location_since_ms),
        since(feed.online_since_ms),
        live.pc_uptime_ms.map(super::system::time_to_text),
    ]
    .into_iter()
    .flatten()
    .collect();
    if parts.is_empty() {
        return String::new();
    }
    let label = if feed.minimal { String::new() } else { format!("{} ", feed.timer_label) };
    format!("{label}{}", parts.join(" | "))
}

/// The wrist panel: the feed rows, the device row, what plays and the two-row bottom bar.
pub fn svg(feed: &WristFeed, live: &Live) -> String {
    let mut out = format!(
        r#"<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}"><g font-family="Segoe UI, Noto Sans, Noto Sans CJK JP, Microsoft YaHei, Segoe UI Emoji, Noto Color Emoji, sans-serif">"#
    );
    if feed.background {
        out += &format!(
            r#"<rect x="0.5" y="0.5" width="511" height="{}" rx="8" fill="{BG}" stroke="{BORDER}"/>"#,
            PANEL_HEIGHT - 1.0
        );
    }
    let devices = if feed.hide_devices { Vec::new() } else { device_row(&live.devices) };
    let status_top = PANEL_HEIGHT - 2.0 * STATUS_HEIGHT;
    let playing_top = status_top - if live.playing.is_some() { STATUS_HEIGHT } else { 0.0 };
    let bottom = playing_top - if devices.is_empty() { 0.0 } else { DEVICE_HEIGHT };
    // .x-container { padding: 6px 10px 2px }
    let fit = ((bottom - 8.0) / ROW_HEIGHT).floor().max(0.0) as usize;
    for (i, row) in feed.rows.iter().take(fit).enumerate() {
        let baseline = 6.0 + (i as f32 + 1.0) * ROW_HEIGHT - 5.0;
        if i % 2 == 1 {
            out += &format!(
                r#"<rect x="1" y="{}" width="510" height="{ROW_HEIGHT}" fill="rgba(255,255,255,0.015)"/>"#,
                6.0 + i as f32 * ROW_HEIGHT
            );
        }
        // A custom tag's colour wins, as the original's inline style did.
        let name_color = if is_colour(&row.colour) {
            row.colour.as_str()
        } else if row.favorite {
            TEXT_FAVORITE
        } else if row.friend {
            TEXT_FRIEND
        } else {
            TEXT_NAME
        };
        let name = ellipsis(&row.name, ROW_CHARS);
        let text = ellipsis(&row.text, ROW_CHARS.saturating_sub(row.time.chars().count() + name.chars().count() + 2));
        out += &format!(
            r#"<text x="{PAD_X}" y="{baseline}" font-size="18" fill="{TEXT_SECONDARY}"><tspan font-size="14" font-weight="600" fill="{TEXT_MUTED}">{}</tspan> <tspan font-weight="600" fill="{name_color}">{}</tspan> {}</text>"#,
            escape(&row.time),
            escape(&name),
            escape(&text)
        );
    }
    if !devices.is_empty() {
        let width = ((SIZE as f32 - 2.0 * PAD_X) / devices.len() as f32).max(DEVICE_WIDTH);
        for (i, device) in devices.iter().enumerate() {
            let cx = PAD_X + width * (i as f32 + 0.5);
            let y = bottom + 3.0;
            let opacity = if device.tracking == "Running_OK" || device.kind == "base" { 0.9 } else { 0.5 };
            out += &format!(
                r#"<image x="{}" y="{y}" height="20" width="20" opacity="{opacity}" href="data:image/png;base64,{}"/>"#,
                cx - 10.0,
                crate::actions::notify::base64(icon(device))
            );
            out += &format!(
                r#"<text x="{cx}" y="{}" font-size="14" font-weight="500" text-anchor="middle" fill="{TEXT_SECONDARY}">{}</text>"#,
                y + 35.0,
                caption(device)
            );
        }
    }
    let right = SIZE as f32 - PAD_X;
    let line = |y: f32, left: &str, right_text: &str| {
        format!(
            r#"<text x="{PAD_X}" y="{y}" font-size="18" fill="{TEXT_SECONDARY}">{}</text><text x="{right}" y="{y}" font-size="18" text-anchor="end" fill="{TEXT_SECONDARY}">{}</text>"#,
            escape(left),
            escape(right_text)
        )
    };
    if let Some((name, played, length)) = &live.playing {
        // The name, the time left and a 3px bar of how much has played.
        let y = playing_top + 19.0;
        out += &format!(
            r##"<text x="{PAD_X}" y="{y}" font-size="18" font-weight="500" fill="#e4e4e7">{}</text><text x="{right}" y="{y}" font-size="18" text-anchor="end" fill="{TEXT_SECONDARY}">{}</text>"##,
            escape(&ellipsis(name, 40)),
            format_seconds(length.saturating_sub(*played) as f64)
        );
        let share = if *length > 0 { (*played as f32 / *length as f32).min(1.0) } else { 0.0 };
        out += &format!(
            r##"<rect x="{}" y="{}" width="{}" height="3" fill="#e4e4e7"/>"##,
            PAD_X + 5.0,
            playing_top + 23.0,
            (SIZE as f32 - 2.0 * PAD_X - 5.0) * share
        );
    }
    let labelled = |label: &str| if feed.minimal || label.is_empty() { String::new() } else { format!("{label} ") };
    let players = if feed.status.is_empty() {
        String::new()
    } else {
        format!("{}{}", labelled(&feed.players_label), feed.status)
    };
    out += &line(status_top + 19.0, &players, &timers(feed, live));
    let cpu = live.cpu.map(|c| format!("{} {c}% ", feed.cpu_label)).unwrap_or_default();
    let online = format!("{cpu}{} {}", feed.online_label, feed.online_friends);
    out += &line(PANEL_HEIGHT - 8.0, online.trim(), &live.clock);
    out += "</g></svg>";
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::proto::WristRow;

    fn device(kind: &'static str, connected: bool, charging: bool, battery: f32) -> Device {
        Device { kind, connected, charging, battery, tracking: "Running_OK" }
    }

    #[test]
    fn each_device_gets_the_original_icon_and_caption() {
        let png = |d: &Device| icon(d).len();
        let off = device("leftController", false, false, 80.0);
        let low = device("leftController", true, false, 10.0);
        let ok = device("leftController", true, false, 80.0);
        assert_ne!(png(&off), png(&low));
        assert_ne!(png(&low), png(&ok));
        assert_eq!(caption(&ok), "80%");
        assert_eq!(caption(&device("base", true, false, 100.0)), "");
        assert_eq!(caption(&device("base", true, false, 3.0)), "3x");
    }

    #[test]
    fn the_device_row_is_ordered_and_base_stations_fold_into_a_count() {
        let row = device_row(&[
            device("tracker", true, false, 90.0),
            device("base", true, false, 100.0),
            device("rightController", false, false, 50.0),
            device("base", true, false, 100.0),
            device("headset", true, false, 70.0),
            device("leftController", true, false, 60.0),
            device("base", false, false, 100.0),
        ]);
        let kinds: Vec<_> = row.iter().map(|d| (d.kind, d.connected, d.battery as i32)).collect();
        assert_eq!(
            kinds,
            [
                ("headset", true, 70),
                ("leftController", true, 60),
                ("tracker", true, 90),
                ("rightController", false, 50),
                ("base", false, 100),
                ("base", true, 2)
            ]
        );
        assert_eq!(format_seconds(65.9), "01:05");
        assert_eq!(format_seconds(3725.0), "01:02:05");
    }

    #[test]
    fn the_wrist_panel_shows_the_feed_devices_and_clock() {
        let feed = WristFeed {
            rows: (0..40)
                .map(|i| WristRow {
                    time: "12:00".into(),
                    name: format!("<Friend{i}>"),
                    text: "has logged in".into(),
                    friend: true,
                    favorite: i == 0,
                    colour: if i == 1 { "#ff00aa".into() } else { String::new() },
                })
                .collect(),
            background: true,
            status: "3 (1)".into(),
            location_since_ms: 10_000_000 - 725_000,
            online_since_ms: 10_000_000 - 3_725_000,
            online_friends: 7,
            timer_label: "Timer:".into(),
            players_label: "Players:".into(),
            cpu_label: "CPU:".into(),
            online_label: "Online:".into(),
            ..WristFeed::default()
        };
        let live = Live {
            clock: "11/11/2026 13:37:00".into(),
            now_ms: 10_000_000,
            cpu: Some(12),
            pc_uptime_ms: Some(90_061_000),
            playing: Some(("Song".into(), 30, 200)),
            devices: vec![device("headset", true, true, 55.0)],
        };
        let svg = svg(&feed, &live);
        assert!(svg.contains("&lt;Friend0&gt;"));
        assert!(svg.contains(TEXT_FAVORITE));
        assert!(svg.contains("#ff00aa"), "the tag colour");
        assert!(is_colour("#abc") && !is_colour("red\" onload=\"x") && !is_colour(""));
        assert!(!svg.contains("Friend30"), "only the rows that fit");
        assert!(svg.contains("55%") && svg.contains("13:37:00") && svg.contains("Players: 3 (1)"));
        assert!(svg.contains("Timer: 12m | 1h 2m | 1d 1h 1m"));
        assert!(svg.contains("CPU: 12% Online: 7"));
        assert!(svg.contains("Song") && svg.contains("02:50"));
        let minimal = super::svg(&WristFeed { minimal: true, ..feed.clone() }, &live);
        assert!(minimal.contains(">3 (1)<") && minimal.contains(">12m | 1h 2m | 1d 1h 1m<"));
        let pixels = super::super::hmd::render(&svg, SIZE, SIZE).expect("renders");
        assert_eq!(pixels[3], 0, "the rounded corner is clear");
        assert_eq!(pixels[((256 * SIZE + 256) * 4 + 3) as usize], 255, "the panel is opaque");
    }
}
