//! Export to Calendar (the original's `OpenCalendarFile`): the event's .ics written to the agent's
//! folder as event.ics and opened with the PC's calendar app.

use super::{Answer, blocking};
use crate::platform;
use crate::proto::{Capability, OpenCalendarFile};

pub const CAPABILITIES: [Capability; 1] = [Capability::Calendar];

/// The original's check: an iCalendar file from start to end.
pub fn is_calendar(ics: &str) -> bool {
    ics.starts_with("BEGIN:VCALENDAR") && ics.trim_end().ends_with("END:VCALENDAR")
}

pub async fn open(open: OpenCalendarFile) -> Answer {
    if !is_calendar(&open.ics) {
        return Answer::failed("Invalid calendar file".to_string());
    }
    blocking(move || {
        let path = platform::data_dir().join("event.ics");
        std::fs::create_dir_all(platform::data_dir())
            .and_then(|()| std::fs::write(&path, &open.ics))
            .and_then(|()| platform::open_url(&path.display().to_string()))
            .map(|()| Answer::ok())
            .into()
    })
    .await
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_a_whole_calendar_is_opened() {
        assert!(is_calendar("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n"));
        assert!(!is_calendar("<html>"));
        assert!(!is_calendar("BEGIN:VCALENDAR\r\n"));
    }
}
