//! The agent's connection to the server (`/1/ws`, `vrcx_live.proto`), as the Qt app's `log_service`
//! ran it: hello, the game state, then wait for `GameLogResume` (how far this PC's log is already
//! stored), then ship parsed log lines as `GameLogBatch` frames one at a time, each waiting for its
//! `GameLogAck`. `ClientAction`s are carried out on the PC. A dropped connection keeps the batch it
//! had not had acknowledged and sends it again; the server dedupes.

use std::collections::VecDeque;
use std::time::Duration;

use futures_util::{SinkExt, StreamExt};
use prost::Message as _;
use tokio::sync::{broadcast, mpsc, watch};
use tokio::time::{Instant, sleep_until};
use tokio_tungstenite::tungstenite::Message;
use tokio_tungstenite::tungstenite::client::IntoClientRequest;
use tracing::{debug, info, warn};

use crate::actions;
use crate::config::{self, Config};
use crate::game::GameStatus;
use crate::log_watcher::LogWatcher;
use crate::proto::log_entry::Entry;
use crate::proto::{
    ActionResult, ClientHello, ClientMessage, GameLogBatch, GameState, IpcPacket, LogEntry, Ping, ProtocolVersion,
    ServerMessage, client_message, ipc_packet, server_message,
};
use crate::status::Status;

/// The batch size the original's `LogWatcher.Get` handed over.
const BATCH_SIZE: usize = 1000;
const RECONNECT_DELAY: Duration = Duration::from_secs(10);
/// A ping every 30 s; nothing at all from the server for 90 s means the connection is dead (the
/// server lost power, the router forgot it, the PC changed network) and it is opened again.
const PING_INTERVAL: Duration = Duration::from_secs(30);
const SILENCE_TIMEOUT: Duration = Duration::from_secs(90);
/// How often the logs are read once caught up: often while VRChat runs, rarely otherwise (it may
/// still be writing its last lines).
const POLL_WHILE_PLAYING: Duration = Duration::from_secs(1);
const POLL_IDLE: Duration = Duration::from_secs(10);
const BYTES_PER_POLL: u64 = 1 << 20;
/// Unsent batches before reading pauses: the log files are the buffer while the server is away.
const MAX_QUEUED_BATCHES: usize = 2;

pub type Socket = tokio_tungstenite::WebSocketStream<tokio_tungstenite::MaybeTlsStream<tokio::net::TcpStream>>;

/// The server's http(s) base as its websocket address.
pub fn socket_url(server_address: &str) -> Result<url::Url, String> {
    let mut url = url::Url::parse(server_address.trim()).map_err(|e| format!("{server_address}: {e}"))?;
    let scheme = match url.scheme() {
        "https" | "wss" => "wss",
        "http" | "ws" => "ws",
        other => return Err(format!("{server_address}: {other} is not http or https")),
    };
    url.set_scheme(scheme).map_err(|()| format!("{server_address}: cannot use {scheme}"))?;
    let path = format!("{}/1/ws", url.path().trim_end_matches('/'));
    url.set_path(&path);
    Ok(url)
}

async fn connect(config: &Config) -> Result<Socket, String> {
    let url = socket_url(&config.server_address)?;
    let mut request = url.as_str().into_client_request().map_err(|e| e.to_string())?;
    request
        .headers_mut()
        .insert("VrcxToken", config.bearer_token.parse().map_err(|_| "the token is not a valid header")?);
    let (socket, _) = tokio::time::timeout(Duration::from_secs(15), tokio_tungstenite::connect_async(request))
        .await
        .map_err(|_| "timed out".to_owned())?
        .map_err(|e| e.to_string())?;
    Ok(socket)
}

/// Inputs the agent's tasks feed the connection.
pub struct Channels {
    pub relay: mpsc::Receiver<ipc_packet::Packet>,
    pub ipc_out: broadcast::Sender<Vec<u8>>,
    pub game: watch::Receiver<GameStatus>,
}

/// What a connection keeps across reconnects.
struct Shipper {
    config: Config,
    watcher: Option<LogWatcher>,
    pending: VecDeque<Vec<LogEntry>>,
    status: Status,
    /// Whether the newest quit or join line read says VRChat quit on its own (the original's
    /// VrcClosedGracefully: set by the quit line, cleared by a join).
    closed_gracefully: bool,
}

fn game_state(game: GameStatus, closed_gracefully: bool) -> client_message::Message {
    client_message::Message::GameState(GameState {
        running: game.running,
        steamvr_running: game.steamvr_running,
        closed_gracefully: (!game.running && game.ran).then_some(closed_gracefully),
        hmd_afk: game.hmd_afk,
    })
}

/// Runs for as long as the agent does.
pub async fn run(mut channels: Channels) {
    let mut shipper = Shipper::new(Config::load());
    loop {
        let config = Config::load();
        if config != shipper.config {
            info!("settings changed, starting over");
            shipper = Shipper::new(config);
        }
        if !shipper.config.is_configured() {
            shipper.status.set_state("not configured: run `vrcxd-agent setup --server <address> --token <token>`");
            tokio::time::sleep(PING_INTERVAL).await;
            continue;
        }
        if shipper.config.log_dir().is_none() {
            shipper
                .status
                .set_state("VRChat's log folder was not found; set it with `vrcxd-agent setup --log-dir <folder>`");
            tokio::time::sleep(PING_INTERVAL).await;
            continue;
        }
        shipper.status.set_state("connecting");
        match connect(&shipper.config).await {
            Ok(socket) => {
                info!("connected to {}", shipper.config.server_address);
                shipper.status.set_state("connected");
                let reason = shipper.session(socket, &mut channels).await;
                warn!("connection closed: {reason}; reconnecting in {}s", RECONNECT_DELAY.as_secs());
                shipper.status.set_state(&format!("disconnected: {reason}"));
            }
            Err(e) => {
                warn!("could not connect to {}: {e}", shipper.config.server_address);
                shipper.status.set_state(&format!("could not connect: {e}"));
            }
        }
        tokio::time::sleep(RECONNECT_DELAY).await;
    }
}

fn encode(message: client_message::Message) -> Message {
    Message::Binary(ClientMessage { message: Some(message) }.encode_to_vec().into())
}

fn timestamp_now() -> prost_types::Timestamp {
    let now = chrono::Utc::now();
    prost_types::Timestamp { seconds: now.timestamp(), nanos: now.timestamp_subsec_nanos() as i32 }
}

impl Shipper {
    fn new(config: Config) -> Self {
        Self { config, watcher: None, pending: VecDeque::new(), status: Status::default(), closed_gracefully: false }
    }

    /// One connection, until it drops; why it ended.
    async fn session(&mut self, socket: Socket, channels: &mut Channels) -> String {
        let (mut tx, mut rx) = socket.split();
        let (results_tx, mut results_rx) = mpsc::channel::<ActionResult>(32);
        let config_stamp = config_modified();

        let ctx =
            actions::Context { config: self.config.clone(), results: results_tx, ipc_out: channels.ipc_out.clone() };
        let hello = ClientHello {
            protocol_version: ProtocolVersion::ProtocolVersion1 as u32,
            client_id: self.config.client_id.clone(),
            client_name: format!("vrcxd-agent/{}", env!("CARGO_PKG_VERSION")),
            // The agent only ships logs and runs actions; the server's live events are for pages.
            mute_events: true,
            capabilities: actions::capabilities().into_iter().map(|c| c as i32).collect(),
            os: std::env::consts::OS.to_owned(),
            folders: Some(actions::folders::report(&self.config)),
            utc_offset_minutes: chrono::Local::now().offset().local_minus_utc() / 60,
        };
        let game = *channels.game.borrow_and_update();
        for message in [client_message::Message::Hello(hello), game_state(game, self.closed_gracefully)] {
            if let Err(e) = tx.send(encode(message)).await {
                return e.to_string();
            }
        }

        let mut awaiting_ack = false;
        let mut resumed = false;
        let mut silence = Instant::now() + SILENCE_TIMEOUT;
        let mut ping = tokio::time::interval_at(Instant::now() + PING_INTERVAL, PING_INTERVAL);
        let mut next_poll = Instant::now();

        loop {
            let can_poll = resumed && self.watcher.is_some() && self.pending.len() < MAX_QUEUED_BATCHES;
            tokio::select! {
                frame = rx.next() => {
                    let bytes = match frame {
                        Some(Ok(Message::Binary(bytes))) => bytes,
                        Some(Ok(Message::Close(close))) => return close.map_or_else(|| "closed".into(), |c| format!("closed: {}", c.reason)),
                        Some(Ok(_)) => { silence = Instant::now() + SILENCE_TIMEOUT; continue }
                        Some(Err(e)) => return e.to_string(),
                        None => return "closed".into(),
                    };
                    // Any frame shows the connection is alive.
                    silence = Instant::now() + SILENCE_TIMEOUT;
                    let Ok(message) = ServerMessage::decode(bytes) else {
                        warn!("undecodable frame from the server, ignoring");
                        continue;
                    };
                    match message.message {
                        Some(server_message::Message::Hello(hello)) => {
                            info!("server {} speaks protocol {}", hello.server_version, hello.protocol_version);
                        }
                        Some(server_message::Message::GameLogResume(resume)) => {
                            self.resume(resume.last_entry_at);
                            resumed = true;
                            next_poll = Instant::now();
                        }
                        Some(server_message::Message::GameLogAck(ack)) => {
                            debug!("batch acknowledged, {} accepted", ack.accepted);
                            if let Some(batch) = self.pending.pop_front() {
                                self.status.sent(batch.len());
                            }
                            awaiting_ack = false;
                        }
                        Some(server_message::Message::Action(action)) => {
                            actions::perform(action, &ctx);
                        }
                        Some(server_message::Message::Pong(_) | server_message::Message::Event(_)) | None => {}
                    }
                }
                _ = ping.tick() => {
                    if config_modified() != config_stamp && Config::load() != self.config {
                        return "the settings changed".into();
                    }
                    if let Err(e) = tx.send(encode(client_message::Message::Ping(Ping {}))).await {
                        return e.to_string();
                    }
                }
                () = sleep_until(silence) => {
                    return format!("no answer from the server for {}s", SILENCE_TIMEOUT.as_secs());
                }
                () = sleep_until(next_poll), if can_poll => {
                    let caught_up = self.poll();
                    let playing = channels.game.borrow().running;
                    next_poll = Instant::now() + if !caught_up { Duration::ZERO } else if playing { POLL_WHILE_PLAYING } else { POLL_IDLE };
                }
                changed = channels.game.changed() => {
                    if changed.is_err() { return "the game monitor stopped".into() }
                    let game = *channels.game.borrow_and_update();
                    info!("VRChat running: {}, SteamVR running: {}", game.running, game.steamvr_running);
                    // Read the session's last lines first, so whether it quit on its own is known.
                    if !game.running {
                        self.poll();
                    }
                    next_poll = Instant::now();
                    if let Err(e) = tx.send(encode(game_state(game, self.closed_gracefully))).await {
                        return e.to_string();
                    }
                }
                Some(packet) = channels.relay.recv() => {
                    let ipc = IpcPacket { received_at: Some(timestamp_now()), packet: Some(packet) };
                    if let Err(e) = tx.send(encode(client_message::Message::Ipc(ipc))).await {
                        return e.to_string();
                    }
                }
                Some(result) = results_rx.recv() => {
                    if let Err(e) = tx.send(encode(client_message::Message::ActionResult(result))).await {
                        return e.to_string();
                    }
                }
            }

            if resumed
                && !awaiting_ack
                && let Some(batch) = self.pending.front()
            {
                let message = encode(client_message::Message::GameLog(GameLogBatch { entries: batch.clone() }));
                if let Err(e) = tx.send(message).await {
                    return e.to_string();
                }
                awaiting_ack = true;
            }
        }
    }

    fn resume(&mut self, last_entry_at: Option<prost_types::Timestamp>) {
        if self.watcher.is_some() {
            return;
        }
        // Lines at or before this are stored already; unset means send everything.
        let till = last_entry_at
            .and_then(|t| chrono::DateTime::from_timestamp(t.seconds, t.nanos.max(0) as u32))
            .unwrap_or(chrono::DateTime::UNIX_EPOCH);
        let Some(dir) = self.config.log_dir() else { return };
        info!("watching {} for lines after {till}", dir.display());
        self.watcher = Some(LogWatcher::new(dir, till));
    }

    /// Reads new lines into batches; whether everything was read.
    fn poll(&mut self) -> bool {
        let Some(watcher) = self.watcher.as_mut() else { return true };
        let mut entries = watcher.poll(BYTES_PER_POLL);
        for entry in &entries {
            match entry.entry {
                Some(Entry::VrcQuit(_)) => self.closed_gracefully = true,
                Some(Entry::Location(_)) => self.closed_gracefully = false,
                Some(Entry::DesktopMode(_)) => crate::vr::set_game_no_vr(true),
                Some(Entry::OpenvrInit(_)) => crate::vr::set_game_no_vr(false),
                _ => {}
            }
        }
        while !entries.is_empty() {
            let rest = entries.split_off(entries.len().min(BATCH_SIZE));
            self.pending.push_back(std::mem::replace(&mut entries, rest));
        }
        watcher.caught_up()
    }
}

fn config_modified() -> Option<std::time::SystemTime> {
    std::fs::metadata(config::path()).and_then(|m| m.modified()).ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn whether_vrchat_quit_on_its_own_is_said_only_once_it_stopped() {
        let state = |running, ran, graceful| match game_state(
            GameStatus { running, steamvr_running: true, ran, hmd_afk: false },
            graceful,
        ) {
            client_message::Message::GameState(s) => s,
            _ => unreachable!(),
        };
        assert_eq!(state(true, true, false).closed_gracefully, None);
        assert_eq!(state(false, false, true).closed_gracefully, None);
        assert_eq!(state(false, true, true).closed_gracefully, Some(true));
        assert_eq!(state(false, true, false).closed_gracefully, Some(false));
        assert!(state(false, true, false).steamvr_running);
    }

    #[test]
    fn the_socket_is_under_the_server_address() {
        assert_eq!(socket_url("http://nas:5280").unwrap().as_str(), "ws://nas:5280/1/ws");
        assert_eq!(socket_url("https://vrcx.example/base/").unwrap().as_str(), "wss://vrcx.example/base/1/ws");
        assert!(socket_url("ftp://nas").is_err());
        assert!(socket_url("not a url").is_err());
    }
}
