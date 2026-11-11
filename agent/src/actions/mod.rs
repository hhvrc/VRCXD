//! The `ClientAction`s the server relays from the web client, and which of them this agent does
//! (`capabilities`, sent in the hello). Each family has its own module; an action this agent does
//! not do answers `UNSUPPORTED`.

use tokio::sync::{broadcast, mpsc};
use tracing::warn;

use crate::config::Config;
use crate::proto::client_action::Action;
use crate::proto::{ActionResult, ActionStatus, Capability, ClientAction};
use crate::settings::Settings;
use crate::{game, ipc, screenshot};

pub mod cache;
pub mod calendar;
pub mod folders;
pub mod media;
pub mod notify;
pub mod registry;
pub mod vrchat_files;

/// What an action answers.
#[derive(Debug, PartialEq)]
pub struct Answer {
    pub status: ActionStatus,
    pub error: String,
    pub json: String,
}

impl Answer {
    pub fn ok() -> Self {
        Self { status: ActionStatus::Ok, error: String::new(), json: String::new() }
    }
    pub fn json(value: serde_json::Value) -> Self {
        Self { json: value.to_string(), ..Self::ok() }
    }
    pub fn failed(error: impl Into<String>) -> Self {
        Self { status: ActionStatus::Failed, error: error.into(), json: String::new() }
    }
    pub fn status(status: ActionStatus, error: impl Into<String>) -> Self {
        Self { status, error: error.into(), json: String::new() }
    }
    pub fn unsupported() -> Self {
        Self::status(ActionStatus::Unsupported, "This PC's agent does not do that")
    }
    pub fn not_confirmed() -> Self {
        Self::status(ActionStatus::NotConfirmed, "Not confirmed")
    }
}

impl<E: std::fmt::Display> From<Result<Answer, E>> for Answer {
    fn from(result: Result<Answer, E>) -> Self {
        result.unwrap_or_else(|e| Answer::failed(e.to_string()))
    }
}

/// The action families this agent does on this OS.
pub fn capabilities() -> Vec<Capability> {
    let mut caps = vec![Capability::Launch, Capability::Screenshots, Capability::Settings];
    if cfg!(windows) {
        caps.extend([Capability::OpenInstance, Capability::Ipc]);
    }
    caps.extend(folders::CAPABILITIES);
    caps.extend(vrchat_files::CAPABILITIES);
    caps.extend(cache::CAPABILITIES);
    caps.extend(media::CAPABILITIES);
    caps.extend(calendar::CAPABILITIES);
    caps.extend(notify::CAPABILITIES);
    caps.extend(crate::discord::CAPABILITIES);
    caps.push(Capability::Openvr);
    if cfg!(windows) {
        caps.extend(registry::CAPABILITIES);
    }
    caps
}

/// Everything an action may need.
#[derive(Clone)]
pub struct Context {
    pub config: Config,
    pub results: mpsc::Sender<ActionResult>,
    pub ipc_out: broadcast::Sender<Vec<u8>>,
}

/// Carries out one action in the background; one with a request id answers through `results`.
pub fn perform(action: ClientAction, ctx: &Context) {
    let request_id = action.request_id;
    let Some(kind) = action.action else { return };
    let ctx = ctx.clone();
    tokio::spawn(async move {
        let answer = run(kind, &ctx).await;
        if answer.status != ActionStatus::Ok {
            warn!("action failed: {:?} {}", answer.status, answer.error);
        }
        if !request_id.is_empty() {
            let result =
                ActionResult { request_id, status: answer.status as i32, error: answer.error, json: answer.json };
            let _ = ctx.results.send(result).await;
        }
    });
}

/// Runs blocking work (files, the registry, child processes) off the agent's one thread.
pub async fn blocking<F: FnOnce() -> Answer + Send + 'static>(f: F) -> Answer {
    tokio::task::spawn_blocking(f).await.unwrap_or_else(|e| Answer::failed(e.to_string()))
}

async fn run(action: Action, ctx: &Context) -> Answer {
    match action {
        Action::ProcessScreenshot(shot) => {
            screenshot::process(shot).await;
            Answer::ok()
        }
        Action::QuitVrchat(_) => {
            blocking(|| {
                game::quit();
                Answer::ok()
            })
            .await
        }
        Action::LaunchVrchat(launch) => game::launch(&launch, &ctx.config).await,
        Action::OpenInstance(open) => game::open_instance(&open.launch_url).await,
        Action::SendIpc(packet) => {
            // No program on the pipe is fine; nobody answers these.
            let _ = ctx.ipc_out.send(ipc::frame(&packet));
            Answer::ok()
        }
        Action::OpenFolder(open) => folders::open(open, &ctx.config).await,
        Action::PickPath(pick) => folders::pick(pick, &ctx.config).await,
        Action::GetCacheInfo(_) => cache::info(&ctx.config).await,
        Action::CheckCache(entry) => cache::check_entry(entry, &ctx.config).await,
        Action::DeleteCacheEntry(entry) => cache::delete_entry(entry, &ctx.config).await,
        Action::ClearCache(confirm) => cache::clear(confirm, &ctx.config).await,
        Action::SweepCache(_) => cache::sweep_cache(&ctx.config).await,
        Action::SaveMedia(media) => media::save_media(media, &ctx.config).await,
        Action::CropPrints(confirm) => media::crop_prints(confirm, &ctx.config).await,
        Action::OpenCalendarFile(open) => calendar::open(open).await,
        Action::ShowNotification(n) => notify::show(n).await,
        Action::UpdateNowPlayingPie(pie) => {
            crate::vr::hmd::update_pie(pie);
            Answer::ok()
        }
        Action::UpdateHud(feed) => {
            crate::vr::hmd::update_hud(feed);
            Answer::ok()
        }
        Action::UpdateWristFeed(feed) => {
            crate::vr::wrist::set_feed(feed);
            Answer::ok()
        }
        Action::SetDiscordPresence(presence) => {
            crate::discord::set(presence);
            Answer::ok()
        }
        Action::ReadVrchatConfig(_) => vrchat_files::read_config(&ctx.config).await,
        Action::WriteVrchatConfig(write) => vrchat_files::write_config(write, &ctx.config).await,
        Action::ReadLocalModerations(read) => vrchat_files::read_moderations(read, &ctx.config).await,
        Action::SetLocalModeration(set) => vrchat_files::write_moderation(set, &ctx.config).await,
        Action::ReadVrchatRegistry(_) => registry::read().await,
        Action::WriteVrchatRegistry(write) => registry::write(write).await,
        Action::DeleteVrchatRegistry(confirm) => registry::delete(confirm).await,
        Action::ReadVrchatRegistryKey(read) => registry::read_key(read).await,
        Action::SetVrchatRegistryKey(set) => registry::set_key(set).await,
        Action::DeleteScreenshotMetadata(delete) => folders::delete_screenshot_metadata(delete, &ctx.config).await,
        Action::ApplyAgentSettings(settings) => {
            blocking(move || Settings::from(settings).save().map(|()| Answer::ok()).into()).await
        }
    }
}
