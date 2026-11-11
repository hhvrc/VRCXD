using Microsoft.EntityFrameworkCore;
using VRCX.Common.Live;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Live;

/// <summary>What the pipe on one PC has said about the programs connected to it.</summary>
public sealed class IpcSession
{
    /// <summary>A Ping came: a Photon logger runs (the original's photonLoggingEnabled, never turned off again).</summary>
    public bool PhotonLogger { get; set; }
    /// <summary>The external notifier's version from its MsgPing (the original's externalNotifierVersion).</summary>
    public int ExternalNotifierVersion { get; set; }
}

/// <summary>
/// The original's ipcEvent and eventVrcxMessage: applies a packet the desktop app read from the
/// VRCX named pipe on its PC. Custom tags are kept per account; Noty and External become game
/// log rows in the PC's current instance, as the original stored them with its lastLocation.
/// </summary>
public sealed class IpcIngestor(IDbContextFactory<VrcxContext> dbFactory, ILiveEventBus bus, VRCX.Common.Services.Live.Photon.PhotonService photon, ILogger<IpcIngestor> logger)
{
    /// <summary>Above this, the external notifier sends its own Noty notifications.</summary>
    private const int NotifierOwnNoty = 21;

    public async Task HandleAsync(GameLogSession session, IpcSession ipc, IpcPacket packet, CancellationToken ct)
    {
        var at = packet.ReceivedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow;
        switch (packet.PacketCase)
        {
            case IpcPacket.PacketOneofCase.Ping:
                ipc.PhotonLogger = true;
                session.PhotonLogger = true;
                photon.LoggerSeen(session);
                break;
            case IpcPacket.PacketOneofCase.PhotonJson:
                session.PhotonLogger = true;
                await photon.HandleAsync(session, packet.PhotonJson, ct);
                break;
            case IpcPacket.PacketOneofCase.MsgPing:
                ipc.ExternalNotifierVersion = packet.MsgPing.Version;
                break;
            case IpcPacket.PacketOneofCase.CustomTag:
                await CustomTagAsync(session, packet.CustomTag, at, ct);
                break;
            case IpcPacket.PacketOneofCase.ClearCustomTags:
                await ClearCustomTagsAsync(session, ct);
                break;
            case IpcPacket.PacketOneofCase.Noty:
                if (ipc.PhotonLogger || ipc.ExternalNotifierVersion > NotifierOwnNoty) break;
                if (string.IsNullOrEmpty(packet.Noty.Text)) break;
                await AddEventAsync(session, new GameLogEvent { At = at, InstanceLocation = session.CurrentInstanceLocation, Kind = "Event", Data = packet.Noty.Text }, "event", ct);
                break;
            case IpcPacket.PacketOneofCase.External:
                {
                    var external = packet.External;
                    var row = new GameLogEvent
                    {
                        At = at, InstanceLocation = session.CurrentInstanceLocation, Kind = "External", Data = external.Message,
                        UserId = string.IsNullOrEmpty(external.UserId) ? null : external.UserId, DisplayName = external.DisplayName,
                    };
                    // Stored either way; only a notifying one plays as a notification.
                    var notify = !external.HasNotify || external.Notify;
                    await AddEventAsync(session, row, notify ? "external" : "external_quiet", ct);
                    break;
                }
            default:
                logger.LogDebug("Ignoring an IPC packet with no case from {Client}", session.ClientId);
                break;
        }
    }

    /// <summary>The account playing on the PC, or the only account while its log has not said.</summary>
    private static async Task<string?> AccountAsync(VrcxContext db, GameLogSession session, CancellationToken ct)
    {
        if (session.AccountId is { } id) return await db.Accounts.AnyAsync(a => a.UserId == id, ct) ? id : null;
        return await db.Accounts.Select(a => a.UserId).Take(2).ToListAsync(ct) is [var only] ? only : null;
    }

    /// <summary>The original's addCustomTag: a tag replaces the user's tag, an empty one removes it.</summary>
    private async Task CustomTagAsync(GameLogSession session, IpcCustomTag tag, DateTimeOffset at, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tag.UserId)) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccountAsync(db, session, ct) is not { } account) return;
        var row = await db.CustomUserTags.FindAsync([account, tag.UserId], ct);
        if (string.IsNullOrEmpty(tag.Tag))
        {
            if (row is not null) db.CustomUserTags.Remove(row);
        }
        else
        {
            if (row is null)
            {
                row = new CustomUserTag { OwnerUserId = account, UserId = tag.UserId, Tag = tag.Tag };
                db.CustomUserTags.Add(row);
            }
            row.Tag = tag.Tag;
            row.Colour = string.IsNullOrEmpty(tag.TagColour) ? null : tag.TagColour;
            row.UpdatedAt = at;
        }
        await db.SaveChangesAsync(ct);
        bus.Publish(LiveEvents.CustomTag(account, tag.UserId, tag.Tag, tag.TagColour, cleared: false));
    }

    private async Task ClearCustomTagsAsync(GameLogSession session, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccountAsync(db, session, ct) is not { } account) return;
        await db.CustomUserTags.Where(t => t.OwnerUserId == account).ExecuteDeleteAsync(ct);
        bus.Publish(LiveEvents.CustomTag(account, "", "", "", cleared: true));
    }

    private async Task AddEventAsync(GameLogSession session, GameLogEvent row, string kind, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.GameLogEvents.Add(row);
        await db.SaveChangesAsync(ct);
        bus.Publish(LiveEvents.GameLog(session.AccountId ?? await AccountAsync(db, session, ct), session.CurrentInstanceLocation, [kind]));
    }
}
