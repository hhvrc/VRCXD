using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VRCX.Vrchat.Pipeline;

/// <summary>
/// Reads the pipeline websocket (wss://pipeline.vrchat.cloud/?auth=token)
/// and yields decoded events until the socket closes or the token is
/// cancelled. Reconnection policy belongs to the caller - this just streams
/// one connection.
/// </summary>
public static class PipelineConnection
{
    public static async IAsyncEnumerable<PipelineEvent> ReadAsync(
        string token,
        ILogger logger,
        Uri? address,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var socket = new ClientWebSocket();
        // ClientWebSocket shares nothing with the HttpClient - without this
        // header the handshake is rejected with 403 before auth is checked.
        socket.Options.SetRequestHeader("User-Agent", VrchatApiClient.UserAgent);
        // A ping every 30 s that must be answered within 30 s: a connection a router dropped without
        // closing it is aborted instead of waiting on it forever.
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        await socket.ConnectAsync(new Uri($"{address ?? VrchatEndpoint.Default.Pipeline}?auth={Uri.EscapeDataString(token)}"), ct);
        logger.LogInformation("Pipeline websocket connected");

        var buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var text = await ReceiveFrameAsync(socket, buffer, logger, ct);
            if (text is null) yield break;
            PipelineEvent decoded;
            try
            {
                decoded = PipelineEvent.Parse(text);
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogWarning(ex, "Undecodable pipeline frame: {Frame}", text.Length > 500 ? text[..500] : text);
                continue;
            }
            yield return decoded;
        }
    }

    /// <summary>
    /// With nothing at all for this long the connection is given up and the caller reconnects: a
    /// silent pipeline stops notifications and fine-grained presence, and reconnecting is cheap.
    /// </summary>
    public static TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>One whole frame as text, or null when the socket closed or went idle.</summary>
    private static async Task<string?> ReceiveFrameAsync(ClientWebSocket socket, byte[] buffer, ILogger logger, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(IdleTimeout);
        using var frame = new MemoryStream();
        try
        {
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(), idle.Token);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                frame.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Pipeline websocket silent for {Idle}; reconnecting", IdleTimeout);
            return null;
        }
        return Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length);
    }
}
