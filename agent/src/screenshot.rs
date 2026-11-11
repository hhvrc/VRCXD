//! `ProcessScreenshot`: the original's `AddScreenshotMetadata` (`Dotnet/AppApi/Cef/Screenshot.cs`,
//! `PNGFile`, `PNGHelper`). Renames the picture with the world id when asked, writes the VRCX
//! metadata JSON as a `Description` iTXt chunk byte for byte as the original did, and copies the
//! picture to the clipboard when asked.

use std::path::{Path, PathBuf};
use std::time::Duration;

use tracing::{info, warn};

use crate::proto::ProcessScreenshot;

/// The original's own loop: ten tries, a second apart, while VRChat still holds the file.
const MAX_ATTEMPTS: u32 = 10;
const RETRY_DELAY: Duration = Duration::from_secs(1);

/// Only VRChat's own screenshots are touched.
fn is_vrchat_screenshot(path: &Path) -> bool {
    path.is_file()
        && path.extension().is_some_and(|e| e.eq_ignore_ascii_case("png"))
        && path.file_stem().and_then(|s| s.to_str()).is_some_and(|s| s.starts_with("VRChat_"))
}

pub async fn process(action: ProcessScreenshot) {
    let path = PathBuf::from(&action.path);
    if !is_vrchat_screenshot(&path) {
        warn!("screenshot action for an unexpected path, skipping: {}", action.path);
        return;
    }
    let mut attempts = MAX_ATTEMPTS;
    while !crate::platform::is_file_unlocked(&path) {
        attempts -= 1;
        if attempts == 0 {
            warn!("screenshot still locked after retries, giving up: {}", path.display());
            return;
        }
        tokio::time::sleep(RETRY_DELAY).await;
    }
    let result = tokio::task::spawn_blocking(move || apply(&path, &action)).await;
    if let Err(e) = result {
        warn!("screenshot processing failed: {e}");
    }
}

fn apply(path: &Path, action: &ProcessScreenshot) {
    let mut path = path.to_owned();
    if action.append_world_id && !action.world_id.is_empty() {
        let stem = path.file_stem().unwrap_or_default().to_string_lossy();
        let ext = path.extension().unwrap_or_default().to_string_lossy();
        let renamed = path.with_file_name(format!("{stem}_{}.{ext}", action.world_id));
        match std::fs::rename(&path, &renamed) {
            Ok(()) => path = renamed,
            Err(e) => warn!("failed to rename screenshot {} to {}: {e}", path.display(), renamed.display()),
        }
    }
    if !action.metadata_json.is_empty() {
        match std::fs::read(&path)
            .map_err(|e| e.to_string())
            .and_then(|bytes| with_description(&bytes, &action.metadata_json).ok_or_else(|| "not a PNG".to_owned()))
        {
            Ok(bytes) => match std::fs::write(&path, bytes) {
                Ok(()) => info!("wrote screenshot metadata {}", path.display()),
                Err(e) => warn!("failed to write screenshot metadata {}: {e}", path.display()),
            },
            Err(e) => warn!("failed to write screenshot metadata {}: {e}", path.display()),
        }
    }
    if action.copy_to_clipboard {
        match copy_to_clipboard(&path) {
            Ok(()) => info!("copied screenshot to the clipboard {}", path.display()),
            Err(e) => warn!("failed to copy screenshot to the clipboard {}: {e}", path.display()),
        }
    }
}

const SIGNATURE: [u8; 8] = [0x89, b'P', b'N', b'G', b'\r', b'\n', 0x1a, b'\n'];

/// The PNG with an iTXt chunk holding keyword `Description` and `text` (no compression, no language
/// tag or translated keyword) inserted before the first IDAT or IEND chunk, where the original's
/// `PNGFile.ReadChunks` stops. `None` when it is not a PNG.
pub fn with_description(png: &[u8], text: &str) -> Option<Vec<u8>> {
    if png.len() < 8 || png[..8] != SIGNATURE {
        return None;
    }
    let mut pos = 8;
    let mut insert_at = png.len();
    while pos + 8 <= png.len() {
        let length = u32::from_be_bytes(png[pos..pos + 4].try_into().ok()?) as usize;
        let kind = &png[pos + 4..pos + 8];
        if kind == b"IDAT" || kind == b"IEND" {
            insert_at = pos;
            break;
        }
        pos += 8 + length + 4;
    }
    let mut data = b"Description".to_vec();
    // Keyword separator, compression flag, compression method, empty language tag and its
    // separator, empty translated keyword and its separator.
    data.extend_from_slice(&[0, 0, 0, 0, 0]);
    data.extend_from_slice(text.as_bytes());

    let mut chunk = Vec::with_capacity(data.len() + 12);
    chunk.extend_from_slice(&(data.len() as u32).to_be_bytes());
    chunk.extend_from_slice(b"iTXt");
    chunk.extend_from_slice(&data);
    chunk.extend_from_slice(&crc32(b"iTXt", &data).to_be_bytes());

    let mut out = Vec::with_capacity(png.len() + chunk.len());
    out.extend_from_slice(&png[..insert_at.min(png.len())]);
    out.extend_from_slice(&chunk);
    out.extend_from_slice(&png[insert_at.min(png.len())..]);
    Some(out)
}

/// The PNG without its first iTXt chunk with this keyword (the original's `PNGHelper.DeleteTextChunk`,
/// which looks only at the chunks before the image data). `None` when it is not a PNG or has none.
pub fn without_text_chunk(png: &[u8], keyword: &str) -> Option<Vec<u8>> {
    if png.len() < 8 || png[..8] != SIGNATURE {
        return None;
    }
    let mut pos = 8;
    while pos + 8 <= png.len() {
        let length = u32::from_be_bytes(png[pos..pos + 4].try_into().ok()?) as usize;
        let kind = &png[pos + 4..pos + 8];
        let end = pos.checked_add(12 + length).filter(|&e| e <= png.len())?;
        if kind == b"IDAT" || kind == b"IEND" {
            return None;
        }
        let data = &png[pos + 8..pos + 8 + length];
        if kind == b"iTXt" && data.split(|&b| b == 0).next() == Some(keyword.as_bytes()) {
            let mut out = Vec::with_capacity(png.len() - (end - pos));
            out.extend_from_slice(&png[..pos]);
            out.extend_from_slice(&png[end..]);
            return Some(out);
        }
        pos = end;
    }
    None
}

/// DeleteTextMetadata(path, true): the VRChat XMP chunk and VRCX's Description chunk. Whether the
/// file changed.
pub fn delete_metadata(path: &Path) -> std::io::Result<bool> {
    let original = std::fs::read(path)?;
    let mut png = original.clone();
    for keyword in ["XML:com.adobe.xmp", "Description"] {
        if let Some(stripped) = without_text_chunk(&png, keyword) {
            png = stripped;
        }
    }
    if png == original {
        return Ok(false);
    }
    let temp = path.with_extension("png.vrcxd-tmp");
    std::fs::write(&temp, &png)?;
    std::fs::rename(&temp, path)?;
    Ok(true)
}

/// Every .png under `dir`, at any depth.
pub fn pngs_under(dir: &Path) -> Vec<std::path::PathBuf> {
    let mut found = Vec::new();
    let mut pending = vec![dir.to_path_buf()];
    while let Some(next) = pending.pop() {
        let Ok(entries) = std::fs::read_dir(&next) else { continue };
        for entry in entries.filter_map(Result::ok) {
            let path = entry.path();
            match entry.file_type() {
                Ok(t) if t.is_dir() => pending.push(path),
                Ok(t) if t.is_file() && path.extension().is_some_and(|e| e == "png") => found.push(path),
                _ => {}
            }
        }
    }
    found
}

/// The PNG CRC-32 over the chunk type and data (`PNGChunk.Crc32`).
fn crc32(kind: &[u8], data: &[u8]) -> u32 {
    let mut c = 0xFFFF_FFFFu32;
    for &b in kind.iter().chain(data) {
        c ^= u32::from(b);
        for _ in 0..8 {
            c = if c & 1 != 0 { 0xEDB8_8320 ^ (c >> 1) } else { c >> 1 };
        }
    }
    c ^ 0xFFFF_FFFF
}

fn copy_to_clipboard(path: &Path) -> Result<(), String> {
    let decoder = png::Decoder::new(std::io::BufReader::new(std::fs::File::open(path).map_err(|e| e.to_string())?));
    let mut decoder = decoder;
    decoder.set_transformations(png::Transformations::normalize_to_color8() | png::Transformations::ALPHA);
    let mut reader = decoder.read_info().map_err(|e| e.to_string())?;
    let mut pixels = vec![0; reader.output_buffer_size().ok_or("picture too large")?];
    let frame = reader.next_frame(&mut pixels).map_err(|e| e.to_string())?;
    pixels.truncate(frame.buffer_size());
    let rgba = match frame.color_type {
        png::ColorType::Rgba => pixels,
        png::ColorType::GrayscaleAlpha => pixels.as_chunks::<2>().0.iter().flat_map(|&[g, a]| [g, g, g, a]).collect(),
        other => return Err(format!("unexpected colour type {other:?}")),
    };
    let image = arboard::ImageData { width: frame.width as usize, height: frame.height as usize, bytes: rgba.into() };
    arboard::Clipboard::new().and_then(|mut c| c.set_image(image)).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A 1x1 PNG: signature, IHDR, IDAT, IEND.
    fn tiny_png() -> Vec<u8> {
        let mut out = Vec::new();
        let mut encoder = png::Encoder::new(&mut out, 1, 1);
        encoder.set_color(png::ColorType::Rgba);
        encoder.write_header().unwrap().write_image_data(&[1, 2, 3, 4]).unwrap();
        out
    }

    #[test]
    fn the_chunk_goes_before_the_image_data_and_reads_back() {
        let png = tiny_png();
        let out = with_description(&png, r#"{"application":"VRCX"}"#).unwrap();
        // Signature (8) + IHDR (8 + 13 + 4): the iTXt chunk comes right after.
        assert_eq!(&out[33 + 4..33 + 8], b"iTXt");
        let decoder = png::Decoder::new(std::io::Cursor::new(&out));
        let reader = decoder.read_info().unwrap();
        let text = &reader.info().utf8_text[0];
        assert_eq!(text.keyword, "Description");
        assert_eq!(text.get_text().unwrap(), r#"{"application":"VRCX"}"#);
    }

    #[test]
    fn deleting_metadata_takes_out_the_chunks_it_names() {
        let png = tiny_png();
        let tagged = with_description(&png, "{\"application\":\"VRCX\"}").unwrap();
        assert_eq!(without_text_chunk(&tagged, "Description").unwrap(), png);
        assert!(without_text_chunk(&tagged, "XML:com.adobe.xmp").is_none());
        assert!(without_text_chunk(&png, "Description").is_none());
        assert!(without_text_chunk(b"not a png", "Description").is_none());

        let dir = tempfile::tempdir().unwrap();
        std::fs::create_dir_all(dir.path().join("2025-01")).unwrap();
        let file = dir.path().join("2025-01").join("VRChat_a.png");
        std::fs::write(&file, &tagged).unwrap();
        assert_eq!(pngs_under(dir.path()), vec![file.clone()]);
        assert!(delete_metadata(&file).unwrap());
        assert_eq!(std::fs::read(&file).unwrap(), png);
        assert!(!delete_metadata(&file).unwrap());
    }

    #[test]
    fn crc_matches_the_png_reference() {
        // The IEND chunk's well-known CRC.
        assert_eq!(crc32(b"IEND", &[]), 0xAE42_6082);
    }

    #[test]
    fn not_a_png_is_left_alone() {
        assert!(with_description(b"GIF89a", "x").is_none());
    }

    #[test]
    fn only_vrchat_screenshots_are_touched() {
        let dir = tempfile::tempdir().unwrap();
        let ours = dir.path().join("VRChat_2025-01-01_00-00-00.000_1920x1080.png");
        let other = dir.path().join("holiday.png");
        std::fs::write(&ours, tiny_png()).unwrap();
        std::fs::write(&other, tiny_png()).unwrap();
        assert!(is_vrchat_screenshot(&ours));
        assert!(!is_vrchat_screenshot(&other));
        assert!(!is_vrchat_screenshot(&dir.path().join("VRChat_missing.png")));
    }

    #[test]
    fn the_world_id_is_appended_and_metadata_written() {
        let dir = tempfile::tempdir().unwrap();
        let shot = dir.path().join("VRChat_2025-01-01.png");
        std::fs::write(&shot, tiny_png()).unwrap();
        let action = ProcessScreenshot {
            path: shot.to_string_lossy().into(),
            metadata_json: "{}".into(),
            append_world_id: true,
            world_id: "wrld_x".into(),
            copy_to_clipboard: false,
        };
        apply(&shot, &action);
        let renamed = dir.path().join("VRChat_2025-01-01_wrld_x.png");
        assert!(renamed.is_file() && !shot.exists());
        assert!(std::fs::read(&renamed).unwrap().windows(4).any(|w| w == b"iTXt"));
    }
}
