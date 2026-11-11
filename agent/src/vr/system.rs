//! The PC's CPU load and uptime for the wrist overlay's bottom bar (the original's SystemMonitor
//! CpuUsage and AppApiVr.GetUptime).

use std::sync::Mutex;
use std::time::{Duration, Instant};

/// Busy and total CPU time so far, in any unit.
fn cpu_times() -> Option<(u64, u64)> {
    #[cfg(windows)]
    {
        use windows_sys::Win32::Foundation::FILETIME;
        use windows_sys::Win32::System::Threading::GetSystemTimes;
        let zero = FILETIME { dwLowDateTime: 0, dwHighDateTime: 0 };
        let (mut idle, mut kernel, mut user) = (zero, zero, zero);
        if unsafe { GetSystemTimes(&mut idle, &mut kernel, &mut user) } == 0 {
            return None;
        }
        let ticks = |t: FILETIME| ((t.dwHighDateTime as u64) << 32) | t.dwLowDateTime as u64;
        // Kernel time includes idle time.
        let total = ticks(kernel) + ticks(user);
        Some((total.saturating_sub(ticks(idle)), total))
    }
    #[cfg(not(windows))]
    {
        let stat = std::fs::read_to_string("/proc/stat").ok()?;
        parse_proc_stat(&stat)
    }
}

/// The first "cpu" line of /proc/stat: idle and iowait are not busy.
#[cfg_attr(windows, allow(dead_code))]
fn parse_proc_stat(stat: &str) -> Option<(u64, u64)> {
    let line = stat.lines().find(|l| l.starts_with("cpu "))?;
    let values: Vec<u64> = line.split_whitespace().skip(1).filter_map(|v| v.parse().ok()).collect();
    let total: u64 = values.iter().sum();
    let idle = values.get(3).copied().unwrap_or(0) + values.get(4).copied().unwrap_or(0);
    Some((total.saturating_sub(idle), total))
}

/// The load between two samples, 0 to 100.
fn percent(before: (u64, u64), after: (u64, u64)) -> f32 {
    let busy = after.0.saturating_sub(before.0) as f32;
    let total = after.1.saturating_sub(before.1) as f32;
    if total <= 0.0 { 0.0 } else { (busy / total * 100.0).clamp(0.0, 100.0) }
}

struct Sampler {
    last: Option<(Instant, (u64, u64))>,
    value: f32,
}

static CPU: Mutex<Sampler> = Mutex::new(Sampler { last: None, value: 0.0 });

/// The CPU load over the last second or so, as the original's performance counter gave it.
pub fn cpu_percent() -> f32 {
    let Ok(mut s) = CPU.lock() else { return 0.0 };
    let now = Instant::now();
    match s.last {
        Some((at, _)) if now - at < Duration::from_secs(1) => {}
        Some((_, before)) => {
            if let Some(after) = cpu_times() {
                s.value = percent(before, after);
                s.last = Some((now, after));
            }
        }
        None => s.last = cpu_times().map(|t| (now, t)),
    }
    s.value
}

/// How long the PC has been up.
pub fn uptime() -> Option<Duration> {
    #[cfg(windows)]
    {
        Some(Duration::from_millis(unsafe { windows_sys::Win32::System::SystemInformation::GetTickCount64() }))
    }
    #[cfg(not(windows))]
    {
        let text = std::fs::read_to_string("/proc/uptime").ok()?;
        let secs: f64 = text.split_whitespace().next()?.parse().ok()?;
        Some(Duration::from_secs_f64(secs))
    }
}

/// The original's timeToText: the largest units down to minutes, seconds (rounded to five) only
/// under a minute.
pub fn time_to_text(ms: i64) -> String {
    let mut n = ms.unsigned_abs() / 1000;
    let mut parts = Vec::new();
    for (unit, size) in [("y", 31_536_000), ("d", 86_400), ("h", 3_600), ("m", 60)] {
        if n >= size {
            parts.push(format!("{}{unit}", n / size));
            n %= size;
        }
    }
    if parts.is_empty() {
        parts.push(format!("{}s", (n + 2) / 5 * 5));
    }
    parts.join(" ")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn times_read_as_the_original_wrote_them() {
        assert_eq!(time_to_text(9_000), "10s");
        assert_eq!(time_to_text(3_725_000), "1h 2m");
        assert_eq!(time_to_text(90_061_000), "1d 1h 1m");
        assert_eq!(time_to_text(0), "0s");
    }

    #[test]
    fn the_load_comes_from_busy_time_over_total_time() {
        let stat = "cpu  100 0 100 700 100 0 0 0 0 0\ncpu0 1 2 3 4\n";
        assert_eq!(parse_proc_stat(stat), Some((200, 1000)));
        assert_eq!(percent((200, 1000), (250, 1100)), 50.0);
        assert_eq!(percent((5, 5), (5, 5)), 0.0);
        assert!(uptime().is_some_and(|u| u > Duration::ZERO));
        let _ = cpu_percent();
    }
}
