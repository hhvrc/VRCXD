//! Saving prints, stickers and emoji into the UGC folder (the original's `ImageSaving.cs`
//! SavePrintToFile, SaveStickerToFile, SaveEmojiToFile, CropPrintImage and CropAllPrints). The
//! server downloads the image with the account's session and sends its bytes; this writes them.

use std::path::{Path, PathBuf};

use serde_json::json;

use super::{Answer, blocking, folders};
use crate::config::Config;
use crate::proto::{ActionStatus, Capability, CropPrints, MediaKind, SaveMedia};
use crate::screenshot::pngs_under;
use crate::settings::Settings;

pub const CAPABILITIES: [Capability; 1] = [Capability::SaveMedia];

const SIGNATURE: [u8; 8] = [0x89, b'P', b'N', b'G', b'\r', b'\n', 0x1a, b'\n'];
const INVALID: &str = "\"<>|:*?\\/";

/// MakeValidFileName: no path separators, no characters Windows refuses, no trailing dots.
pub fn valid_file_name(name: &str) -> String {
    let kept: String = name.chars().filter(|c| !(c.is_control() || INVALID.contains(*c))).collect();
    kept.trim_end_matches('.').to_string()
}

fn folder_of(kind: MediaKind) -> Option<&'static str> {
    match kind {
        MediaKind::Print => Some("Prints"),
        MediaKind::Sticker => Some("Stickers"),
        MediaKind::Emoji => Some("Emoji"),
        MediaKind::Unspecified => None,
    }
}

/// Where the file goes: `<ugc>/<Prints|Stickers|Emoji>/<month>/<name>`.
pub fn target(ugc: &Path, media: &SaveMedia) -> Option<PathBuf> {
    let kind = folder_of(media.kind())?;
    let name = valid_file_name(&media.file_name);
    if name.is_empty() {
        return None;
    }
    Some(ugc.join(kind).join(valid_file_name(&media.month_folder)).join(name))
}

/// Writes the file unless it is there already: its path, or None when it existed.
pub fn save(ugc: &Path, media: &SaveMedia) -> std::io::Result<Option<PathBuf>> {
    let path = target(ugc, media).ok_or_else(|| std::io::Error::other("not a print, sticker or emoji file name"))?;
    if path.exists() {
        return Ok(None);
    }
    std::fs::create_dir_all(path.parent().unwrap_or(ugc))?;
    let temp = path.with_extension("png.vrcxd-tmp");
    std::fs::write(&temp, &media.data)?;
    std::fs::rename(&temp, &path)?;
    if media.kind() == MediaKind::Print && media.crop {
        // CropPrintImage answering false: the print stays as saved.
        let _ = crop_file(&path);
    }
    Ok(Some(path))
}

/// The iTXt chunks before the image data, whole (length, type, data, CRC).
fn text_chunks(png: &[u8]) -> Vec<&[u8]> {
    let mut found = Vec::new();
    let mut pos = 8;
    while pos + 8 <= png.len() {
        let Ok(bytes) = png[pos..pos + 4].try_into() else { break };
        let end = pos + 12 + u32::from_be_bytes(bytes) as usize;
        let kind = &png[pos + 4..pos + 8];
        if end > png.len() || kind == b"IDAT" || kind == b"IEND" {
            break;
        }
        if kind == b"iTXt" {
            found.push(&png[pos..end]);
        }
        pos = end;
    }
    found
}

/// The chunks put in before the first IDAT.
fn with_chunks(png: &[u8], chunks: &[&[u8]]) -> Vec<u8> {
    let mut pos = 8;
    let mut at = png.len();
    while pos + 8 <= png.len() {
        let Ok(bytes) = png[pos..pos + 4].try_into() else { break };
        if &png[pos + 4..pos + 8] == b"IDAT" {
            at = pos;
            break;
        }
        pos += 12 + u32::from_be_bytes(bytes) as usize;
    }
    let at = at.min(png.len());
    let mut out = png[..at].to_vec();
    for chunk in chunks {
        out.extend_from_slice(chunk);
    }
    out.extend_from_slice(&png[at..]);
    out
}

/// CropPrint: a 2048x1440 print cut to its 1920x1080 picture at (64, 69), keeping its iTXt
/// chunks. None when it is not a print-sized PNG.
pub fn crop(png: &[u8]) -> Option<Vec<u8>> {
    if png.len() < 8 || png[..8] != SIGNATURE {
        return None;
    }
    let mut decoder = png::Decoder::new(std::io::Cursor::new(png));
    decoder.set_transformations(png::Transformations::EXPAND);
    let mut reader = decoder.read_info().ok()?;
    let mut buf = vec![0; reader.output_buffer_size()?];
    let frame = reader.next_frame(&mut buf).ok()?;
    if frame.width != 2048 || frame.height != 1440 {
        return None;
    }
    let pixel = frame.line_size / frame.width as usize;
    let (x, y, w, h) = (64usize, 69usize, 1920usize, 1080usize);
    let mut cropped = Vec::with_capacity(w * h * pixel);
    for row in y..y + h {
        let start = row * frame.line_size + x * pixel;
        cropped.extend_from_slice(&buf[start..start + w * pixel]);
    }
    let mut out = Vec::new();
    {
        let mut encoder = png::Encoder::new(&mut out, w as u32, h as u32);
        encoder.set_color(frame.color_type);
        encoder.set_depth(frame.bit_depth);
        let mut writer = encoder.write_header().ok()?;
        writer.write_image_data(&cropped).ok()?;
    }
    Some(with_chunks(&out, &text_chunks(png)))
}

/// CropPrintImage: whether the file was a print and is now cropped.
pub fn crop_file(path: &Path) -> std::io::Result<bool> {
    let Some(cropped) = crop(&std::fs::read(path)?) else { return Ok(false) };
    let temp = path.with_extension("png.vrcxd-tmp");
    std::fs::write(&temp, cropped)?;
    std::fs::rename(&temp, path)?;
    Ok(true)
}

fn ugc(config: &Config) -> Option<PathBuf> {
    folders::ugc_photos(config, &Settings::load())
}

pub async fn save_media(media: SaveMedia, config: &Config) -> Answer {
    let config = config.clone();
    blocking(move || match ugc(&config) {
        Some(dir) => match save(&dir, &media) {
            Ok(path) => Answer::json(json!({ "path": path.map(|p| p.display().to_string()).unwrap_or_default() })),
            Err(e) => Answer::failed(e.to_string()),
        },
        None => Answer::status(ActionStatus::NotFound, "The UGC folder is not known on this PC"),
    })
    .await
}

/// CropAllPrints: every PNG under the Prints folder; how many were cropped.
pub async fn crop_prints(confirm: CropPrints, config: &Config) -> Answer {
    if !confirm.confirm {
        return Answer::not_confirmed();
    }
    let config = config.clone();
    blocking(move || {
        let Some(dir) = ugc(&config) else { return Answer::json(json!({ "cropped": 0 })) };
        let cropped = pngs_under(&dir.join("Prints")).iter().filter(|p| crop_file(p).unwrap_or(false)).count();
        Answer::json(json!({ "cropped": cropped }))
    })
    .await
}

#[cfg(test)]
mod tests {
    use super::*;

    fn png_of(width: u32, height: u32) -> Vec<u8> {
        let mut out = Vec::new();
        {
            let mut encoder = png::Encoder::new(&mut out, width, height);
            encoder.set_color(png::ColorType::Rgb);
            encoder.set_depth(png::BitDepth::Eight);
            let mut writer = encoder.write_header().unwrap();
            let mut data = vec![255u8; (width * height * 3) as usize];
            // A red pixel where the picture starts.
            if width > 64 && height > 69 {
                let start = (69 * width as usize + 64) * 3;
                data[start..start + 3].copy_from_slice(&[255, 0, 0]);
            }
            writer.write_image_data(&data).unwrap();
        }
        out
    }

    #[test]
    fn file_names_are_made_valid() {
        assert_eq!(valid_file_name("a/b\\c:d*e?.png"), "abcde.png");
        assert_eq!(valid_file_name("name..."), "name");
        assert_eq!(valid_file_name("2024-11"), "2024-11");
    }

    #[test]
    fn saving_writes_once_into_the_kinds_folder() {
        let dir = tempfile::tempdir().unwrap();
        let media = SaveMedia {
            kind: MediaKind::Sticker as i32,
            month_folder: "2024-11".into(),
            file_name: "Me_2024-11-03_inv_x.png".into(),
            data: vec![1, 2, 3],
            ..Default::default()
        };
        let path = save(dir.path(), &media).unwrap().unwrap();
        assert_eq!(path, dir.path().join("Stickers").join("2024-11").join("Me_2024-11-03_inv_x.png"));
        assert_eq!(std::fs::read(&path).unwrap(), vec![1, 2, 3]);
        assert!(save(dir.path(), &media).unwrap().is_none());
    }

    #[test]
    fn a_print_is_cut_to_its_picture_with_its_text_kept() {
        let print = crate::screenshot::with_description(&png_of(2048, 1440), "note").unwrap();
        let cropped = crop(&print).unwrap();
        let mut reader = png::Decoder::new(std::io::Cursor::new(&cropped)).read_info().unwrap();
        let mut buf = vec![0; reader.output_buffer_size().unwrap()];
        let frame = reader.next_frame(&mut buf).unwrap();
        assert_eq!((frame.width, frame.height), (1920, 1080));
        assert_eq!(&buf[..3], &[255, 0, 0]);
        assert_eq!(text_chunks(&cropped).len(), 1);
        // Anything not print-sized is left alone.
        assert!(crop(&png_of(100, 50)).is_none());
    }
}
