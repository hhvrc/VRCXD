//! The few OpenVR calls the agent makes, through `openvr_api`'s C function tables (openvr_capi.h),
//! loaded at run time: the agent starts on PCs without SteamVR, and the original shipped the same
//! library beside its exe (Dotnet/libs/openvr_api.dll).

use std::ffi::{CString, c_char, c_void};
use std::path::{Path, PathBuf};

/// `EVRApplicationType::VRApplication_Background`: never starts SteamVR, only joins it.
const APPLICATION_BACKGROUND: i32 = 3;

pub const MAX_DEVICES: usize = 64;

pub const CLASS_HMD: i32 = 1;
pub const CLASS_CONTROLLER: i32 = 2;
pub const CLASS_TRACKER: i32 = 3;
pub const CLASS_TRACKING_REFERENCE: i32 = 4;

pub const ROLE_LEFT: i32 = 1;
pub const ROLE_RIGHT: i32 = 2;

pub const PROP_TRACKING_SYSTEM_NAME: i32 = 1000;
pub const PROP_IS_CHARGING: i32 = 1011;
pub const PROP_BATTERY_PERCENTAGE: i32 = 1012;

pub const BUTTON_PROXIMITY_SENSOR: u32 = 31;
pub const EVENT_QUIT: u32 = 700;

/// `VREvent_t` on Windows and 64-bit unix: 12 bytes of header, then the 8-aligned 48-byte data union.
#[repr(C)]
#[derive(Clone, Copy)]
pub struct Event {
    pub event_type: u32,
    pub device: u32,
    pub age: f32,
    _pad: u32,
    _data: [u64; 6],
}

/// `VRControllerState_t`.
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct ControllerState {
    pub packet: u32,
    pub pressed: u64,
    pub touched: u64,
    pub axis: [[f32; 2]; 5],
}

/// `TrackedDevicePose_t`.
#[repr(C)]
#[derive(Clone, Copy, Default)]
pub struct Pose {
    pub to_absolute: [[f32; 4]; 3],
    pub velocity: [f32; 3],
    pub angular_velocity: [f32; 3],
    pub tracking_result: i32,
    pub valid: bool,
    pub connected: bool,
}

const _: () = assert!(std::mem::size_of::<Event>() == 64);
const _: () = assert!(std::mem::size_of::<ControllerState>() == 64);
const _: () = assert!(std::mem::size_of::<Pose>() == 80);

/// `ETrackingResult` by name, as the original's device rows printed it.
pub fn tracking_result_name(result: i32) -> &'static str {
    match result {
        1 => "Uninitialized",
        100 => "Calibrating_InProgress",
        101 => "Calibrating_OutOfRange",
        200 => "Running_OK",
        201 => "Running_OutOfRange",
        300 => "Fallback_RotationOnly",
        _ => "",
    }
}

type InitFn = unsafe extern "C" fn(*mut i32, i32, *const c_char) -> u32;
type ShutdownFn = unsafe extern "C" fn();
type GetInterfaceFn = unsafe extern "C" fn(*const c_char, *mut i32) -> *mut c_void;

/// The loaded library. Dropping it does not unload it: OpenVR keeps threads of its own.
pub struct Library {
    init: InitFn,
    shutdown: ShutdownFn,
    get_interface: GetInterfaceFn,
}

#[cfg(windows)]
const LIBRARY: &str = "openvr_api.dll";
#[cfg(not(windows))]
const LIBRARY: &str = "libopenvr_api.so";

/// Beside the agent, as the original shipped it.
fn candidates() -> Vec<PathBuf> {
    let mut paths = Vec::new();
    if let Some(dir) = std::env::current_exe().ok().and_then(|p| p.parent().map(Path::to_path_buf)) {
        paths.push(dir.join(LIBRARY));
        paths.push(dir.join("libs").join(LIBRARY));
    }
    paths
}

impl Library {
    pub fn load() -> Option<Self> {
        candidates().into_iter().filter(|p| p.is_file()).find_map(|p| unsafe { Self::open(&p) })
    }

    #[cfg(windows)]
    unsafe fn open(path: &Path) -> Option<Self> {
        use std::os::windows::ffi::OsStrExt;
        use windows_sys::Win32::System::LibraryLoader::{GetProcAddress, LoadLibraryW};
        let wide: Vec<u16> = path.as_os_str().encode_wide().chain(Some(0)).collect();
        let module = unsafe { LoadLibraryW(wide.as_ptr()) };
        if module.is_null() {
            return None;
        }
        let symbol = |name: &[u8]| unsafe { GetProcAddress(module, name.as_ptr()) }.map(|f| f as *const c_void);
        unsafe {
            Self::from_symbols(
                symbol(b"VR_InitInternal2\0")?,
                symbol(b"VR_ShutdownInternal\0")?,
                symbol(b"VR_GetGenericInterface\0")?,
            )
        }
    }

    #[cfg(not(windows))]
    unsafe fn open(path: &Path) -> Option<Self> {
        use std::os::unix::ffi::OsStrExt;
        let name = CString::new(path.as_os_str().as_bytes()).ok()?;
        let module = unsafe { libc::dlopen(name.as_ptr(), libc::RTLD_NOW) };
        if module.is_null() {
            return None;
        }
        let symbol = |name: &[u8]| {
            let f = unsafe { libc::dlsym(module, name.as_ptr().cast()) };
            (!f.is_null()).then_some(f as *const c_void)
        };
        unsafe {
            Self::from_symbols(
                symbol(b"VR_InitInternal2\0")?,
                symbol(b"VR_ShutdownInternal\0")?,
                symbol(b"VR_GetGenericInterface\0")?,
            )
        }
    }

    unsafe fn from_symbols(init: *const c_void, shutdown: *const c_void, get_interface: *const c_void) -> Option<Self> {
        unsafe {
            Some(Self {
                init: std::mem::transmute::<*const c_void, InitFn>(init),
                shutdown: std::mem::transmute::<*const c_void, ShutdownFn>(shutdown),
                get_interface: std::mem::transmute::<*const c_void, GetInterfaceFn>(get_interface),
            })
        }
    }

    /// Joins a running SteamVR as a background application.
    pub fn connect(&self) -> Result<System<'_>, i32> {
        let mut error = 0;
        unsafe { (self.init)(&mut error, APPLICATION_BACKGROUND, std::ptr::null()) };
        if error != 0 {
            return Err(error);
        }
        // IVRSystem_023 added PollNextEventWithPoseAndOverlays before GetControllerState.
        for (version, controller_state) in [("FnTable:IVRSystem_023", 30), ("FnTable:IVRSystem_022", 29)] {
            let name = CString::new(version).expect("no nul");
            let mut error = 0;
            let table = unsafe { (self.get_interface)(name.as_ptr(), &mut error) };
            if error == 0 && !table.is_null() {
                return Ok(System { library: self, table: table.cast(), controller_state });
            }
        }
        unsafe { (self.shutdown)() };
        Err(-1)
    }
}

/// `HmdMatrix34_t`: rows of a 3x4 transform, translation in the last column.
pub type Matrix34 = [[f32; 4]; 3];

/// Where each `IVROverlay` call sits in its function table; _028 added two before DestroyOverlay's
/// neighbours, so the indices move.
#[derive(Clone, Copy)]
struct OverlayIndices {
    find: usize,
    create: usize,
    alpha: usize,
    width: usize,
    transform_relative: usize,
    show: usize,
    hide: usize,
    input_method: usize,
    raw: usize,
    dashboard_visible: usize,
}

const OVERLAY_028: OverlayIndices = OverlayIndices {
    find: 0,
    create: 1,
    alpha: 16,
    width: 22,
    transform_relative: 35,
    show: 43,
    hide: 44,
    input_method: 50,
    raw: 62,
    dashboard_visible: 68,
};
const OVERLAY_027: OverlayIndices = OverlayIndices {
    find: 0,
    create: 1,
    alpha: 15,
    width: 21,
    transform_relative: 34,
    show: 41,
    hide: 42,
    input_method: 48,
    raw: 60,
    dashboard_visible: 66,
};

/// An `IVROverlay` function table, valid while its `System` is.
pub struct Overlay {
    table: *const *const c_void,
    at: OverlayIndices,
}

/// An `IVRSystem` function table; shutting OpenVR down when dropped.
pub struct System<'a> {
    library: &'a Library,
    table: *const *const c_void,
    controller_state: usize,
}

impl Drop for System<'_> {
    fn drop(&mut self) {
        unsafe { (self.library.shutdown)() };
    }
}

macro_rules! call {
    ($self:ident, $index:expr, fn($($arg:ty),*) -> $ret:ty, $($value:expr),*) => {{
        let f = unsafe { *$self.table.add($index) };
        let f: unsafe extern "system" fn($($arg),*) -> $ret = unsafe { std::mem::transmute(f) };
        unsafe { f($($value),*) }
    }};
}

impl System<'_> {
    pub fn overlay(&self) -> Option<Overlay> {
        for (version, at) in [("FnTable:IVROverlay_028", OVERLAY_028), ("FnTable:IVROverlay_027", OVERLAY_027)] {
            let name = CString::new(version).expect("no nul");
            let mut error = 0;
            let table = unsafe { (self.library.get_interface)(name.as_ptr(), &mut error) };
            if error == 0 && !table.is_null() {
                return Some(Overlay { table: table.cast(), at });
            }
        }
        None
    }

    pub fn poses(&self) -> [Pose; MAX_DEVICES] {
        let mut poses = [Pose::default(); MAX_DEVICES];
        // TrackingUniverseStanding
        call!(self, 10, fn(i32, f32, *mut Pose, u32) -> (), 1, 0.0, poses.as_mut_ptr(), MAX_DEVICES as u32);
        poses
    }

    pub fn controller_role(&self, device: u32) -> i32 {
        call!(self, 17, fn(u32) -> i32, device)
    }

    pub fn device_class(&self, device: u32) -> i32 {
        call!(self, 18, fn(u32) -> i32, device)
    }

    pub fn is_connected(&self, device: u32) -> bool {
        call!(self, 19, fn(u32) -> bool, device)
    }

    pub fn bool_property(&self, device: u32, prop: i32) -> Option<bool> {
        let mut error = 0;
        let value = call!(self, 20, fn(u32, i32, *mut i32) -> bool, device, prop, &mut error);
        (error == 0).then_some(value)
    }

    pub fn float_property(&self, device: u32, prop: i32) -> Option<f32> {
        let mut error = 0;
        let value = call!(self, 21, fn(u32, i32, *mut i32) -> f32, device, prop, &mut error);
        (error == 0).then_some(value)
    }

    pub fn string_property(&self, device: u32, prop: i32) -> Option<String> {
        let mut error = 0;
        let mut buffer = [0u8; 256];
        let len = call!(
            self,
            23,
            fn(u32, i32, *mut u8, u32, *mut i32) -> u32,
            device,
            prop,
            buffer.as_mut_ptr(),
            buffer.len() as u32,
            &mut error
        );
        let end = (len as usize).saturating_sub(1).min(buffer.len());
        (error == 0).then(|| String::from_utf8_lossy(&buffer[..end]).into_owned())
    }

    pub fn poll_event(&self) -> Option<Event> {
        let mut event = Event { event_type: 0, device: 0, age: 0.0, _pad: 0, _data: [0; 6] };
        let got = call!(self, 25, fn(*mut Event, u32) -> bool, &mut event, std::mem::size_of::<Event>() as u32);
        got.then_some(event)
    }

    /// False while the SteamVR dashboard is open, as the original noted.
    pub fn controller_state(&self, device: u32) -> Option<ControllerState> {
        let mut state = ControllerState::default();
        let ok = call!(
            self,
            self.controller_state,
            fn(u32, *mut ControllerState, u32) -> bool,
            device,
            &mut state,
            std::mem::size_of::<ControllerState>() as u32
        );
        ok.then_some(state)
    }
}

impl Overlay {
    /// The overlay with this key, made if it is not there yet.
    pub fn find_or_create(&self, key: &str) -> Result<u64, i32> {
        let key = CString::new(key).map_err(|_| -1)?;
        let mut handle = 0u64;
        let error = call!(self, self.at.find, fn(*const c_char, *mut u64) -> i32, key.as_ptr(), &mut handle);
        if error == 0 {
            return Ok(handle);
        }
        let error = call!(
            self,
            self.at.create,
            fn(*const c_char, *const c_char, *mut u64) -> i32,
            key.as_ptr(),
            key.as_ptr(),
            &mut handle
        );
        if error == 0 { Ok(handle) } else { Err(error) }
    }

    pub fn set_alpha(&self, handle: u64, alpha: f32) -> i32 {
        call!(self, self.at.alpha, fn(u64, f32) -> i32, handle, alpha)
    }

    pub fn set_width(&self, handle: u64, meters: f32) -> i32 {
        call!(self, self.at.width, fn(u64, f32) -> i32, handle, meters)
    }

    /// No input: the overlays only show.
    pub fn set_no_input(&self, handle: u64) -> i32 {
        call!(self, self.at.input_method, fn(u64, i32) -> i32, handle, 0)
    }

    pub fn set_transform_relative(&self, handle: u64, device: u32, transform: &Matrix34) -> i32 {
        call!(self, self.at.transform_relative, fn(u64, u32, *const Matrix34) -> i32, handle, device, transform)
    }

    pub fn show(&self, handle: u64) -> i32 {
        call!(self, self.at.show, fn(u64) -> i32, handle)
    }

    pub fn hide(&self, handle: u64) -> i32 {
        call!(self, self.at.hide, fn(u64) -> i32, handle)
    }

    /// Straight (not premultiplied) RGBA, row by row.
    pub fn set_raw(&self, handle: u64, rgba: &mut [u8], width: u32, height: u32) -> i32 {
        call!(
            self,
            self.at.raw,
            fn(u64, *mut c_void, u32, u32, u32) -> i32,
            handle,
            rgba.as_mut_ptr().cast(),
            width,
            height,
            4
        )
    }

    pub fn dashboard_visible(&self) -> bool {
        call!(self, self.at.dashboard_visible, fn() -> bool,)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_shipped_library_has_the_entry_points() {
        // Loads only: connecting would join this PC's SteamVR.
        let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("assets").join(LIBRARY);
        assert!(unsafe { Library::open(&path) }.is_some(), "{}", path.display());
        assert_eq!(tracking_result_name(200), "Running_OK");
    }
}
