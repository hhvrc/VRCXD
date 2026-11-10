using System.Net;

namespace VRCX.Vrchat;

/// <summary>
/// Puts every request to a VRChat host through the shared <see cref="VrchatRateLimiter"/> and
/// retries 429s, so a burst of lookups (paging a 1000-friend list, resolving every world in a
/// feed) across any number of accounts never trips VRChat's rate limit hard enough to get this
/// IP's requests rejected. Requests to other hosts pass straight through.
/// </summary>
public sealed class VrchatThrottlingHandler : DelegatingHandler
{
    private readonly VrchatRateLimiter _limiter;
    private readonly int _maxRetries;

    public VrchatThrottlingHandler(HttpMessageHandler inner, VrchatRateLimiter limiter, int maxRetries = 3) : base(inner)
    {
        _limiter = limiter;
        _maxRetries = maxRetries;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var limited = VrchatRateLimiter.AppliesTo(request.RequestUri);
        for (var attempt = 0; ; attempt++)
        {
            if (limited) await _limiter.AcquireAsync(cancellationToken);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= _maxRetries)
                return response;

            var delay = RetryDelay(response, attempt);
            response.Dispose();
            if (limited)
            {
                // Park the whole budget, not just this request: the next AcquireAsync waits it out.
                _limiter.Penalize(delay);
            }
            else
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } at) retryAfter = at - DateTimeOffset.UtcNow;
        var delay = retryAfter ?? TimeSpan.FromSeconds(5 * (attempt + 1));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}
