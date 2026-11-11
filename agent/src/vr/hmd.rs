//! The HMD overlay's notifications (the original vr.html's Noty toasts: playNoty, its layouts and the
//! relax and sunset themes), drawn as SVG and rasterised with resvg for `SetOverlayRaw`.

use std::sync::{Arc, Mutex, OnceLock};
use std::time::{Duration, Instant};

use crate::proto::{HudEntry, HudFeed, HudTimeout, NowPlayingPie};

/// The HMD half of the original's texture.
pub const SIZE: u32 = 1024;
/// Noty's default maxVisible.
const MAX_VISIBLE: usize = 5;
const BAR_HEIGHT: f32 = 84.0;
const BAR_GAP: f32 = 8.0;
/// `.noty_layout { width: 75% }`.
const LAYOUT_WIDTH: f32 = SIZE as f32 * 0.75;
/// `.noty_layout { padding-top: 512px }`: toasts stay in the lower half of the view.
const LAYOUT_TOP: f32 = 512.0;
/// Noty's own layout margin from the edges.
const EDGE: f32 = 20.0;
/// Characters that fit on a bar at 30px beside the image, for the original's ellipsis.
const MAX_CHARS: usize = 40;

#[derive(Debug, Clone)]
pub struct Toast {
    pub title: String,
    pub body: String,
    /// PNG or JPEG bytes, or empty.
    pub image: Vec<u8>,
    pub until: Instant,
}

#[derive(Debug, Clone, PartialEq)]
pub struct Style {
    /// Noty's layout: topLeft, topCenter, topRight, centerLeft, center, centerRight, bottomLeft,
    /// bottomCenter, bottomRight (and top, bottom).
    pub position: String,
    /// The sunset theme, which the original used in dark mode.
    pub dark: bool,
    /// 0 to 1.
    pub opacity: f64,
}

impl Default for Style {
    fn default() -> Self {
        Self { position: "topCenter".into(), dark: false, opacity: 1.0 }
    }
}

/// One Photon HUD line, with how many times it came in a row (the original's combo).
#[derive(Debug, Clone)]
pub struct HudLine {
    pub entry: HudEntry,
    pub combo: u32,
    pub until: Instant,
}

/// The original's hud-feed keeps ten lines.
const HUD_LINES: usize = 10;

struct Queue {
    toasts: Vec<Toast>,
    style: Style,
    hud: Vec<HudLine>,
    timeouts: Vec<HudTimeout>,
    pie: Option<NowPlayingPie>,
    /// The whole percent last drawn, so the pie redraws as it fills.
    pie_drawn: u32,
    changed: bool,
}

static QUEUE: Mutex<Queue> = Mutex::new(Queue {
    toasts: Vec::new(),
    style: Style { position: String::new(), dark: false, opacity: 1.0 },
    hud: Vec::new(),
    timeouts: Vec::new(),
    pie: None,
    pie_drawn: 0,
    changed: false,
});

/// What the HMD overlay shows right now.
#[derive(Debug, Clone, Default)]
pub struct Frame {
    pub toasts: Vec<Toast>,
    pub style: Style,
    pub hud: Vec<HudLine>,
    pub timeouts: Vec<HudTimeout>,
    /// The share of the video played, 1 to 100; 0 hides the pie.
    pub pie: u32,
}

impl Frame {
    pub fn is_empty(&self) -> bool {
        self.toasts.is_empty() && self.hud.is_empty() && self.timeouts.is_empty() && self.pie == 0
    }
}

/// Seconds played by the clock, None when nothing plays or it has ended.
fn played(pie: &NowPlayingPie, now_unix: f64) -> Option<f64> {
    if !pie.active || pie.length <= 0.0 {
        return None;
    }
    let elapsed =
        if pie.paused { pie.position } else { pie.position + (now_unix - pie.logged_at) * pie.speed.max(0.0) };
    (elapsed < pie.length).then_some(elapsed.max(0.0))
}

/// The original's nowPlaying.percentage from the clock, for the pie: 0 hides it.
pub fn pie_percent(pie: &NowPlayingPie, now_unix: f64) -> u32 {
    match played(pie, now_unix) {
        Some(elapsed) if pie.pie && elapsed > 0.0 => ((elapsed / pie.length * 100.0).floor() as u32).clamp(1, 100),
        _ => 0,
    }
}

/// What plays for the wrist: the name, whole seconds played and the length.
pub fn now_playing(now_unix: f64) -> Option<(String, u64, u64)> {
    let q = QUEUE.lock().ok()?;
    let pie = q.pie.as_ref()?;
    let elapsed = played(pie, now_unix)?;
    Some((pie.name.clone(), elapsed as u64, pie.length as u64))
}

fn now_unix() -> f64 {
    std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs_f64()).unwrap_or_default()
}

/// The original's nowPlayingUpdate for the pie.
pub fn update_pie(pie: NowPlayingPie) {
    if let Ok(mut q) = QUEUE.lock() {
        q.pie = pie.active.then_some(pie);
        q.changed = true;
    }
}

/// The original's addEntryHudFeed (a repeat of the same name and text folds into one line with a
/// count) and updateHudTimeout.
pub fn update_hud(feed: HudFeed) {
    let Ok(mut q) = QUEUE.lock() else { return };
    let until = Instant::now() + Duration::from_millis(feed.message_timeout_ms.max(0) as u64);
    // Oldest first, so the newest ends on top.
    for entry in feed.added.into_iter().rev() {
        let mut combo = 1;
        q.hud.retain(|line| {
            let same = line.entry.display_name == entry.display_name && line.entry.text == entry.text;
            if same {
                combo = line.combo + 1;
            }
            !same
        });
        q.hud.insert(0, HudLine { entry, combo, until });
    }
    q.hud.truncate(HUD_LINES);
    if feed.timeouts_set {
        q.timeouts = feed.timeouts;
    }
    q.changed = true;
}

/// Queues a toast, as the original's playNoty did.
pub fn push(title: String, body: String, image: Vec<u8>, timeout: Duration, style: Style) {
    if let Ok(mut q) = QUEUE.lock() {
        q.toasts.push(Toast { title, body, image, until: Instant::now() + timeout });
        let excess = q.toasts.len().saturating_sub(MAX_VISIBLE);
        q.toasts.drain(..excess);
        q.style = style;
        q.changed = true;
    }
}

/// What still shows, when it changed since the last call (something new or something gone).
pub fn take_changes(now: Instant) -> Option<Frame> {
    let mut q = QUEUE.lock().ok()?;
    let before = q.toasts.len() + q.hud.len();
    q.toasts.retain(|t| t.until > now);
    q.hud.retain(|l| l.until > now);
    if q.toasts.len() + q.hud.len() != before {
        q.changed = true;
    }
    let pie = q.pie.as_ref().map_or(0, |p| pie_percent(p, now_unix()));
    if pie != q.pie_drawn {
        q.pie_drawn = pie;
        q.changed = true;
    }
    if !std::mem::take(&mut q.changed) {
        return None;
    }
    Some(Frame {
        toasts: q.toasts.clone(),
        style: q.style.clone(),
        hud: q.hud.clone(),
        timeouts: q.timeouts.clone(),
        pie,
    })
}

pub fn clear() {
    if let Ok(mut q) = QUEUE.lock() {
        q.toasts.clear();
        q.hud.clear();
        q.timeouts.clear();
        q.pie = None;
        q.changed = true;
    }
}

fn escape(text: &str) -> String {
    text.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;").replace('"', "&quot;")
}

/// `text-overflow: ellipsis` on one line, by characters.
fn ellipsis(text: &str, max: usize) -> String {
    if text.chars().count() <= max {
        return text.to_owned();
    }
    let mut cut: String = text.chars().take(max.saturating_sub(1)).collect();
    cut.push('…');
    cut
}

fn mime(image: &[u8]) -> Option<&'static str> {
    match image {
        [0x89, b'P', b'N', b'G', ..] => Some("image/png"),
        [0xFF, 0xD8, ..] => Some("image/jpeg"),
        [b'G', b'I', b'F', ..] => Some("image/gif"),
        [b'R', b'I', b'F', b'F', _, _, _, _, b'W', b'E', b'B', b'P', ..] => Some("image/webp"),
        _ => None,
    }
}

/// Where the first bar goes and which way the stack grows.
fn origin(position: &str, count: usize) -> (f32, f32) {
    let stack = count as f32 * (BAR_HEIGHT + BAR_GAP) - BAR_GAP;
    let x = if position.ends_with("Left") {
        EDGE
    } else if position.ends_with("Right") {
        SIZE as f32 - EDGE - LAYOUT_WIDTH
    } else {
        (SIZE as f32 - LAYOUT_WIDTH) / 2.0
    };
    let y = if position.starts_with("bottom") {
        SIZE as f32 - EDGE - stack
    } else if position.starts_with("center") {
        LAYOUT_TOP + (SIZE as f32 - LAYOUT_TOP - stack) / 2.0
    } else {
        LAYOUT_TOP + EDGE
    };
    (x, y)
}

const FONTS: &str =
    "Segoe UI, Noto Sans, Noto Sans CJK JP, Microsoft YaHei, Segoe UI Emoji, Noto Color Emoji, sans-serif";
/// The red timeout icon from Vr.vue's hud-timeout.
const TIMEOUT_ICON: &str = "M68.6,96.5L87,78.1c1.6-1.6,1.6-4.1,0-5.7s-4.1-1.6-5.7,0L62.9,90.9L44.5,72.5l18.4-18.4c1.6-1.6,1.6-4.1,0-5.7c-1.6-1.6-4.1-1.6-5.7,0L38.9,66.8l-6.4-6.4L21.2,71.8C11,82,9.7,97.9,17.4,109.5L0,126.9l8.5,8.5L25.9,118c11.6,7.7,27.5,6.4,37.8-3.8L75,102.9C75,102.9,68.6,96.5,68.6,96.5z";
/// Lines over the game stay readable with a dark outline behind them.
const OUTLINE: &str = r##"stroke="#000" stroke-width="4" stroke-linejoin="round" paint-order="stroke""##;

/// The Photon event HUD: right-aligned 32px lines from the top, newest first.
fn hud_svg(lines: &[HudLine]) -> String {
    let mut out = String::new();
    for (i, line) in lines.iter().enumerate() {
        let e = &line.entry;
        let y = 34.0 + i as f32 * 40.0;
        let name_color = if super::wrist::is_colour(&e.colour) {
            e.colour.as_str()
        } else if e.favorite {
            "#fbbf24"
        } else if e.friend {
            "#fafafa"
        } else {
            "#d4d4d8"
        };
        let text_color = if e.color == "yellow" { "yellow" } else { "#a1a1aa" };
        let marks = format!("{}{}", if e.master { "👑" } else { "" }, if e.moderator { "⚔️" } else { "" });
        let combo =
            if line.combo > 1 { format!(r##"<tspan fill="#71717a"> x{}</tspan>"##, line.combo) } else { String::new() };
        out += &format!(
            r#"<text x="{}" y="{y}" font-size="32" text-anchor="end" {OUTLINE}>{}<tspan font-weight="bold" fill="{name_color}">{}</tspan><tspan fill="{text_color}"> {}</tspan>{combo}</text>"#,
            SIZE - 4,
            escape(&marks),
            escape(&ellipsis(&e.display_name, 24)),
            escape(&ellipsis(&e.text, 40)),
        );
    }
    out
}

/// The timeout HUD: "(Ns) name" in red at 40px above the icon in the bottom right.
fn timeouts_svg(timeouts: &[HudTimeout]) -> String {
    if timeouts.is_empty() {
        return String::new();
    }
    let size = SIZE as f32;
    let mut out = format!(
        r##"<path transform="translate({} {})" fill="#ED1B24" d="{TIMEOUT_ICON}"/>"##,
        size - 90.0,
        size - 140.0
    );
    let bottom = size - 150.0;
    for (i, t) in timeouts.iter().rev().enumerate() {
        out += &format!(
            r##"<text x="{}" y="{}" font-size="40" text-anchor="end" fill="#ed1b24" {OUTLINE}>({}s) {}</text>"##,
            size - 4.0,
            bottom - i as f32 * 48.0,
            t.seconds,
            escape(&ellipsis(&t.display_name, 30))
        );
    }
    out
}

/// The progress pie: a 120px half-transparent white disc in the bottom left that fills clockwise
/// from the top, as the original's np-progress-circle (a 60px-wide stroke on a 30px circle).
fn pie_svg(percent: u32) -> String {
    if percent == 0 {
        return String::new();
    }
    let circumference = 2.0 * std::f32::consts::PI * 30.0;
    let filled = circumference * percent as f32 / 100.0;
    format!(
        r#"<circle cx="60" cy="{cy}" r="30" fill="none" stroke="white" stroke-width="60" opacity="0.5" stroke-dasharray="{filled} {circumference}" transform="rotate(-90 60 {cy})"/>"#,
        cy = SIZE as f32 - 60.0
    )
}

/// The whole HMD texture: the Photon HUDs, the progress pie and the notification toasts.
pub fn frame_svg(frame: &Frame) -> String {
    let mut out = format!(
        r#"<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}"><g font-family="{FONTS}">"#
    );
    out += &hud_svg(&frame.hud);
    out += &timeouts_svg(&frame.timeouts);
    out += &pie_svg(frame.pie);
    out += &toasts_svg(&frame.toasts, &frame.style);
    out += "</g></svg>";
    out
}

/// The toasts as the original's Noty bars: an 84px bar with the image on the left and
/// "<strong>title</strong> body" at 30px.
fn toasts_svg(toasts: &[Toast], style: &Style) -> String {
    let (background, border, color) =
        if style.dark { ("#073b4c", "#073b4c", "#fff") } else { ("#fff", "#dedede", "#444") };
    let (x, top) = origin(&style.position, toasts.len());
    let mut out = format!(r#"<g opacity="{}" font-size="30">"#, style.opacity.clamp(0.0, 1.0));
    for (i, toast) in toasts.iter().enumerate() {
        let y = top + i as f32 * (BAR_HEIGHT + BAR_GAP);
        out += &format!(
            r#"<rect x="{x}" y="{y}" width="{LAYOUT_WIDTH}" height="{BAR_HEIGHT}" rx="8" fill="{background}" stroke="{border}" stroke-width="2"/>"#
        );
        let mut text_x = x + 22.0;
        if let Some(mime) = mime(&toast.image) {
            let id = format!("clip{i}");
            out += &format!(
                r#"<clipPath id="{id}"><rect x="{x}" y="{y}" width="{BAR_HEIGHT}" height="{BAR_HEIGHT}" rx="8"/></clipPath><image x="{x}" y="{y}" width="{BAR_HEIGHT}" height="{BAR_HEIGHT}" preserveAspectRatio="xMidYMid slice" clip-path="url(#{id})" xlink:href="data:{mime};base64,{}"/>"#,
                crate::actions::notify::base64(&toast.image)
            );
            text_x += BAR_HEIGHT;
        }
        let title = ellipsis(&toast.title, MAX_CHARS);
        let body = ellipsis(&toast.body, MAX_CHARS.saturating_sub(title.chars().count() + 1));
        // padding-top 16px, then the 30px line's baseline.
        let baseline = y + 16.0 + 30.0;
        out += &format!(
            r#"<text x="{text_x}" y="{baseline}" fill="{color}"><tspan font-weight="bold">{}</tspan> {}</text>"#,
            escape(&title),
            escape(&body)
        );
    }
    out += "</g>";
    out
}

fn options() -> &'static resvg::usvg::Options<'static> {
    static OPTIONS: OnceLock<resvg::usvg::Options<'static>> = OnceLock::new();
    OPTIONS.get_or_init(|| {
        let mut db = resvg::usvg::fontdb::Database::new();
        db.load_system_fonts();
        resvg::usvg::Options { fontdb: Arc::new(db), ..resvg::usvg::Options::default() }
    })
}

/// The SVG as straight RGBA for `SetOverlayRaw`; None when it does not parse.
pub fn render(svg: &str, width: u32, height: u32) -> Option<Vec<u8>> {
    let tree = resvg::usvg::Tree::from_str(svg, options()).ok()?;
    let mut pixmap = resvg::tiny_skia::Pixmap::new(width, height)?;
    resvg::render(&tree, resvg::tiny_skia::Transform::default(), &mut pixmap.as_mut());
    let mut rgba = pixmap.take();
    // tiny-skia keeps premultiplied alpha.
    for px in rgba.as_chunks_mut::<4>().0 {
        let a = px[3] as u32;
        if a != 0 && a != 255 {
            for c in &mut px[..3] {
                *c = ((*c as u32 * 255 + a / 2) / a).min(255) as u8;
            }
        }
    }
    Some(rgba)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn toast(title: &str, body: &str) -> Toast {
        Toast {
            title: title.into(),
            body: body.into(),
            image: Vec::new(),
            until: Instant::now() + Duration::from_secs(3),
        }
    }

    #[test]
    fn toasts_stack_where_the_layout_says() {
        assert_eq!(origin("topCenter", 1), (128.0, 532.0));
        assert_eq!(origin("bottomRight", 2), (236.0, 1024.0 - 20.0 - 176.0));
        assert_eq!(origin("centerLeft", 1).0, 20.0);
    }

    #[test]
    fn a_toast_is_escaped_shortened_and_themed() {
        let frame = Frame {
            toasts: vec![toast("<Ann>", &"x".repeat(80))],
            style: Style { dark: true, ..Style::default() },
            ..Frame::default()
        };
        let svg = frame_svg(&frame);
        assert!(svg.contains("&lt;Ann&gt;"));
        assert!(svg.contains("#073b4c"));
        assert!(svg.contains('…'));
        let pixels = render(&svg, SIZE, SIZE).expect("renders");
        assert_eq!(pixels.len(), (SIZE * SIZE * 4) as usize);
        // The bar is opaque where it is drawn, and the rest is clear.
        let at = |x: u32, y: u32| pixels[((y * SIZE + x) * 4 + 3) as usize];
        assert_eq!(at(600, 600), 255);
        assert_eq!(at(10, 10), 0);
    }

    #[test]
    fn the_pie_follows_the_clock() {
        let pie = NowPlayingPie {
            active: true,
            pie: true,
            position: 30.0,
            logged_at: 1000.0,
            length: 120.0,
            speed: 1.0,
            paused: false,
            name: "Song".into(),
        };
        assert_eq!(pie_percent(&NowPlayingPie { pie: false, ..pie.clone() }, 1000.0), 0, "the pie is off");
        assert_eq!(pie_percent(&pie, 1000.0), 25);
        assert_eq!(pie_percent(&pie, 1030.0), 50);
        assert_eq!(pie_percent(&NowPlayingPie { paused: true, ..pie.clone() }, 1060.0), 25);
        assert_eq!(pie_percent(&pie, 1100.0), 0, "ended");
        assert_eq!(pie_percent(&NowPlayingPie { length: 0.0, ..pie.clone() }, 1000.0), 0, "no length");
        assert_eq!(played(&pie, 1010.0), Some(40.0));
        assert!(pie_svg(50).contains("stroke-dasharray=\"94.2"));
        assert!(pie_svg(0).is_empty());
    }

    // One test for the shared queue, so the two do not race.
    #[test]
    fn toasts_and_hud_lines_expire_and_are_capped() {
        clear();
        let _ = take_changes(Instant::now());
        for i in 0..7 {
            push(format!("n{i}"), "joined".into(), Vec::new(), Duration::from_millis(50), Style::default());
        }
        let shown = take_changes(Instant::now()).expect("changed");
        assert_eq!(shown.toasts.iter().map(|t| t.title.as_str()).collect::<Vec<_>>(), ["n2", "n3", "n4", "n5", "n6"]);
        assert!(take_changes(Instant::now()).is_none());
        let left = take_changes(Instant::now() + Duration::from_secs(1)).expect("expired");
        assert!(left.is_empty());

        let line =
            |name: &str, text: &str| HudEntry { display_name: name.into(), text: text.into(), ..HudEntry::default() };
        update_hud(HudFeed {
            added: vec![line("Bee", "ChatBox hi"), line("Ann", "has joined VR")],
            message_timeout_ms: 50,
            ..HudFeed::default()
        });
        update_hud(HudFeed {
            added: vec![line("Bee", "ChatBox hi")],
            message_timeout_ms: 50,
            timeouts_set: true,
            timeouts: vec![HudTimeout { display_name: "Cat".into(), seconds: 9 }],
        });
        let frame = take_changes(Instant::now()).expect("changed");
        assert_eq!(
            frame.hud.iter().map(|l| (l.entry.display_name.as_str(), l.combo)).collect::<Vec<_>>(),
            [("Bee", 2), ("Ann", 1)]
        );
        assert_eq!(frame.timeouts.len(), 1);
        let svg = frame_svg(&frame);
        assert!(svg.contains(" x2") && svg.contains("(9s) Cat"));
        let pixels = render(&svg, SIZE, SIZE).expect("renders");
        assert!(pixels.as_chunks::<4>().0.iter().any(|p| p[3] == 255), "the HUD draws");
        let later = take_changes(Instant::now() + Duration::from_secs(1)).expect("lines expired");
        assert!(later.hud.is_empty() && later.timeouts.len() == 1, "timeouts stay until replaced");
        clear();
    }
}
