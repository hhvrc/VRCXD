//! Turns one VRChat log line into at most one typed `LogEntry`, the same fields the original's
//! `LogWatcher.cs` emitted (and `gameLog.js` unpacked). No derived state is built here: the server
//! does that, from these entries only.
//!
//! A line-for-line port of the Qt desktop app's `log_parser.cpp`, including its deliberate fixes
//! over the C# original:
//! - "STEAMVR HMD Model: " matches (C# compared 20 chars against a 19-char literal).
//! - The audio device name loses VRChat's closing `'`.
//! - Malformed user info ("Name (usr_x" with no ")") is parsed leniently instead of throwing.
//! - "User Authenticated: Name (usr_...)" becomes `user_authenticated`, so the server knows which
//!   account plays on this PC.

use std::collections::HashSet;

use chrono::{DateTime, Duration, Local, NaiveDateTime, TimeZone, Utc};
use tracing::{info, warn};

use crate::proto::log_entry::Entry;
use crate::proto::{
    LogAvatarChange, LogLocation, LogLocationDestination, LogNoData, LogPlayer, LogStickerSpawn, LogText, LogVideoPlay,
};

/// Where the message starts in a timestamped line:
/// `2021.06.23 12:02:56 Log        -  [Behaviour] Entering Room: ...`
const OFFSET: usize = 34;

/// Per-file parser state (the original's `LogContext` minus file positions).
#[derive(Debug, Default, Clone)]
pub struct LogContext {
    audio_device_changed: bool,
    last_audio_device: String,
    video_playback_errors: HashSet<String>,
    location_destination: String,
    recent_world_name: String,
    shader_keywords_limit_reached: bool,
    /// Time of the newest line that carried one; continuation lines have none of their own.
    last_line_time: Option<DateTime<Utc>>,
}

/// What one parser made of a message: not its line, its line with nothing to send, or an entry.
enum Parsed {
    NotMatched,
    Consumed,
    Entry(Entry),
}
use Parsed::{Consumed, NotMatched};

fn text(f: fn(LogText) -> Entry, s: impl Into<String>) -> Parsed {
    Parsed::Entry(f(LogText { text: s.into() }))
}

/// VRChat writes local time, `yyyy.MM.dd HH:mm:ss`.
fn line_time(line: &str) -> Option<DateTime<Utc>> {
    let stamp = line.get(..19)?;
    let naive = NaiveDateTime::parse_from_str(stamp, "%Y.%m.%d %H:%M:%S").ok()?;
    let local = Local
        .from_local_datetime(&naive)
        .earliest()
        // A time in the hour skipped when clocks go forward: as Qt does, read it an hour later.
        .or_else(|| Local.from_local_datetime(&(naive + Duration::hours(1))).earliest())?;
    Some(local.with_timezone(&Utc))
}

/// The text after the last `pattern`.
fn after<'a>(s: &'a str, pattern: &str) -> Option<&'a str> {
    s.rfind(pattern).map(|i| &s[i + pattern.len()..])
}

fn without_last_char(s: &str) -> &str {
    let mut chars = s.chars();
    chars.next_back();
    chars.as_str()
}

fn clean_id(s: &str) -> String {
    s.chars().filter(|c| c.is_ascii_alphanumeric() || matches!(c, '_' | '-' | '~' | ':' | '(' | ')')).collect()
}

fn clean_location(s: &str) -> String {
    s.replace('/', "")
}

/// `Name (usr_xxx)` gives the display name and cleaned user id; with no ` (` the whole is the name.
fn parse_user_info(info: &str) -> (String, String) {
    let Some(pos) = info.rfind(" (") else { return (info.to_owned(), String::new()) };
    let rest = &info[pos + 2..];
    let id = rest.find(')').map_or(rest, |end| &rest[..end]);
    (info[..pos].to_owned(), clean_id(id))
}

fn parse_udon_exception(line: &str) -> Option<Entry> {
    if line.contains("[PyPyDance]") {
        return Some(Entry::UdonException(LogText { text: line.to_owned() }));
    }
    let i = line.find(" ---> VRC.Udon.VM.UdonVMException: ")?;
    Some(Entry::UdonException(LogText { text: line[i..].to_owned() }))
}

fn player(display_name: String, user_id: String) -> LogPlayer {
    LogPlayer { display_name, user_id }
}

fn parse_user_authenticated(msg: &str) -> Parsed {
    let Some(info) = after(msg, "User Authenticated: ") else { return NotMatched };
    let (display_name, user_id) = parse_user_info(info);
    if user_id.is_empty() {
        warn!("no user id in authentication line: {msg}");
        return Consumed;
    }
    Parsed::Entry(Entry::UserAuthenticated(player(display_name, user_id)))
}

fn parse_location(ctx: &mut LogContext, msg: &str) -> Parsed {
    if msg.contains("[Behaviour] Entering Room: ") {
        if let Some(name) = after(msg, "] Entering Room: ") {
            ctx.recent_world_name = name.to_owned();
        }
        return Consumed;
    }
    if msg.contains("[Behaviour] Joining ")
        && !msg.contains("] Joining or Creating Room: ")
        && !msg.contains("] Joining friend: ")
    {
        let Some(location) = after(msg, "] Joining ").filter(|l| !l.is_empty()) else { return Consumed };
        ctx.last_audio_device.clear();
        ctx.video_playback_errors.clear();
        return Parsed::Entry(Entry::Location(LogLocation {
            location: clean_location(location),
            world_name: ctx.recent_world_name.clone(),
        }));
    }
    NotMatched
}

fn parse_location_destination(ctx: &mut LogContext, msg: &str) -> Parsed {
    if msg.contains("[Behaviour] OnLeftRoom") {
        let location = std::mem::take(&mut ctx.location_destination);
        return Parsed::Entry(Entry::LocationDestination(LogLocationDestination { location }));
    }
    if msg.contains("[Behaviour] Destination fetching: ") {
        if let Some(dest) = after(msg, "] Destination fetching: ").filter(|d| !d.is_empty()) {
            ctx.location_destination = clean_location(dest);
        }
        return Consumed;
    }
    NotMatched
}

fn parse_player_joined_or_left(msg: &str) -> Parsed {
    let (info, joined) = if msg.contains("[Behaviour] OnPlayerJoined") && !msg.contains("] OnPlayerJoined:") {
        (after(msg, "] OnPlayerJoined "), true)
    } else if msg.contains("[Behaviour] OnPlayerLeft")
        && !msg.contains("] OnPlayerLeftRoom")
        && !msg.contains("] OnPlayerLeft:")
    {
        (after(msg, "] OnPlayerLeft "), false)
    } else {
        return NotMatched;
    };
    let Some(info) = info else { return Consumed };
    let (display_name, user_id) = parse_user_info(info);
    if display_name.is_empty() && user_id.is_empty() {
        warn!("failed to parse user info from log line: {msg}");
        return Consumed;
    }
    let p = player(display_name, user_id);
    Parsed::Entry(if joined { Entry::PlayerJoined(p) } else { Entry::PlayerLeft(p) })
}

fn parse_portal_spawn(msg: &str) -> Parsed {
    if msg.contains("[Behaviour] Instantiated a (Clone [") && msg.contains("] Portals/PortalInternalDynamic)") {
        return Parsed::Entry(Entry::PortalSpawn(LogNoData {}));
    }
    NotMatched
}

fn parse_shader_keywords_limit(ctx: &mut LogContext, msg: &str) -> Parsed {
    if !msg.contains("Maximum number (384) of shader global keywords exceeded") {
        return NotMatched;
    }
    if ctx.shader_keywords_limit_reached {
        return Consumed;
    }
    ctx.shader_keywords_limit_reached = true;
    text(Entry::Event, "Shader Keyword Limit has been reached")
}

fn parse_join_blocked(msg: &str) -> Parsed {
    if !msg.contains("] Master is not sending any events! Moving to a new instance.") {
        return NotMatched;
    }
    text(Entry::Event, "Joining instance blocked by master")
}

fn parse_avatar_pedestal_change(msg: &str) -> Parsed {
    match msg.strip_prefix("[Network Processing] RPC invoked SwitchAvatar on AvatarPedestal for ") {
        Some(user) => text(Entry::Event, format!("{user} changed avatar pedestal")),
        None => NotMatched,
    }
}

const YOUTUBE_BOT_ERROR: &str = "Sign in to confirm";
const YOUTUBE_BOT_ERROR_FIX: &str = "[VRCX] Fix error with this: https://github.com/EllyVR/VRCVideoCacher";

fn video_error(ctx: &mut LogContext, data: &str) -> Parsed {
    if !ctx.video_playback_errors.insert(data.to_owned()) {
        return Consumed;
    }
    let message =
        if data.contains(YOUTUBE_BOT_ERROR) { format!("{YOUTUBE_BOT_ERROR_FIX}\n{data}") } else { data.to_owned() };
    text(Entry::Event, format!("VideoError: {message}"))
}

fn parse_video_error(ctx: &mut LogContext, msg: &str) -> Parsed {
    for marker in ["[Video Playback] ERROR: ", "[AVProVideo] Error: "] {
        if let Some(i) = msg.find(marker) {
            return video_error(ctx, &msg[i + marker.len()..]);
        }
    }
    NotMatched
}

fn parse_untrusted_url(ctx: &mut LogContext, msg: &str) -> Parsed {
    if !msg.contains("Attempted to play an untrusted URL") {
        return NotMatched;
    }
    if !ctx.video_playback_errors.insert(msg.to_owned()) {
        return Consumed;
    }
    text(Entry::Event, format!("VideoError: {msg}"))
}

fn parse_world_vrcx(msg: &str) -> Parsed {
    // [VRCX] VideoPlay(PyPyDance) "https://jd.pypy.moe/api/v1/videos/-Q3pdlsQxOk.mp4",0.5338666,260.6938,"1339 : Le Freak (Random)"
    match msg.strip_prefix("[VRCX] ") {
        Some(rest) => text(Entry::Vrcx, rest),
        None => NotMatched,
    }
}

fn parse_world_data_vrcx(msg: &str) -> Parsed {
    // PWI, deprecated: logged and dropped, like the C# watcher.
    match msg.strip_prefix("[VRCX-World] ") {
        Some(rest) => {
            info!("VRCX-World data: {rest}");
            Consumed
        }
        None => NotMatched,
    }
}

fn video_play(url: &str, display_name: &str) -> Parsed {
    Parsed::Entry(Entry::VideoPlay(LogVideoPlay { video_url: url.to_owned(), display_name: display_name.to_owned() }))
}

fn parse_video_change(msg: &str) -> Parsed {
    // [Video Playback] Attempting to resolve URL 'https://www.youtube.com/watch?v=dQw4w9WgXcQ'
    // [Video Playback] Resolving URL 'rtspt://topaz.chat/live/kiriri520'
    let url = msg
        .strip_prefix("[Video Playback] Attempting to resolve URL '")
        .or_else(|| msg.strip_prefix("[Video Playback] Resolving URL '"));
    match url {
        Some(url) => video_play(without_last_char(url), ""),
        None => NotMatched,
    }
}

fn parse_now_playing(msg: &str) -> Parsed {
    // nowplaying {"id":"Main-1","type":"mediaPlay","mediaTime":12.5}
    // https://protv.dev/guides/nowplaying: the JSON goes to the server whole.
    const MARKER: &str = "nowplaying {";
    let Some(pos) = msg.find(MARKER) else { return NotMatched };
    let start = pos + MARKER.len() - 1;
    match msg.rfind('}') {
        Some(end) if end >= start => text(Entry::NowPlaying, &msg[start..=end]),
        _ => NotMatched,
    }
}

fn parse_sdk2_video_play(msg: &str) -> Parsed {
    // User Natsumi-sama added URL https://www.youtube.com/watch?v=dQw4w9WgXcQ
    let Some(rest) = msg.strip_prefix("User ") else { return NotMatched };
    let marker = " added URL ";
    match rest.rfind(marker) {
        Some(pos) => video_play(&rest[pos + marker.len()..], &rest[..pos]),
        None => NotMatched,
    }
}

fn parse_usharp_video_play(msg: &str) -> Parsed {
    // [USharpVideo] Started video load for URL: https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=1s, requested by <name>
    let Some(rest) = msg.strip_prefix("[USharpVideo] Started video load for URL: ") else { return NotMatched };
    let marker = ", requested by ";
    match rest.rfind(marker) {
        Some(pos) => video_play(&rest[..pos], &rest[pos + marker.len()..]),
        None => NotMatched,
    }
}

fn parse_usharp_video_sync(msg: &str) -> Parsed {
    match msg.strip_prefix("[USharpVideo] Syncing video to ") {
        Some(rest) => text(Entry::VideoSync, rest),
        None => NotMatched,
    }
}

fn parse_notification(msg: &str) -> Parsed {
    let Some(rest) = msg.strip_prefix("[API] Received Notification: <") else { return NotMatched };
    match rest.rfind("> received at ") {
        Some(pos) => text(Entry::Notification, &rest[..pos]),
        None => NotMatched,
    }
}

fn parse_api_request(msg: &str) -> Parsed {
    // [API] [110] Sending Get request to https://api.vrchat.cloud/api/1/worlds?...
    if !msg.starts_with("[API] [") {
        return NotMatched;
    }
    match after(msg, "] Sending Get request to ") {
        Some(url) => text(Entry::ApiRequest, url),
        None => NotMatched,
    }
}

fn parse_avatar_change(msg: &str) -> Parsed {
    // [Behaviour] Switching K.MOG to avatar MoeSera
    let Some(rest) = msg.strip_prefix("[Behaviour] Switching ") else { return NotMatched };
    let marker = " to avatar ";
    match rest.rfind(marker) {
        Some(pos) => Parsed::Entry(Entry::AvatarChange(LogAvatarChange {
            display_name: rest[..pos].to_owned(),
            avatar_name: rest[pos + marker.len()..].to_owned(),
        })),
        None => NotMatched,
    }
}

fn parse_audio_configuration_changed(ctx: &mut LogContext, msg: &str) -> Parsed {
    // [Always] uSpeak: OnAudioConfigurationChanged - devicesChanged = True, resetting mic..
    // [Always] uSpeak: SetInputDevice 0 (2 total) 'Microphone (NVIDIA Broadcast)'
    if msg.contains("[Always] uSpeak: OnAudioConfigurationChanged") {
        ctx.audio_device_changed = true;
        return Consumed;
    }
    if !msg.contains("[Always] uSpeak: SetInputDevice 0") {
        return NotMatched;
    }
    let Some(device) = after(msg, ") '").filter(|d| !d.is_empty()) else { return Consumed };
    let device = device.strip_suffix('\'').unwrap_or(device).to_owned();
    if ctx.last_audio_device.is_empty() {
        ctx.audio_device_changed = false;
        ctx.last_audio_device = device;
        return Consumed;
    }
    if !ctx.audio_device_changed || ctx.last_audio_device == device {
        return Consumed;
    }
    ctx.audio_device_changed = false;
    let event = format!("Audio device changed, mic set to '{device}'");
    ctx.last_audio_device = device;
    text(Entry::Event, event)
}

fn parse_application_quit(msg: &str) -> Parsed {
    if msg.starts_with("VRCApplication: OnApplicationQuit at ")
        || msg.starts_with("VRCApplication: HandleApplicationQuit at ")
    {
        return Parsed::Entry(Entry::VrcQuit(LogNoData {}));
    }
    NotMatched
}

fn parse_openvr_init(msg: &str) -> Parsed {
    if msg.starts_with("Initializing VRSDK.") || msg.starts_with("STEAMVR HMD Model: ") {
        return Parsed::Entry(Entry::OpenvrInit(LogNoData {}));
    }
    NotMatched
}

fn parse_desktop_mode(msg: &str) -> Parsed {
    if msg.starts_with("VR Disabled") {
        return Parsed::Entry(Entry::DesktopMode(LogNoData {}));
    }
    NotMatched
}

fn parse_screenshot(msg: &str) -> Parsed {
    // [VRC Camera] Took screenshot to: C:\Users\Tea\Pictures\VRChat\...\VRChat_....png
    if !msg.contains("[VRC Camera] Took screenshot to: ") {
        return NotMatched;
    }
    text(Entry::Screenshot, after(msg, "] Took screenshot to: ").unwrap_or_default())
}

fn parse_resource_download(msg: &str) -> Parsed {
    // [String Download] Attempting to load String from URL 'https://pastebin.com/raw/BaW6NL2L'
    // [Image Download] Attempting to load image from URL 'https://i.imgur.com/lCfUMX0.jpeg'
    let (url, image) = if let Some(u) = after(msg, "] Attempting to load String from URL '") {
        (u, false)
    } else if let Some(u) = after(msg, "] Attempting to load image from URL '") {
        (u, true)
    } else {
        return NotMatched;
    };
    let url = without_last_char(url);
    // VRCX's own local requests (the Electron client's world-data server).
    if url.starts_with("http://127.0.0.1:22500") || url.starts_with("http://localhost:22500") {
        return Consumed;
    }
    text(if image { Entry::ResourceLoadImage } else { Entry::ResourceLoadString }, url)
}

fn parse_vote_kick(msg: &str) -> Parsed {
    // [Behaviour] Received executive message: You have been kicked from the instance by majority vote
    match msg.strip_prefix("[Behaviour] Received executive message: ") {
        Some(rest) => text(Entry::Event, rest),
        None => NotMatched,
    }
}

fn parse_failed_to_join(msg: &str) -> Parsed {
    // [Behaviour] Failed to join instance 'wrld_...' due to '...'
    match msg.strip_prefix("[Behaviour] ") {
        Some(data) if data.starts_with("Failed to join instance ") => text(Entry::Event, data),
        _ => NotMatched,
    }
}

fn parse_osc_failed_to_start(msg: &str) -> Parsed {
    // Could not Start OSC: Address already in use
    if !msg.starts_with("Could not Start OSC: ") {
        return NotMatched;
    }
    text(Entry::Event, format!("VRChat couldn't start OSC server, \"{msg}\""))
}

fn parse_moderation_manager(msg: &str) -> Parsed {
    // [ModerationManager] This instance will be reset in 60 minutes due to its age.
    // [ModerationManager] A vote kick has been initiated against <name>, do you agree?
    // [ModerationManager] Vote to kick <name> succeeded
    let tag = "[ModerationManager] ";
    for pattern in ["This instance will be reset in ", "A vote kick has been initiated against ", "Vote to kick "] {
        if let Some(i) = msg.find(&format!("{tag}{pattern}")) {
            return text(Entry::Event, &msg[i + tag.len()..]);
        }
    }
    NotMatched
}

fn parse_sticker_spawn(msg: &str) -> Parsed {
    // [StickersManager] User usr_032383a7-748c-4fb2-94e4-bcb928e5de6b (Natsumi-sama) spawned sticker inv_8b380ee4-...
    let tag = "[StickersManager] User ";
    let Some(i) = msg.find(tag) else { return NotMatched };
    if !msg.contains("inv_") || !msg.contains("spawned sticker") {
        return NotMatched;
    }
    let info = &msg[i + tag.len()..];
    // The player lines' "Name (id)" shape, flipped: the user id first, the name in the parentheses.
    let (user_id, display_name) = parse_user_info(info);
    if user_id.is_empty() && display_name.is_empty() {
        warn!("failed to parse user info from log line: {msg}");
        return Consumed;
    }
    let inventory_id = info.find("inv_").map(|p| clean_id(&info[p..])).unwrap_or_default();
    Parsed::Entry(Entry::StickerSpawn(LogStickerSpawn { user_id, display_name, inventory_id }))
}

/// Parses one log line. Lines at or before `till` (the newest entry the server already has) or over
/// an hour in the future are skipped, like the original's `ParseLog`. `None` means nothing to send.
pub fn parse_line(
    ctx: &mut LogContext,
    line: &str,
    till: DateTime<Utc>,
    now: DateTime<Utc>,
) -> Option<(DateTime<Utc>, Entry)> {
    if let Some(entry) = parse_udon_exception(line) {
        // Udon exception text often lands on continuation lines with no timestamp. The C# stamped
        // those "now", which during catch-up dates an old line in the present and moves the server's
        // resume point past everything not yet read. They take the time of the line before instead,
        // and are skipped with it.
        let dt = match line_time(line) {
            Some(dt) => {
                ctx.last_line_time = Some(dt);
                Some(dt)
            }
            None => ctx.last_line_time,
        };
        if dt.is_some_and(|dt| dt <= till) {
            return None;
        }
        return Some((dt.unwrap_or(now), entry));
    }

    let bytes = line.as_bytes();
    if bytes.len() <= 36 || bytes[31] != b'-' || !line.is_char_boundary(OFFSET) {
        return None;
    }
    let Some(dt) = line_time(line) else {
        warn!("failed to parse log date: {line}");
        return None;
    };
    ctx.last_line_time = Some(dt);
    if dt <= till {
        return None;
    }
    // Compensates for VRChat's log not handling DST changes.
    if now + Duration::minutes(61) < dt {
        warn!("invalid log time, too new: {line}");
        return None;
    }

    let msg = &line[OFFSET..];
    // The first parser that recognises the line wins, in the original's order.
    macro_rules! first {
        ($($parser:expr),+ $(,)?) => {{
            let mut result = NotMatched;
            $(if matches!(result, NotMatched) { result = $parser; })+
            result
        }};
    }
    let parsed = match parse_user_authenticated(msg) {
        NotMatched if msg.starts_with('[') => first![
            parse_player_joined_or_left(msg),
            parse_location(ctx, msg),
            parse_location_destination(ctx, msg),
            parse_portal_spawn(msg),
            parse_notification(msg),
            parse_api_request(msg),
            parse_avatar_change(msg),
            parse_join_blocked(msg),
            parse_avatar_pedestal_change(msg),
            parse_video_error(ctx, msg),
            parse_video_change(msg),
            parse_usharp_video_play(msg),
            parse_usharp_video_sync(msg),
            parse_world_vrcx(msg),
            parse_world_data_vrcx(msg),
            parse_audio_configuration_changed(ctx, msg),
            parse_screenshot(msg),
            parse_resource_download(msg),
            parse_vote_kick(msg),
            parse_failed_to_join(msg),
            parse_moderation_manager(msg),
            parse_sticker_spawn(msg),
        ],
        NotMatched => first![
            parse_now_playing(msg),
            parse_shader_keywords_limit(ctx, msg),
            parse_sdk2_video_play(msg),
            parse_application_quit(msg),
            parse_openvr_init(msg),
            parse_desktop_mode(msg),
            parse_osc_failed_to_start(msg),
            parse_untrusted_url(ctx, msg),
        ],
        other => other,
    };
    match parsed {
        Parsed::Entry(entry) => Some((dt, entry)),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    //! The Qt app's `test_log_parser.cpp`, case for case.
    use super::*;

    fn far_past() -> DateTime<Utc> {
        DateTime::UNIX_EPOCH
    }
    fn far_future() -> DateTime<Utc> {
        Utc.with_ymd_and_hms(2030, 1, 1, 0, 0, 0).unwrap()
    }
    fn parse(ctx: &mut LogContext, line: &str) -> Option<Entry> {
        parse_line(ctx, line, far_past(), far_future()).map(|(_, e)| e)
    }
    fn one(line: &str) -> Option<Entry> {
        parse(&mut LogContext::default(), line)
    }
    fn event(line: &str) -> String {
        match one(line) {
            Some(Entry::Event(t)) => t.text,
            other => panic!("not an event: {other:?}"),
        }
    }

    #[test]
    fn location_uses_recent_world_name() {
        let mut ctx = LogContext::default();
        assert!(parse(&mut ctx, "2021.06.23 12:02:56 Log        -  [Behaviour] Entering Room: VRChat Home").is_none());
        let loc = "wrld_4432ea9b-729c-46e3-8eaf-846aa0a37fdd:67646~private(usr_4f76a584-9d4b-46f6-8209-8305eb683661)~region(eu)";
        let e = parse(&mut ctx, &format!("2021.06.23 12:02:56 Log        -  [Behaviour] Joining {loc}"));
        assert_eq!(e, Some(Entry::Location(LogLocation { location: loc.into(), world_name: "VRChat Home".into() })));
        assert!(
            parse(&mut ctx, "2021.06.23 12:02:56 Log        -  [Behaviour] Joining or Creating Room: VRChat Home")
                .is_none()
        );
        assert!(parse(&mut ctx, "2021.06.23 12:02:56 Log        -  [Behaviour] Joining friend: pypy").is_none());
    }

    #[test]
    fn location_destination_emitted_on_left_room() {
        let mut ctx = LogContext::default();
        assert!(parse(&mut ctx, "2021.09.02 00:49:15 Log        -  [Behaviour] Destination fetching: wrld_4432ea9b-729c-46e3-8eaf-846aa0a37fdd").is_none());
        let dest = |l: &str| Some(Entry::LocationDestination(LogLocationDestination { location: l.into() }));
        assert_eq!(
            parse(&mut ctx, "2025.11.20 01:35:38 Debug      -  [Behaviour] OnLeftRoom"),
            dest("wrld_4432ea9b-729c-46e3-8eaf-846aa0a37fdd")
        );
        // Cleared after use.
        assert_eq!(parse(&mut ctx, "2025.11.20 01:35:39 Debug      -  [Behaviour] OnLeftRoom"), dest(""));
    }

    #[test]
    fn players_joined_and_left() {
        let p = |n: &str, id: &str| player(n.into(), id.into());
        assert_eq!(
            one(
                "2021.12.12 11:47:22 Log        -  [Behaviour] OnPlayerJoined Natsumi-sama (usr_032383a7-748c-4fb2-94e4-bcb928e5de6b)"
            ),
            Some(Entry::PlayerJoined(p("Natsumi-sama", "usr_032383a7-748c-4fb2-94e4-bcb928e5de6b")))
        );
        assert_eq!(
            one("2021.12.12 11:47:22 Log        -  [Behaviour] OnPlayerJoined Natsumi-sama"),
            Some(Entry::PlayerJoined(p("Natsumi-sama", "")))
        );
        assert_eq!(
            one(
                "2021.12.12 11:47:22 Log        -  [Behaviour] OnPlayerLeft Rize\u{2665} (usr_4f76a584-9d4b-46f6-8209-8305eb683661)"
            ),
            Some(Entry::PlayerLeft(p("Rize\u{2665}", "usr_4f76a584-9d4b-46f6-8209-8305eb683661")))
        );
        assert!(one("2021.12.12 11:47:22 Log        -  [Behaviour] OnPlayerJoined:Unnamed").is_none());
        assert!(one("2021.12.12 11:53:14 Log        -  [Behaviour] OnPlayerLeftRoom").is_none());
        // No closing paren: the C# threw here.
        assert_eq!(
            one("2021.12.12 11:47:22 Log        -  [Behaviour] OnPlayerJoined Bob (usr_abc"),
            Some(Entry::PlayerJoined(p("Bob", "usr_abc")))
        );
    }

    #[test]
    fn video_plays() {
        let v = |u: &str, n: &str| Some(Entry::VideoPlay(LogVideoPlay { video_url: u.into(), display_name: n.into() }));
        assert_eq!(
            one(
                "2021.04.20 13:37:19 Log        -  [Video Playback] Attempting to resolve URL 'https://www.youtube.com/watch?v=dQw4w9WgXcQ'"
            ),
            v("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "")
        );
        assert_eq!(
            one("2023.05.12 15:53:48 Log        -  [Video Playback] Resolving URL 'rtspt://topaz.chat/live/kiriri520'"),
            v("rtspt://topaz.chat/live/kiriri520", "")
        );
        assert_eq!(
            one(
                "2021.04.23 13:12:25 Log        -  User Natsumi-sama added URL https://www.youtube.com/watch?v=dQw4w9WgXcQ"
            ),
            v("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "Natsumi-sama")
        );
        // The name is what follows the last ", requested by ", so spaces in it survive.
        let spaced = "\u{029C} \u{1D00} \u{1D18} \u{1D18} \u{028F}";
        assert_eq!(
            one(&format!(
                "2021.12.12 05:51:58 Log        -  [USharpVideo] Started video load for URL: https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=1s, requested by {spaced}"
            )),
            v("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=1s", spaced)
        );
        assert_eq!(
            one("2022.01.16 05:20:23 Log        -  [USharpVideo] Syncing video to 2.52"),
            Some(Entry::VideoSync(LogText { text: "2.52".into() }))
        );
    }

    #[test]
    fn video_errors_deduped_until_next_location() {
        let mut ctx = LogContext::default();
        let line = "2021.04.08 06:37:45 Error      -  [Video Playback] ERROR: Video unavailable";
        let err = Some(Entry::Event(LogText { text: "VideoError: Video unavailable".into() }));
        assert_eq!(parse(&mut ctx, line), err);
        assert!(parse(&mut ctx, line).is_none());
        parse(&mut ctx, "2021.04.08 06:38:00 Log        -  [Behaviour] Joining wrld_x:1");
        assert_eq!(parse(&mut ctx, line), err);

        assert_eq!(
            event("2024.07.31 22:28:47 Error      -  [AVProVideo] Error: Sign in to confirm you're not a bot"),
            "VideoError: [VRCX] Fix error with this: https://github.com/EllyVR/VRCVideoCacher\nSign in to confirm you're not a bot"
        );
        let untrusted =
            "Attempted to play an untrusted URL (Domain: localhost) that is not allowlisted for public instances.";
        assert_eq!(
            event(&format!("2025.05.04 22:38:12 Error      -  {untrusted}")),
            format!("VideoError: {untrusted}")
        );
    }

    #[test]
    fn misc_events() {
        assert_eq!(
            event(
                "2021.04.07 09:34:37 Error      -  [Behaviour] Master is not sending any events! Moving to a new instance."
            ),
            "Joining instance blocked by master"
        );
        assert_eq!(
            event(
                "2021.05.07 10:48:19 Log        -  [Network Processing] RPC invoked SwitchAvatar on AvatarPedestal for User"
            ),
            "User changed avatar pedestal"
        );
        assert_eq!(
            event(
                "2023.06.02 01:08:04 Log        -  [Behaviour] Received executive message: You have been kicked from the instance by majority vote"
            ),
            "You have been kicked from the instance by majority vote"
        );
        assert_eq!(
            event(
                "2023.09.01 10:42:19 Warning    -  [Behaviour] Failed to join instance 'wrld_78eb:82384' due to 'outdated'"
            ),
            "Failed to join instance 'wrld_78eb:82384' due to 'outdated'"
        );
        assert_eq!(
            event("2023.09.26 04:12:57 Warning    -  Could not Start OSC: Address already in use"),
            "VRChat couldn't start OSC server, \"Could not Start OSC: Address already in use\""
        );
        assert_eq!(
            event(
                "2024.08.30 01:43:40 Log        -  [ModerationManager] This instance will be reset in 60 minutes due to its age."
            ),
            "This instance will be reset in 60 minutes due to its age."
        );
        let kick = "Vote to kick \u{05d1}\u{05d5}\u{05e8}\u{05e7}\u{05e1} \u{05d1}\u{05de}\u{05d0}\u{05d5}\u{05d5}\u{05e8}\u{05e8}\u{05d9}\u{05dd} 849d succeeded";
        assert_eq!(event(&format!("2024.08.29 02:05:21 Log        -  [ModerationManager] {kick}")), kick);

        let mut ctx = LogContext::default();
        let shader = "2021.08.20 04:20:09 Error      -  Maximum number (384) of shader global keywords exceeded, keyword _FOG_EXP2 will be ignored.";
        assert_eq!(
            parse(&mut ctx, shader),
            Some(Entry::Event(LogText { text: "Shader Keyword Limit has been reached".into() }))
        );
        assert!(parse(&mut ctx, shader).is_none());
    }

    #[test]
    fn audio_device_change_needs_reconfiguration() {
        let mut ctx = LogContext::default();
        let set = |name: &str| {
            format!("2025.01.03 19:11:42 Log        -  [Always] uSpeak: SetInputDevice 0 (2 total) '{name}'")
        };
        assert!(parse(&mut ctx, &set("Mic A")).is_none());
        assert!(parse(&mut ctx, &set("Mic B")).is_none());
        parse(
            &mut ctx,
            "2025.01.03 19:12:00 Log        -  [Always] uSpeak: OnAudioConfigurationChanged - devicesChanged = True, resetting mic..",
        );
        assert_eq!(
            parse(&mut ctx, &set("Mic (NVIDIA Broadcast)")),
            Some(Entry::Event(LogText { text: "Audio device changed, mic set to 'Mic (NVIDIA Broadcast)'".into() }))
        );
    }

    #[test]
    fn simple_markers() {
        assert!(matches!(
            one("2022.06.12 01:51:46 Log        -  VRCApplication: OnApplicationQuit at 1603.499"),
            Some(Entry::VrcQuit(_))
        ));
        assert!(matches!(
            one("2024.10.23 21:18:34 Log        -  VRCApplication: HandleApplicationQuit at 936.5161"),
            Some(Entry::VrcQuit(_))
        ));
        assert!(matches!(one("2023.04.22 16:52:28 Log        -  Initializing VRSDK."), Some(Entry::OpenvrInit(_))));
        assert!(matches!(
            one("2024.07.26 01:48:56 Log        -  STEAMVR HMD Model: Index"),
            Some(Entry::OpenvrInit(_))
        ));
        assert!(matches!(one("2023.04.22 16:54:18 Log        -  VR Disabled"), Some(Entry::DesktopMode(_))));
        assert!(matches!(
            one(
                "2022.07.29 18:40:37 Log        -  [Behaviour] Instantiated a (Clone [800004] Portals/PortalInternalDynamic)"
            ),
            Some(Entry::PortalSpawn(_))
        ));
    }

    #[test]
    fn text_payloads() {
        let t = |s: &str| LogText { text: s.into() };
        assert_eq!(
            one(
                "2023.02.08 12:31:35 Log        -  [VRC Camera] Took screenshot to: C:\\Users\\Tea\\Pictures\\VRChat\\a.png"
            ),
            Some(Entry::Screenshot(t("C:\\Users\\Tea\\Pictures\\VRChat\\a.png")))
        );
        assert_eq!(
            one(
                "2023.03.23 11:37:21 Log        -  [String Download] Attempting to load String from URL 'https://pastebin.com/raw/BaW6NL2L'"
            ),
            Some(Entry::ResourceLoadString(t("https://pastebin.com/raw/BaW6NL2L")))
        );
        assert_eq!(
            one(
                "2023.03.23 11:32:25 Log        -  [Image Download] Attempting to load image from URL 'https://i.imgur.com/lCfUMX0.jpeg'"
            ),
            Some(Entry::ResourceLoadImage(t("https://i.imgur.com/lCfUMX0.jpeg")))
        );
        assert!(one("2023.03.23 11:32:25 Log        -  [Image Download] Attempting to load image from URL 'http://127.0.0.1:22500/x.png'").is_none());
        assert_eq!(
            one(
                "2021.10.03 09:48:43 Log        -  [API] [101] Sending Get request to https://api.vrchat.cloud/api/1/users/usr_x?apiKey=k"
            ),
            Some(Entry::ApiRequest(t("https://api.vrchat.cloud/api/1/users/usr_x?apiKey=k")))
        );
        assert_eq!(
            one(
                "2021.01.03 05:48:58 Log        -  [API] Received Notification: < Notification from username:pypy, type:friendRequest> received at 01/02/2021 16:48:58 UTC"
            ),
            Some(Entry::Notification(t(" Notification from username:pypy, type:friendRequest")))
        );
        assert_eq!(
            one("2023.11.05 14:45:57 Log        -  [Behaviour] Switching K\u{2024}MOG to avatar MoeSera"),
            Some(Entry::AvatarChange(LogAvatarChange {
                display_name: "K\u{2024}MOG".into(),
                avatar_name: "MoeSera".into()
            }))
        );
        assert_eq!(
            one(
                "2024.01.01 00:00:00 Log        -  [VRCX] VideoPlay(PyPyDance) \"https://jd.pypy.moe/x.mp4\",0.5,260.6,\"Le Freak\""
            ),
            Some(Entry::Vrcx(t("VideoPlay(PyPyDance) \"https://jd.pypy.moe/x.mp4\",0.5,260.6,\"Le Freak\"")))
        );
        assert!(one("2024.01.01 00:00:00 Log        -  [VRCX-World] store:test:testvalue").is_none());
    }

    #[test]
    fn user_authenticated() {
        let expected = Some(Entry::UserAuthenticated(player(
            "Natsumi-sama".into(),
            "usr_032383a7-748c-4fb2-94e4-bcb928e5de6b".into(),
        )));
        assert_eq!(
            one(
                "2025.01.01 00:00:00 Debug      -  User Authenticated: Natsumi-sama (usr_032383a7-748c-4fb2-94e4-bcb928e5de6b)"
            ),
            expected
        );
        assert_eq!(
            one(
                "2025.01.01 00:00:00 Log        -  [Behaviour] User Authenticated: Natsumi-sama (usr_032383a7-748c-4fb2-94e4-bcb928e5de6b)"
            ),
            expected
        );
        assert!(one("2025.01.01 00:00:00 Log        -  User Authenticated: Natsumi-sama").is_none());
    }

    #[test]
    fn now_playing_lines_carry_their_json() {
        let json = r#"{"id":"Main-1","type":"mediaPlay","mediaTime":12.5}"#;
        assert_eq!(
            one(&format!("2026.10.25 12:00:00 Debug      -  nowplaying {json}")),
            Some(Entry::NowPlaying(LogText { text: json.to_owned() }))
        );
        // The marker may follow a prefix; the JSON runs to the last brace.
        assert_eq!(
            one(r#"2026.10.25 12:00:00 Debug      -  [TV] nowplaying {"id":"a","type":"mediaStop"} "#),
            None,
            "a [ line goes through the bracketed parsers, as in the original"
        );
        assert_eq!(
            one(r#"2026.10.25 12:00:00 Debug      -  ProTV nowplaying {"id":"a","type":"mediaStop"}"#),
            Some(Entry::NowPlaying(LogText { text: r#"{"id":"a","type":"mediaStop"}"#.to_owned() }))
        );
        assert!(one("2026.10.25 12:00:00 Debug      -  nowplaying nothing").is_none());
    }

    #[test]
    fn sticker_spawn_is_flipped() {
        assert_eq!(
            one(
                "2025.01.01 00:00:00 Log        -  [StickersManager] User usr_032383a7-748c-4fb2-94e4-bcb928e5de6b (Natsumi-sama) spawned sticker inv_8b380ee4-9a8a-484e-a0c3-b01290b92c6a"
            ),
            Some(Entry::StickerSpawn(LogStickerSpawn {
                user_id: "usr_032383a7-748c-4fb2-94e4-bcb928e5de6b".into(),
                display_name: "Natsumi-sama".into(),
                inventory_id: "inv_8b380ee4-9a8a-484e-a0c3-b01290b92c6a".into(),
            }))
        );
    }

    #[test]
    fn udon_exceptions_and_filters() {
        let mut ctx = LogContext::default();
        let cont = "VRC.Udon.VM.UdonVMException: outer ---> VRC.Udon.VM.UdonVMException: inner";
        let (dt, e) = parse_line(&mut ctx, cont, far_past(), far_future()).unwrap();
        assert_eq!(dt, far_future());
        assert_eq!(e, Entry::UdonException(LogText { text: " ---> VRC.Udon.VM.UdonVMException: inner".into() }));

        let line = "2021.06.23 12:02:56 Log        -  [Behaviour] OnPlayerJoined Bob";
        let dt_utc = line_time(line).unwrap();
        assert_eq!(dt_utc, Local.with_ymd_and_hms(2021, 6, 23, 12, 2, 56).unwrap().with_timezone(&Utc));
        assert!(parse_line(&mut ctx, line, dt_utc, far_future()).is_none());
        assert!(parse_line(&mut ctx, line, dt_utc - Duration::seconds(1), far_future()).is_some());
        assert!(parse_line(&mut ctx, line, far_past(), dt_utc - Duration::hours(2)).is_none());

        assert!(one("garbage").is_none());
        assert!(one("2021.06.23 12:02:56 Log        x  [Behaviour] OnPlayerJoined Bob").is_none());

        // After a dated line, a continuation line takes its time rather than "now", and is skipped
        // with it when the server already has that time.
        let (dt, _) = parse_line(&mut ctx, cont, far_past(), far_future()).unwrap();
        assert_eq!(dt, dt_utc);
        assert!(parse_line(&mut ctx, cont, dt_utc, far_future()).is_none());
    }
}
