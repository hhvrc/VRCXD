# VRCX named pipe in the agent

The original VRCX ran a named pipe (`Dotnet/IPC/IPCServer.cs`, `IPCClient.cs`, `IPCPacket.cs`) that
other programs on the PC use: VRCX's Photon logger mod, external notifiers, tools that tag users.
The PC agent (`agent/src/ipc.rs`) owns that pipe now. It parses each packet and relays it to the server over the live
socket (`server/Common/Protos/vrcx_live.proto`); the server applies it. The server can also ask the
app to write a packet to the pipe. This page is what the desktop side needs to build it.

## The pipe

- Name: `vrcx-ipc-<n>`, where `<n>` is the sum of the UTF-16 code units of the Windows user name
  (`Environment.UserName`), as a decimal number. So the pipe path is `\\.\pipe\vrcx-ipc-<n>`.
  Programs compute the name the same way, so it must match exactly.
- Duplex (`InOut`), byte mode, asynchronous, as many instances as clients connect
  (`MaxAllowedServerInstances`): after each connection is accepted a new listening instance is made.
- The original VRCX made the same pipe, so it and the agent should not both run. While VRCX holds the
  name, the agent tries again every minute.

## Framing

- Each packet is one UTF-8 JSON object (no BOM) followed by a single `0x00` byte.
- A read may hold part of a packet or several packets. Append to a buffer; when the buffer ends
  in `0x00`, split it on `0x00`, skip empty pieces and handle each piece. The original only split
  when the data read so far ended in `0x00`; splitting on every `0x00` as it arrives is equivalent.
- Packets the app writes use the same framing: compact JSON, then `0x00`.
- A read of 0 bytes, or an error, means the program went away: drop that client.

## Packets read from the pipe

Packet JSON keys are case-sensitive, exactly as below: the original read `type` in lower case and
the rest as written here. (Packets VRCX writes use `Type`, see below.)

Each handled packet becomes a `ClientMessage { ipc = IpcPacket { ... } }`. Set
`IpcPacket.received_at` to when the packet was read. Send it only after the hello with a client id;
the server ignores IPC from a connection without one.

| JSON `type` | `MsgType` | Other keys read | `IpcPacket` case |
|---|---|---|---|
| `VrcxMessage` | `CustomTag` | `UserId`, `Tag`, `TagColour` | `custom_tag` (`IpcCustomTag`: `user_id`, `tag`, `tag_colour`). An empty or missing `Tag` removes the user's tag. |
| `VrcxMessage` | `ClearCustomTags` | none | `clear_custom_tags` (`LogNoData`) |
| `VrcxMessage` | `Noty` | `Data` | `noty` (`LogText.text` = `Data`) |
| `VrcxMessage` | `External` | `Data`, `UserId`, `DisplayName`, `notify` | `external` (`IpcExternal`: `message` = `Data`, `user_id`, `display_name`; set `notify` only when the JSON has `notify`) |
| `Ping` | | none | `ping` (`LogNoData`), every time one arrives |
| `MsgPing` | | `version` (number) | `msg_ping` (`IpcMsgPing.version`) |
| `OnEvent`, `OnOperationResponse`, `VRCEvent`, `Event7List` | | the whole packet | `photon_json` (the packet's JSON as read) |

The Photon stream is relayed whole: the server parses it as the original's photon store did, and
drops it while VRChat is not running there.

Not relayed:

- `OnOperationRequest`: the original only logged it.
- `LaunchCommand` (`command`): another VRCX start handing over a `vrcx://` command. The app handles it
  itself by opening the web UI's `/launch?command=<command>` page, which does what the original's
  `eventLaunchCommand` did.
- `VRCXLaunch` and anything else: ignore (the original only logged them).

What the server does with them, for reference: custom tags are stored per account (the account
playing on the PC, or the only account) and pushed to pages as `CustomTagChanged`; `Noty` becomes a
game log Event in the PC's current instance unless a `Ping` came on this connection or the last
`MsgPing` version is above 21 (those programs notify themselves); `External` becomes a game log
External row there, and a notification unless `notify` is false.

## Packets written to the pipe

The server sends `ServerMessage { action = ClientAction { send_ipc = SendIpc { ... } } }` with no
`request_id`, so no `ActionResult` is expected. Write it to every connected pipe client as
`{"Type":"<type>","Data":<data or null>,"MsgType":"<msg_type>"}` followed by `0x00`, the same
key order as `IPCPacket`. Use `null` for an empty `data`.

| `type` | `msg_type` | `data` | When |
|---|---|---|---|
| `VRCXLaunch` | `VRCXLaunch` | empty | Right after the hello, when an account is signed in on the server (the original's `IPCAnnounceStart`). |
| `VrcxMessage` | `ShowUserDialog` | user id | A user dialog opened in the web UI (the original's `SendIpc`). |

A client that is not reading may block writes; write asynchronously and drop a client whose write
fails, as the original did.

## Testing

`server/Testing/Desktop/FakeDesktop.cs` sends these cases (`SendIpcAsync`, builders in `Ipc`) and
reads the written packets (`NextIpcAsync`); `server/API.IntegrationTests/Tests/IpcTests.cs` covers
the server side. The pipe itself (name, framing, JSON mapping) is tested in `agent/src/ipc.rs`.
