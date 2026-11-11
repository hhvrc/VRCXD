//! The VRCXD agent: a small headless program on the gaming PC. It ships VRChat's logs to the VRCXD
//! server, carries out what the web client asks of the PC (start VRChat, open an instance in it,
//! write screenshot metadata), and runs the original VRCX's named pipe for other programs. It has
//! no window: the web client is its interface.

// No console window when started at login; commands attach to the console they were typed in.
#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

mod actions;
mod config;
mod discord;
mod game;
mod ipc;
mod launcher;
mod live;
mod log_parser;
mod log_watcher;
mod platform;
mod screenshot;
mod settings;
mod status;
mod vr;

mod proto {
    #![allow(clippy::all, dead_code)]
    include!(concat!(env!("OUT_DIR"), "/vrcx.live.rs"));
}

use std::process::ExitCode;

use clap::{Parser, Subcommand};
use tokio::sync::{broadcast, mpsc, watch};
use tracing::{error, info, warn};

use crate::config::Config;

#[derive(Parser)]
#[command(
    name = "vrcxd-agent",
    version,
    about = "Ships VRChat's logs to the VRCXD server and runs what the web client asks of this PC"
)]
struct Cli {
    #[command(subcommand)]
    command: Option<Command>,
}

#[derive(Subcommand)]
enum Command {
    /// Run the agent (what starting it at login does).
    Run {
        /// Also log to this console.
        #[arg(long)]
        console: bool,
    },
    /// Change the settings; only the options given change. A running agent picks them up within 30 s.
    Setup {
        /// The server's address, e.g. http://nas:5280.
        #[arg(long)]
        server: Option<String>,
        /// The server's API token.
        #[arg(long)]
        token: Option<String>,
        /// Keeps this PC's game log apart from other PCs' (default: the machine name).
        #[arg(long)]
        client_id: Option<String>,
        /// VRChat's log folder, when it is not in the usual place.
        #[arg(long)]
        log_dir: Option<String>,
        /// Extra arguments VRChat is started with, e.g. "--fps=144".
        #[arg(long, allow_hyphen_values = true)]
        launch_arguments: Option<String>,
        /// The VRChat folder or its launch.exe, when it is not found on its own.
        #[arg(long)]
        vrchat_path: Option<String>,
        /// The web client's address, when it is not the server address.
        #[arg(long)]
        web_address: Option<String>,
    },
    /// Show the settings and what the running agent is doing.
    Status,
    /// Start the agent at login and let it open vrcx:// links.
    Install,
    /// Undo `install`.
    Uninstall,
    /// Open a vrcx:// link in the web client (what clicking one runs).
    OpenUrl { url: String },
}

fn init_logging(console: bool) -> Option<tracing_appender::non_blocking::WorkerGuard> {
    use tracing_subscriber::prelude::*;
    let filter = || tracing_subscriber::EnvFilter::try_from_env("VRCXD_LOG").unwrap_or_else(|_| "info".into());
    let dir = platform::data_dir().join("logs");
    let _ = std::fs::create_dir_all(&dir);
    let appender = tracing_appender::rolling::Builder::new()
        .rotation(tracing_appender::rolling::Rotation::DAILY)
        .filename_prefix("agent")
        .filename_suffix("log")
        .max_log_files(7)
        .build(&dir);
    let file =
        appender.ok().map(|a| tracing_subscriber::fmt::layer().with_ansi(false).with_writer(a).with_filter(filter()));
    let stderr = console.then(|| tracing_subscriber::fmt::layer().with_writer(std::io::stderr).with_filter(filter()));
    tracing_subscriber::registry().with(file).with(stderr).init();
    None
}

fn main() -> ExitCode {
    let has_console = platform::attach_console();
    let cli = Cli::parse();
    match cli.command.unwrap_or(Command::Run { console: false }) {
        Command::Run { console } => run(console && has_console),
        Command::Setup { server, token, client_id, log_dir, launch_arguments, vrchat_path, web_address } => {
            let mut config = Config::load();
            let fields = [
                (server, &mut config.server_address),
                (token, &mut config.bearer_token),
                (client_id, &mut config.client_id),
                (log_dir, &mut config.vrchat_log_dir),
                (launch_arguments, &mut config.launch_arguments),
                (vrchat_path, &mut config.vrc_launch_path_override),
                (web_address, &mut config.web_address),
            ];
            for (value, field) in fields {
                if let Some(value) = value {
                    *field = value;
                }
            }
            if !config.server_address.is_empty()
                && let Err(e) = live::socket_url(&config.server_address)
            {
                eprintln!("Not a server address: {e}");
                return ExitCode::FAILURE;
            }
            match config.save() {
                Ok(path) => {
                    println!("Saved {}", path.display());
                    ExitCode::SUCCESS
                }
                Err(e) => {
                    eprintln!("Could not save the settings: {e}");
                    ExitCode::FAILURE
                }
            }
        }
        Command::Status => {
            let config = Config::load();
            println!("Settings        {}", config::path().display());
            println!(
                "Server          {}",
                if config.server_address.is_empty() { "(not set)" } else { &config.server_address }
            );
            println!("Token           {}", if config.bearer_token.is_empty() { "(not set)" } else { "(set)" });
            println!("PC name         {}", config.client_id);
            println!("Log folder      {}", config.log_dir().map_or("(not found)".into(), |d| d.display().to_string()));
            println!("Starts at login {}", if platform::installed() { "yes" } else { "no" });
            match status::Status::read() {
                Some(s) if instance_lock().is_err() => {
                    println!("Agent           running (pid {}), {} since {}", s.pid, s.state, s.since);
                    println!(
                        "Lines sent      {}{}",
                        s.entries_sent,
                        if s.last_sent_at.is_empty() { String::new() } else { format!(", last at {}", s.last_sent_at) }
                    );
                }
                _ => println!("Agent           not running"),
            }
            println!("Agent logs      {}", platform::data_dir().join("logs").display());
            ExitCode::SUCCESS
        }
        Command::Install => report(platform::install()),
        Command::Uninstall => report(platform::uninstall()),
        Command::OpenUrl { url } => {
            let _guard = init_logging(false);
            let Some(command) =
                url.strip_prefix("/uri=vrcx://").or_else(|| url.strip_prefix("vrcx://")).filter(|c| !c.is_empty())
            else {
                error!("not a vrcx:// link: {url}");
                return ExitCode::FAILURE;
            };
            if open_launch_page(&Config::load(), command) { ExitCode::SUCCESS } else { ExitCode::FAILURE }
        }
    }
}

fn report(result: std::io::Result<Vec<String>>) -> ExitCode {
    match result {
        Ok(lines) => {
            lines.iter().for_each(|l| println!("{l}"));
            ExitCode::SUCCESS
        }
        Err(e) => {
            eprintln!("Failed: {e}");
            ExitCode::FAILURE
        }
    }
}

/// The original's `eventLaunchCommand`, done by the web client's `/launch?command=` page.
fn open_launch_page(config: &Config, command: &str) -> bool {
    let base = config.web_base();
    if base.is_empty() {
        error!("no server address set, so there is no web client to open {command} in");
        return false;
    }
    let mut url = match url::Url::parse(&format!("{base}/launch")) {
        Ok(url) => url,
        Err(e) => {
            error!("bad web address {base}: {e}");
            return false;
        }
    };
    url.query_pairs_mut().append_pair("command", command.trim_end_matches('/'));
    info!("opening {url}");
    platform::open_url(url.as_str()).inspect_err(|e| error!("{e}")).is_ok()
}

/// Held while the agent runs, so a second copy does not ship the same logs again.
fn instance_lock() -> std::io::Result<std::fs::File> {
    std::fs::create_dir_all(platform::data_dir())?;
    let file = std::fs::OpenOptions::new()
        .create(true)
        .truncate(false)
        .write(true)
        .open(platform::data_dir().join("agent.lock"))?;
    file.try_lock().map_err(|e| std::io::Error::other(e.to_string()))?;
    Ok(file)
}

fn run(console: bool) -> ExitCode {
    let _guard = init_logging(console);
    let Ok(_lock) = instance_lock() else {
        eprintln!("The agent is already running.");
        return ExitCode::FAILURE;
    };
    let _ = rustls::crypto::ring::default_provider().install_default();
    info!("vrcxd-agent {} starting, settings in {}", env!("CARGO_PKG_VERSION"), config::path().display());

    // One thread: the agent is idle nearly all the time.
    let runtime = match tokio::runtime::Builder::new_current_thread().enable_all().build() {
        Ok(rt) => rt,
        Err(e) => {
            error!("could not start: {e}");
            return ExitCode::FAILURE;
        }
    };
    runtime.block_on(async {
        let (game_tx, game_rx) = watch::channel(game::GameStatus::default());
        let game_tx = std::sync::Arc::new(game_tx);
        tokio::spawn(game::watch(game_tx.clone()));
        std::thread::Builder::new().name("openvr".into()).spawn(move || vr::run(game_tx)).ok();
        tokio::spawn(launcher::run(game_rx.clone()));
        tokio::spawn(discord::run());

        let (ipc_in, mut ipc_rx) = mpsc::channel::<ipc::Incoming>(256);
        let (ipc_out, _) = broadcast::channel::<Vec<u8>>(64);
        #[cfg(windows)]
        tokio::spawn(ipc::serve(platform::ipc_pipe_name(), ipc_in, ipc_out.clone()));
        #[cfg(not(windows))]
        drop(ipc_in);

        // Launch commands open the web client whether or not the server is reachable; the rest
        // waits for the connection.
        let (relay_tx, relay_rx) = mpsc::channel(256);
        tokio::spawn(async move {
            while let Some(message) = ipc_rx.recv().await {
                match message {
                    ipc::Incoming::LaunchCommand(command) => {
                        let config = Config::load();
                        tokio::task::spawn_blocking(move || open_launch_page(&config, &command));
                    }
                    ipc::Incoming::Relay(packet) => {
                        if relay_tx.try_send(packet).is_err() {
                            warn!("dropped a pipe packet: the server has been away too long");
                        }
                    }
                }
            }
        });

        tokio::select! {
            () = live::run(live::Channels { relay: relay_rx, ipc_out, game: game_rx }) => {}
            _ = tokio::signal::ctrl_c() => info!("stopping"),
        }
    });
    ExitCode::SUCCESS
}
