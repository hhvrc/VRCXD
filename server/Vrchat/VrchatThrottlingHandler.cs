using System.Net;

namespace VRCX.Vrchat;

/// <summary>
/// Applies the <see cref="VrchatRatePolicy"/> to what a client sends. A request under one of its
/// rules waits for that rule's pace and has a 429 retried with the rule's backoff. Every other
/// request goes straight out, and a 429 without a Retry-After is handed back to the caller as
/// VRChat's answer. A 429 with a Retry-After pauses everything the policy covers until it has
/// passed, and the refused request is then sent again.
/// </summary>
public sealed class VrchatThrottlingHandler(HttpMessageHandler inner, VrchatRatePolicy policy) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var rule = policy.For(request.RequestUri);
        var retries = 0;
        var paused = 0;
        while (true)
        {
            await policy.WaitForPauseAsync(cancellationToken);
            if (rule is not null) await rule.Limiter.AcquireAsync(cancellationToken);
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests) return response;

            if (RetryAfter(response) is { } retryAfter && paused < VrchatRatePolicy.RetryAfterRetries)
            {
                paused++;
                response.Dispose();
                policy.Pause(retryAfter);
                continue;
            }
            if (rule is null || retries >= rule.MaxRetries) return response;

            response.Dispose();
            // The original's executeWithBackoff: the base delay, doubled on every retry.
            await Task.Delay(rule.RetryBaseDelay * Math.Pow(2, retries++), cancellationToken);
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } at) retryAfter = at - DateTimeOffset.UtcNow;
        return retryAfter is null ? null : retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter;
    }
}
