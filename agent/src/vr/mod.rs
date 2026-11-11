//! The original's VR process (Dotnet/Overlay/Cef/VRCXVRCef.cs ThreadLoop) without its overlays yet:
//! joins SteamVR as a background app while the settings and the game want it, says whether the
//! headset is worn (IsHmdAfk) and keeps the device battery rows the wrist feed shows.

pub mod hmd;
mod openvr;
mod system;
pub mod wrist;

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use tokio::sync::watch;
use tracing::{info, warn};

use crate::game::GameStatus;
use crate::settings::Settings;

/// One tracked device as the original's GetVRDevices row: type, connected, charging, battery, tracking.
#[derive(Debug, Clone, PartialEq)]
pub struct Device {
    pub kind: &'static str,
    pub connected: bool,
    pub charging: bool,
    /// 0 to 100.
    pub battery: f32,
    pub tracking: &'static str,
}

static DEVICES: Mutex<Vec<Device>> = Mutex::new(Vec::new());
/// VRChat's log said it started in desktop mode (cleared by its OpenVR init line).
static GAME_NO_VR: AtomicBool = AtomicBool::new(false);

pub fn set_game_no_vr(no_vr: bool) {
    GAME_NO_VR.store(no_vr, Ordering::Relaxed);
}

/// The device rows, empty while OpenVR is off.
pub fn devices() -> Vec<Device> {
    DEVICES.lock().map(|d| d.clone()).unwrap_or_default()
}

/// The original's updateOpenVR condition for `active`.
pub fn wanted(settings: &Settings, game: GameStatus, no_vr: bool) -> bool {
    settings.open_vr && game.steamvr_running && ((game.running && !no_vr) || settings.open_vr_always)
}

/// AFK when the HMD's proximity sensor is not pressed.
pub fn hmd_afk(pressed: u64) -> bool {
    pressed & (1u64 << openvr::BUTTON_PROXIMITY_SENSOR) == 0
}

fn set_afk(game: &watch::Sender<GameStatus>, afk: bool) {
    game.send_if_modified(|status| std::mem::replace(&mut status.hmd_afk, afk) != afk);
}

/// Runs on its own thread for as long as the agent does: OpenVR calls block.
pub fn run(game: Arc<watch::Sender<GameStatus>>) {
    let mut library: Option<&'static openvr::Library> = None;
    let mut tried_library = false;
    let mut system: Option<openvr::System<'static>> = None;
    let mut next_init = Instant::now();
    let mut settings = Settings::load();
    let mut settings_read = Instant::now();
    let mut hmd = HmdOverlay::default();
    let mut wrist = WristOverlay::default();
    loop {
        std::thread::sleep(Duration::from_millis(100));
        if settings_read.elapsed() > Duration::from_secs(2) {
            settings = Settings::load();
            settings_read = Instant::now();
        }
        let status = *game.borrow();
        if !wanted(&settings, status, GAME_NO_VR.load(Ordering::Relaxed)) {
            if system.take().is_some() {
                hmd = HmdOverlay::default();
                wrist = WristOverlay::default();
                info!("OpenVR off");
                stopped(&game);
            }
            continue;
        }
        if system.is_none() {
            if Instant::now() < next_init {
                continue;
            }
            next_init = Instant::now() + Duration::from_secs(5);
            if !tried_library {
                tried_library = true;
                library = openvr::Library::load().map(|l| &*Box::leak(Box::new(l)));
                if library.is_none() {
                    warn!("openvr_api was not found beside the agent; HMD AFK and the VR overlays are off");
                }
            }
            let Some(lib) = library else { continue };
            match lib.connect() {
                Ok(s) => {
                    info!("OpenVR on");
                    system = Some(s);
                }
                Err(e) => {
                    warn!("OpenVR would not start: error {e}");
                    continue;
                }
            }
        }
        let Some(vr) = system.as_ref() else { continue };
        let mut quit = false;
        while let Some(event) = vr.poll_event() {
            if event.event_type == openvr::EVENT_QUIT {
                quit = true;
                break;
            }
        }
        if quit {
            info!("SteamVR is quitting");
            hmd = HmdOverlay::default();
            wrist = WristOverlay::default();
            system = None;
            next_init = Instant::now() + Duration::from_secs(10);
            stopped(&game);
            continue;
        }
        let held = update_devices(vr, &game, &settings);
        hmd.frame(vr);
        wrist.frame(vr, &settings, held);
    }
}

/// The HMD overlay ("VRCX2"): 1 m wide, 1.5 m ahead and 0.3 m down, shown while it has toasts or HUD lines and
/// the dashboard is closed, as ProcessOverlay2 placed it.
#[derive(Default)]
struct HmdOverlay {
    handle: Option<u64>,
    visible: bool,
    has_content: bool,
    dashboard: bool,
}

const HMD_TRANSFORM: openvr::Matrix34 = [[1.0, 0.0, 0.0, 0.0], [0.0, 1.0, 0.0, -0.3], [0.0, 0.0, 1.0, -1.5]];

impl HmdOverlay {
    fn frame(&mut self, vr: &openvr::System<'_>) {
        let Some(overlay) = vr.overlay() else { return };
        let dashboard = overlay.dashboard_visible();
        let changes = hmd::take_changes(Instant::now());
        if changes.is_none() && dashboard == self.dashboard {
            return;
        }
        self.dashboard = dashboard;
        if let Some(frame) = changes {
            self.has_content = !frame.is_empty();
            if self.has_content {
                let Some(handle) = self.handle(&overlay) else { return };
                if let Some(mut rgba) = hmd::render(&hmd::frame_svg(&frame), hmd::SIZE, hmd::SIZE) {
                    overlay.set_raw(handle, &mut rgba, hmd::SIZE, hmd::SIZE);
                }
            }
        }
        let show = self.has_content && !dashboard;
        if let Some(handle) = self.handle.filter(|_| show != self.visible) {
            if show {
                overlay.show(handle)
            } else {
                overlay.hide(handle)
            };
            self.visible = show;
        }
    }

    fn handle(&mut self, overlay: &openvr::Overlay) -> Option<u64> {
        if self.handle.is_none() {
            match overlay.find_or_create("VRCX2") {
                Ok(handle) => {
                    overlay.set_alpha(handle, 0.9);
                    overlay.set_width(handle, 1.0);
                    overlay.set_no_input(handle);
                    overlay.set_transform_relative(handle, 0, &HMD_TRANSFORM);
                    self.handle = Some(handle);
                    self.visible = false;
                }
                Err(e) => warn!("the HMD overlay could not be made: error {e}"),
            }
        }
        self.handle
    }
}

/// The wrist overlay ("VRCX1"): a quarter metre on the controller whose button is held, shown
/// for 10 s after it is let go, as ProcessOverlay1 did.
#[derive(Default)]
struct WristOverlay {
    handle: Option<u64>,
    visible: bool,
    until: Option<Instant>,
    drawn: Option<wrist::Live>,
}

/// Where the panel sits on each hand: the original's _translation and _rotation per side.
fn wrist_transform(left: bool) -> openvr::Matrix34 {
    use std::f32::consts::FRAC_PI_2;
    type M = [[f32; 4]; 4];
    let (t, r) = if left {
        ([-0.07, -0.05, 0.06], [FRAC_PI_2, FRAC_PI_2, -FRAC_PI_2])
    } else {
        ([0.07, -0.05, 0.06], [-FRAC_PI_2, -FRAC_PI_2, -FRAC_PI_2])
    };
    // System.Numerics row-vector matrices: scale, then X, Y and Z rotations, then translation.
    let mul =
        |a: M, b: M| -> M { std::array::from_fn(|i| std::array::from_fn(|j| (0..4).map(|k| a[i][k] * b[k][j]).sum())) };
    let (sx, cx) = r[0].sin_cos();
    let (sy, cy) = r[1].sin_cos();
    let (sz, cz) = r[2].sin_cos();
    let scale: M = [[0.25, 0.0, 0.0, 0.0], [0.0, 0.25, 0.0, 0.0], [0.0, 0.0, 0.25, 0.0], [0.0, 0.0, 0.0, 1.0]];
    let rx: M = [[1.0, 0.0, 0.0, 0.0], [0.0, cx, sx, 0.0], [0.0, -sx, cx, 0.0], [0.0, 0.0, 0.0, 1.0]];
    let ry: M = [[cy, 0.0, -sy, 0.0], [0.0, 1.0, 0.0, 0.0], [sy, 0.0, cy, 0.0], [0.0, 0.0, 0.0, 1.0]];
    let rz: M = [[cz, sz, 0.0, 0.0], [-sz, cz, 0.0, 0.0], [0.0, 0.0, 1.0, 0.0], [0.0, 0.0, 0.0, 1.0]];
    let tr: M = [[1.0, 0.0, 0.0, 0.0], [0.0, 1.0, 0.0, 0.0], [0.0, 0.0, 1.0, 0.0], [t[0], t[1], t[2], 1.0]];
    let m = mul(mul(mul(mul(scale, rx), ry), rz), tr);
    // HmdMatrix34 is the transpose: m0 = M11, m1 = M21, m3 = M41.
    std::array::from_fn(|row| std::array::from_fn(|col| m[col][row]))
}

/// The controller held for the wrist overlay, and which hand it is.
#[derive(Clone, Copy)]
struct Held {
    device: u32,
    left: bool,
}

/// The original's button check: menu (B/Y) when overlayMenuButton is on, else grip, which on
/// Oculus controllers is A/X.
fn wrist_button_held(settings: &Settings, oculus: bool, pressed: u64) -> bool {
    let mask = if settings.overlay_menu_button {
        2
    } else if oculus {
        128
    } else {
        4
    };
    pressed & mask != 0
}

/// overlayHand: 0 either, 1 left, 2 right.
fn wrist_hand_allowed(settings: &Settings, left: bool) -> bool {
    match settings.overlay_hand {
        1 => left,
        2 => !left,
        _ => true,
    }
}

impl WristOverlay {
    fn frame(&mut self, vr: &openvr::System<'_>, settings: &Settings, held: Option<Held>) {
        let Some(overlay) = vr.overlay() else { return };
        if !settings.wrist_overlay {
            if let (Some(handle), true) = (self.handle, self.visible) {
                overlay.hide(handle);
                self.visible = false;
            }
            return;
        }
        let Some(handle) = self.handle(&overlay) else { return };
        let now = Instant::now();
        if let Some(held) = held {
            self.until = Some(now + Duration::from_secs(10));
            overlay.set_transform_relative(handle, held.device, &wrist_transform(held.left));
        }
        let show = !overlay.dashboard_visible() && self.until.is_some_and(|until| now <= until);
        if show {
            let (feed, changed) = wrist::feed();
            let now = chrono::Local::now();
            let live = wrist::Live {
                clock: now.format(if feed.hour12 { "%m/%d/%Y %I:%M:%S %p" } else { "%m/%d/%Y %H:%M:%S" }).to_string(),
                now_ms: now.timestamp_millis(),
                cpu: feed.cpu_usage.then(|| system::cpu_percent().round() as u32),
                pc_uptime_ms: if feed.pc_uptime { system::uptime().map(|u| u.as_millis() as i64) } else { None },
                playing: hmd::now_playing(now.timestamp_millis() as f64 / 1000.0),
                devices: devices(),
            };
            if changed || self.drawn.as_ref() != Some(&live) {
                if let Some(mut rgba) = hmd::render(&wrist::svg(&feed, &live), wrist::SIZE, wrist::SIZE) {
                    overlay.set_raw(handle, &mut rgba, wrist::SIZE, wrist::SIZE);
                }
                self.drawn = Some(live);
            }
        }
        if show != self.visible {
            if show {
                overlay.show(handle)
            } else {
                overlay.hide(handle)
            };
            self.visible = show;
        }
    }

    fn handle(&mut self, overlay: &openvr::Overlay) -> Option<u64> {
        if self.handle.is_none() {
            match overlay.find_or_create("VRCX1") {
                Ok(handle) => {
                    overlay.set_alpha(handle, 0.9);
                    overlay.set_width(handle, 1.0);
                    overlay.set_no_input(handle);
                    self.handle = Some(handle);
                    self.visible = false;
                }
                Err(e) => warn!("the wrist overlay could not be made: error {e}"),
            }
        }
        self.handle
    }
}

fn stopped(game: &watch::Sender<GameStatus>) {
    set_afk(game, false);
    hmd::clear();
    if let Ok(mut d) = DEVICES.lock() {
        d.clear();
    }
}

fn update_devices(vr: &openvr::System<'_>, game: &watch::Sender<GameStatus>, settings: &Settings) -> Option<Held> {
    let poses = vr.poses();
    let mut list = Vec::new();
    let mut held = None;
    for i in 0..openvr::MAX_DEVICES as u32 {
        let class = vr.device_class(i);
        let tracking = openvr::tracking_result_name(poses[i as usize].tracking_result);
        match class {
            openvr::CLASS_HMD => {
                // Fails while the SteamVR dashboard is open: the AFK state holds.
                let Some(state) = vr.controller_state(i) else { continue };
                set_afk(game, hmd_afk(state.pressed));
                // A headset with no battery has no row.
                let Some(battery) = vr.float_property(i, openvr::PROP_BATTERY_PERCENTAGE) else { continue };
                // Neither Virtual Desktop nor Steam Link reports charging.
                list.push(Device {
                    kind: "headset",
                    connected: vr.is_connected(i),
                    charging: false,
                    battery: battery * 100.0,
                    tracking,
                });
            }
            openvr::CLASS_CONTROLLER | openvr::CLASS_TRACKER | openvr::CLASS_TRACKING_REFERENCE => {
                let battery = vr.float_property(i, openvr::PROP_BATTERY_PERCENTAGE).unwrap_or(1.0);
                let charging = vr.bool_property(i, openvr::PROP_IS_CHARGING).unwrap_or(false);
                let role = vr.controller_role(i);
                let left = role == openvr::ROLE_LEFT;
                if (left || role == openvr::ROLE_RIGHT) && wrist_hand_allowed(settings, left) {
                    let oculus = vr
                        .string_property(i, openvr::PROP_TRACKING_SYSTEM_NAME)
                        .is_some_and(|name| name.to_lowercase().contains("oculus"));
                    if vr.controller_state(i).is_some_and(|state| wrist_button_held(settings, oculus, state.pressed)) {
                        held = Some(Held { device: i, left });
                    }
                }
                let kind = match class {
                    openvr::CLASS_CONTROLLER => match role {
                        openvr::ROLE_LEFT => "leftController",
                        openvr::ROLE_RIGHT => "rightController",
                        _ => "controller",
                    },
                    openvr::CLASS_TRACKER => "tracker",
                    _ => "base",
                };
                list.push(Device { kind, connected: vr.is_connected(i), charging, battery: battery * 100.0, tracking });
            }
            _ => {}
        }
    }
    if let Ok(mut d) = DEVICES.lock() {
        *d = list;
    }
    held
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn openvr_runs_while_the_original_set_it_active() {
        let on = Settings { open_vr: true, ..Settings::default() };
        let vr_game = GameStatus { running: true, steamvr_running: true, ..GameStatus::default() };
        assert!(wanted(&on, vr_game, false));
        assert!(!wanted(&on, vr_game, true), "VRChat in desktop mode");
        assert!(!wanted(&Settings::default(), vr_game, false), "the setting is off");
        let closed = GameStatus { running: false, steamvr_running: true, ..GameStatus::default() };
        assert!(!wanted(&on, closed, false));
        assert!(wanted(&Settings { open_vr_always: true, ..on.clone() }, closed, false));
        assert!(
            !wanted(&Settings { open_vr_always: true, ..on }, GameStatus::default(), false),
            "SteamVR is not running"
        );
    }

    #[test]
    fn the_wrist_panel_sits_where_the_original_put_it() {
        let left = wrist_transform(true);
        // The translation column, and a quarter-metre scale on each axis.
        assert_eq!([left[0][3], left[1][3], left[2][3]], [-0.07, -0.05, 0.06]);
        for row in left {
            let len: f32 = row[..3].iter().map(|v| v * v).sum::<f32>().sqrt();
            assert!((len - 0.25).abs() < 1e-5);
        }
        assert_eq!(wrist_transform(false)[0][3], 0.07);
    }

    #[test]
    fn the_wrist_button_and_hand_follow_the_settings() {
        let grip = Settings::default();
        assert!(wrist_button_held(&grip, false, 4));
        assert!(!wrist_button_held(&grip, false, 2));
        assert!(wrist_button_held(&grip, true, 128), "A/X on Oculus");
        let menu = Settings { overlay_menu_button: true, overlay_hand: 1, ..Settings::default() };
        assert!(wrist_button_held(&menu, true, 2));
        assert!(wrist_hand_allowed(&menu, true) && !wrist_hand_allowed(&menu, false));
        assert!(wrist_hand_allowed(&grip, false));
    }

    #[test]
    fn the_proximity_sensor_says_whether_the_headset_is_worn() {
        assert!(hmd_afk(0));
        assert!(!hmd_afk(1 << 31));
        assert!(hmd_afk(1 << 2));
    }
}
