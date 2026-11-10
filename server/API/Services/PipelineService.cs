using System.Collections.Concurrent;
using VRCX.Common.Services.Automation;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Sessions;
using VRCX.Vrchat;
using VRCX.Vrchat.Pipeline;

namespace VRCX.API.Services;

/// <summary>
/// Keeps one pipeline websocket open per live account, reconnecting with backoff. Also the
/// place new logins (made through the API host) get picked up: every tick resumes any account
/// row this process has no client for yet.
/// </summary>
public sealed class PipelineService(IAccountSessionManager sessions, IPipelineIngestor ingestor, AccountAutomation automation, ILogger<PipelineService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, Task> _connections = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await sessions.ResumeAllAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not load account sessions from the database");
            }

            foreach (var accountId in sessions.ActiveAccountIds)
            {
                if (_connections.TryGetValue(accountId, out var existing) && !existing.IsCompleted) continue;
                _connections[accountId] = RunAsync(accountId, stoppingToken);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task RunAsync(string accountId, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            var client = sessions.Get(accountId);
            if (client is null) return;
            try
            {
                var token = await client.GetPipelineTokenAsync(ct);
                logger.LogInformation("Pipeline connecting for {Account}", accountId);
                await DeleteOldPrintsAsync(accountId, ct);
                PipelineErrorEvent? error = null;
                await foreach (var e in PipelineConnection.ReadAsync(token, logger, client.Endpoint.Pipeline, ct))
                {
                    // An error frame is no sign of a good connection: keep backing off.
                    if (e is PipelineErrorEvent err)
                    {
                        error = err;
                        logger.LogWarning("Pipeline for {Account} answered an error: {Error}", accountId, err.Message);
                        continue;
                    }
                    backoff = TimeSpan.FromSeconds(1);
                    await DispatchAsync(accountId, e, ct);
                }
                // A token VRChat says has no session: ask whether the session itself is gone, so a dead
                // login is invalidated instead of asking for a new token forever.
                if (error is { IsAuthError: true }) await client.GetCurrentUserAsync(ct);
                logger.LogInformation("Pipeline closed for {Account}; reconnecting", accountId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (VrchatUnauthorizedException)
            {
                await sessions.InvalidateAsync(accountId, CancellationToken.None);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Pipeline for {Account} failed; retrying in {Backoff}", accountId, backoff);
            }
            await Task.Delay(backoff, ct);
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
        }
    }

    /// <summary>
    /// What happens to one decoded frame: stored, then answered when it is an invite request.
    /// Public so the integration tests can push frames through exactly this path.
    /// </summary>
    public async Task DispatchAsync(string accountId, PipelineEvent e, CancellationToken ct)
    {
        try
        {
            await ingestor.HandleAsync(accountId, e, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to store pipeline {Type} for {Account}: {Content}", e.Type, accountId, e.Content);
        }
        if (e is NotificationEvent { Notification.Type: "requestInvite" } request)
        {
            try
            {
                await automation.AnswerInviteRequestAsync(accountId, request.Notification, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is not VrchatUnauthorizedException)
            {
                logger.LogWarning(ex, "Could not answer an invite request for {Account}", accountId);
            }
        }
        if (e is ContentRefreshEvent { Payload: { ContentType: "print", ActionType: "created" } })
            await DeleteOldPrintsAsync(accountId, ct);
        if (e is NotificationEvent { Notification.Type: "friendRequest" } friendRequest)
        {
            try
            {
                await automation.DeclineFriendRequestAsync(accountId, friendRequest.Notification, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is not VrchatUnauthorizedException)
            {
                logger.LogWarning(ex, "Could not auto decline a friend request for {Account}", accountId);
            }
        }
    }

    private async Task DeleteOldPrintsAsync(string accountId, CancellationToken ct)
    {
        try
        {
            await automation.DeleteOldPrintsAsync(accountId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is not VrchatUnauthorizedException)
        {
            logger.LogWarning(ex, "Could not delete old prints for {Account}", accountId);
        }
    }
}
