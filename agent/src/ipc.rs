//! The original's VRCX named pipe (`Dotnet/IPC`), which other programs on the PC use: the Photon
//! logger mod, external notifiers, tools that tag users. `docs/desktop-ipc.md` is the contract:
//! packets read here are relayed to the server as `IpcPacket`s, `SendIpc` actions are written to
//! every connected program, and a `LaunchCommand` opens the web client's launch page.

use serde_json::Value;

use crate::proto::ipc_packet::Packet;
use crate::proto::{IpcCustomTag, IpcExternal, IpcMsgPing, LogNoData, LogText, SendIpc};

/// What one packet read from the pipe asks for.
#[derive(Debug, PartialEq)]
pub enum Incoming {
    /// Relayed to the server.
    Relay(Packet),
    /// Another VRCX start handing over a `vrcx://` command.
    LaunchCommand(String),
}

#[cfg_attr(not(windows), allow(dead_code))] // Read on the Windows pipe only.
fn string(v: &Value, key: &str) -> String {
    match v.get(key) {
        Some(Value::String(s)) => s.clone(),
        Some(Value::Null) | None => String::new(),
        Some(other) => other.to_string(),
    }
}

/// Maps one packet's JSON (keys case-sensitive, as the original read them). The Photon stream goes
/// on as it was read; packets with no case (`VRCXLaunch`, anything else) give `None`.
#[cfg_attr(not(windows), allow(dead_code))] // Read on the Windows pipe only.
pub fn parse_packet(json: &[u8]) -> Option<Incoming> {
    let v: Value = serde_json::from_slice(json).ok()?;
    let packet = match v.get("type")?.as_str()? {
        "VrcxMessage" => match v.get("MsgType")?.as_str()? {
            "CustomTag" => Packet::CustomTag(IpcCustomTag {
                user_id: string(&v, "UserId"),
                tag: string(&v, "Tag"),
                tag_colour: string(&v, "TagColour"),
            }),
            "ClearCustomTags" => Packet::ClearCustomTags(LogNoData {}),
            "Noty" => Packet::Noty(LogText { text: string(&v, "Data") }),
            "External" => Packet::External(IpcExternal {
                user_id: string(&v, "UserId"),
                display_name: string(&v, "DisplayName"),
                message: string(&v, "Data"),
                notify: v.get("notify").and_then(Value::as_bool),
            }),
            _ => return None,
        },
        "Ping" => Packet::Ping(LogNoData {}),
        "MsgPing" => Packet::MsgPing(IpcMsgPing {
            version: v.get("version").and_then(Value::as_i64).and_then(|n| i32::try_from(n).ok()).unwrap_or_default(),
        }),
        "LaunchCommand" => return Some(Incoming::LaunchCommand(string(&v, "command"))),
        // OnOperationRequest only ever reached the original's debug log.
        "OnEvent" | "OnOperationResponse" | "VRCEvent" | "Event7List" => {
            Packet::PhotonJson(String::from_utf8_lossy(json).into_owned())
        }
        _ => return None,
    };
    Some(Incoming::Relay(packet))
}

/// A packet to write: compact JSON in `IPCPacket`'s key order, then the 0x00 separator.
pub fn frame(packet: &SendIpc) -> Vec<u8> {
    let data = if packet.data.is_empty() { Value::Null } else { Value::String(packet.data.clone()) };
    let mut out = format!(
        "{{\"Type\":{},\"Data\":{},\"MsgType\":{}}}",
        Value::String(packet.r#type.clone()),
        data,
        Value::String(packet.msg_type.clone())
    )
    .into_bytes();
    out.push(0);
    out
}

/// Splits what a program writes into packets: each one ends at a 0x00 byte, a read may hold part
/// of one or several, and empty pieces are skipped.
#[cfg_attr(not(windows), allow(dead_code))] // Read on the Windows pipe only.
#[derive(Default)]
pub struct Framer {
    buffer: Vec<u8>,
}

/// More than this without a separator is not a packet; the buffer is dropped.
#[cfg_attr(not(windows), allow(dead_code))] // Read on the Windows pipe only.
const MAX_PACKET: usize = 1 << 20;

#[cfg_attr(not(windows), allow(dead_code))] // Read on the Windows pipe only.
impl Framer {
    pub fn push(&mut self, bytes: &[u8]) -> Vec<Vec<u8>> {
        let mut packets = Vec::new();
        for &b in bytes {
            if b == 0 {
                if !self.buffer.is_empty() {
                    packets.push(std::mem::take(&mut self.buffer));
                }
            } else if self.buffer.len() < MAX_PACKET {
                self.buffer.push(b);
            } else {
                self.buffer.clear();
            }
        }
        packets
    }
}

#[cfg(windows)]
pub use server::serve;

#[cfg(windows)]
mod server {
    use std::time::Duration;

    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::windows::named_pipe::{NamedPipeServer, ServerOptions};
    use tokio::sync::{broadcast, mpsc};
    use tracing::{debug, info, warn};

    use super::*;

    /// Accepts programs on the pipe for as long as the agent runs. While another program (the
    /// original VRCX) owns the name, tries again every minute.
    pub async fn serve(name: String, incoming: mpsc::Sender<Incoming>, outgoing: broadcast::Sender<Vec<u8>>) {
        let mut first = true;
        loop {
            let server = match ServerOptions::new().first_pipe_instance(first).create(&name) {
                Ok(server) => server,
                Err(e) => {
                    if first {
                        warn!("could not open the VRCX pipe {name} (is the original VRCX running?): {e}");
                    } else {
                        warn!("could not open another instance of the VRCX pipe: {e}");
                    }
                    tokio::time::sleep(Duration::from_secs(60)).await;
                    continue;
                }
            };
            if first {
                info!("VRCX pipe open at {name}");
            }
            first = false;
            if let Err(e) = server.connect().await {
                debug!("a program left the VRCX pipe before it was accepted: {e}");
                continue;
            }
            tokio::spawn(client(server, incoming.clone(), outgoing.subscribe()));
        }
    }

    async fn client(
        pipe: NamedPipeServer,
        incoming: mpsc::Sender<Incoming>,
        mut outgoing: broadcast::Receiver<Vec<u8>>,
    ) {
        debug!("a program connected to the VRCX pipe");
        let (mut reader, mut writer) = tokio::io::split(pipe);
        let write = async move {
            loop {
                match outgoing.recv().await {
                    Ok(packet) => {
                        if writer.write_all(&packet).await.is_err() {
                            return;
                        }
                    }
                    Err(broadcast::error::RecvError::Lagged(_)) => continue,
                    Err(broadcast::error::RecvError::Closed) => return,
                }
            }
        };
        let read = async move {
            let mut framer = Framer::default();
            let mut buf = vec![0u8; 8192];
            loop {
                let n = match reader.read(&mut buf).await {
                    Ok(0) | Err(_) => return,
                    Ok(n) => n,
                };
                for packet in framer.push(&buf[..n]) {
                    if let Some(message) = parse_packet(&packet) {
                        // Full means the server is away; the packet is dropped like any sent then.
                        let _ = incoming.try_send(message);
                    }
                }
            }
        };
        // Either side ending (the program left, or a write failed) drops the client.
        tokio::select! {
            () = write => {}
            () = read => {}
        }
        debug!("a program left the VRCX pipe");
    }

    #[cfg(test)]
    mod tests {
        use tokio::net::windows::named_pipe::ClientOptions;

        use super::*;

        #[tokio::test]
        async fn packets_are_read_and_written_on_the_pipe() {
            let name = format!(r"\\.\pipe\vrcxd-agent-ipc-test-{}", std::process::id());
            let (in_tx, mut in_rx) = mpsc::channel(8);
            let (out_tx, _) = broadcast::channel(8);
            tokio::spawn(serve(name.clone(), in_tx, out_tx.clone()));
            let mut program = loop {
                match ClientOptions::new().open(&name) {
                    Ok(p) => break p,
                    Err(_) => tokio::time::sleep(Duration::from_millis(20)).await,
                }
            };
            // Two packets in one write, the second split across writes.
            program.write_all(b"{\"type\":\"Ping\"}\0{\"type\":\"VrcxMessage\",\"MsgType\":\"Noty\",").await.unwrap();
            program.write_all(b"\"Data\":\"hi\"}\0").await.unwrap();
            assert_eq!(in_rx.recv().await, Some(Incoming::Relay(Packet::Ping(LogNoData {}))));
            assert_eq!(in_rx.recv().await, Some(Incoming::Relay(Packet::Noty(LogText { text: "hi".into() }))));

            // Wait for the client's writer to subscribe, then write to it.
            while out_tx.receiver_count() == 0 {
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
            out_tx
                .send(frame(&SendIpc {
                    r#type: "VRCXLaunch".into(),
                    msg_type: "VRCXLaunch".into(),
                    data: String::new(),
                }))
                .unwrap();
            let mut got = vec![0u8; 128];
            let n = program.read(&mut got).await.unwrap();
            assert_eq!(&got[..n], b"{\"Type\":\"VRCXLaunch\",\"Data\":null,\"MsgType\":\"VRCXLaunch\"}\0");
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn relay(json: &str) -> Packet {
        match parse_packet(json.as_bytes()) {
            Some(Incoming::Relay(p)) => p,
            other => panic!("not relayed: {other:?}"),
        }
    }

    #[test]
    fn packets_map_as_the_contract_says() {
        assert_eq!(
            relay(
                r##"{"type":"VrcxMessage","MsgType":"CustomTag","UserId":"usr_1","Tag":"Friend","TagColour":"#ff0000"}"##
            ),
            Packet::CustomTag(IpcCustomTag {
                user_id: "usr_1".into(),
                tag: "Friend".into(),
                tag_colour: "#ff0000".into()
            })
        );
        // A missing Tag removes the user's tag.
        assert_eq!(
            relay(r#"{"type":"VrcxMessage","MsgType":"CustomTag","UserId":"usr_1"}"#),
            Packet::CustomTag(IpcCustomTag { user_id: "usr_1".into(), ..Default::default() })
        );
        assert_eq!(
            relay(r#"{"type":"VrcxMessage","MsgType":"ClearCustomTags"}"#),
            Packet::ClearCustomTags(LogNoData {})
        );
        assert_eq!(
            relay(r#"{"type":"VrcxMessage","MsgType":"Noty","Data":"hello"}"#),
            Packet::Noty(LogText { text: "hello".into() })
        );
        assert_eq!(
            relay(
                r#"{"type":"VrcxMessage","MsgType":"External","Data":"m","UserId":"usr_1","DisplayName":"Bob","notify":false}"#
            ),
            Packet::External(IpcExternal {
                user_id: "usr_1".into(),
                display_name: "Bob".into(),
                message: "m".into(),
                notify: Some(false)
            })
        );
        // notify is set only when the JSON has it.
        assert!(matches!(
            relay(r#"{"type":"VrcxMessage","MsgType":"External","Data":"m"}"#),
            Packet::External(IpcExternal { notify: None, .. })
        ));
        assert_eq!(relay(r#"{"type":"Ping"}"#), Packet::Ping(LogNoData {}));
        assert_eq!(relay(r#"{"type":"MsgPing","version":22}"#), Packet::MsgPing(IpcMsgPing { version: 22 }));
    }

    #[test]
    fn launch_commands_and_unknown_packets() {
        assert_eq!(
            parse_packet(br#"{"type":"LaunchCommand","command":"world/wrld_1"}"#),
            Some(Incoming::LaunchCommand("world/wrld_1".into()))
        );
        let photon =
            r#"{"type":"OnEvent","OnEventData":{"Code":4,"Parameters":{"254":2}},"dt":"2024-01-01T00:00:00Z"}"#;
        assert_eq!(parse_packet(photon.as_bytes()), Some(Incoming::Relay(Packet::PhotonJson(photon.into()))));
        for ignored in [
            r#"{"type":"OnOperationRequest","OnOperationRequestData":{}}"#,
            r#"{"type":"VRCXLaunch"}"#,
            r#"{"Type":"Ping"}"#,
            "not json",
            r#"{"type":"VrcxMessage","MsgType":"Other"}"#,
        ] {
            assert_eq!(parse_packet(ignored.as_bytes()), None, "{ignored}");
        }
    }

    #[test]
    fn written_packets_keep_the_originals_shape() {
        let packet =
            SendIpc { r#type: "VrcxMessage".into(), msg_type: "ShowUserDialog".into(), data: "usr_\"1".into() };
        assert_eq!(
            frame(&packet),
            b"{\"Type\":\"VrcxMessage\",\"Data\":\"usr_\\\"1\",\"MsgType\":\"ShowUserDialog\"}\0"
        );
    }

    #[test]
    fn framing_splits_on_zero_and_keeps_partial_packets() {
        let mut framer = Framer::default();
        assert_eq!(framer.push(b"a\0\0b"), vec![b"a".to_vec()]);
        assert_eq!(framer.push(b"c\0"), vec![b"bc".to_vec()]);
        assert!(framer.push(b"").is_empty());
    }
}
