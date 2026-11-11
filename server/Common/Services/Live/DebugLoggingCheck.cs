using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VRCX.Common.Live;

namespace VRCX.Common.Services.Live;

/// <summary>
/// The original's runCheckVRChatDebugLoggingFlow: a minute after VRChat starts or stops, and when
/// the PC's agent connects, LOGGING_ENABLED is read from VRChat's registry and set back to 1 when it
/// was turned off, since the game log needs it. The web client tells the user what happened.
/// </summary>
public sealed class DebugLoggingCheck(DesktopClients desktops, ILiveEventBus bus, ILogger<DebugLoggingCheck> logger)
{
    public const string Key = "LOGGING_ENABLED";

    /// <summary>How long after a game state change to look (the original's 60 s).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();

    /// <summary>Checks after <see cref="Delay"/>, replacing a check still waiting for this PC.</summary>
    public void Schedule(string clientId, string? accountId)
    {
        if (accountId is null) return;
        var cts = new CancellationTokenSource();
        if (_pending.TryGetValue(clientId, out var previous)) previous.Cancel();
        _pending[clientId] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Delay, cts.Token);
                await CheckAsync(clientId, accountId, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Checking VRChat's debug logging on {Client} failed", clientId);
            }
            finally
            {
                _pending.TryRemove(new KeyValuePair<string, CancellationTokenSource>(clientId, cts));
                cts.Dispose();
            }
        }, CancellationToken.None);
    }

    private async Task CheckAsync(string clientId, string accountId, CancellationToken ct)
    {
        if (desktops.Get(clientId) is not { } desktop || !desktop.Can(Capability.Registry)) return;
        var read = await desktops.RequestAsync(clientId, new ClientAction { ReadVrchatRegistryKey = new ReadVrchatRegistryKey { Key = Key } }, ct);
        if (read is not { Status: ActionStatus.Ok } || string.IsNullOrEmpty(read.Json)) return;
        using var doc = JsonDocument.Parse(read.Json);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null) return;
        var on = value.ValueKind == JsonValueKind.Number ? value.TryGetInt32(out var n) && n == 1 : value.ToString().Trim() == "1";
        if (on) return;

        var set = await desktops.RequestAsync(clientId, new ClientAction
        {
            SetVrchatRegistryKey = new SetVrchatRegistryKey { Key = Key, ValueJson = "\"1\"", Type = 4 },
        }, ct);
        bus.Publish(LiveEvents.AgentAlert(accountId, set?.Status == ActionStatus.Ok ? AgentAlertKind.DebugLoggingEnabled : AgentAlertKind.DebugLoggingFailed));
    }
}
