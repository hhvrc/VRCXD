using System.Net;
using System.Net.Http.Headers;
using System.Text;
using VRCX.Vrchat;

namespace VRCX.Common.Tests;

// The process-wide VRChat budget: which hosts it covers, that the sliding windows hold,
// and that a 429 on one client parks every client sharing the limiter.
public class VrchatRateLimiterTests
{
    [Test]
    public async Task Only_vrchat_hosts_are_limited()
    {
        await Assert.That(VrchatRateLimiter.AppliesTo(new Uri("https://api.vrchat.cloud/api/1/auth/user"))).IsTrue();
        await Assert.That(VrchatRateLimiter.AppliesTo(new Uri("wss://pipeline.vrchat.cloud/?authToken=x"))).IsTrue();
        await Assert.That(VrchatRateLimiter.AppliesTo(new Uri("https://VRChat.com/home"))).IsTrue();
        await Assert.That(VrchatRateLimiter.AppliesTo(new Uri("https://assets.vrchat.com/x.png"))).IsTrue();
        await Assert.That(VrchatRateLimiter.AppliesTo(new Uri("https://notvrchat.cloud/"))).IsFalse();
        await Assert.That(VrchatRateLimiter.AppliesTo(new Uri("https://example.com/vrchat.cloud"))).IsFalse();
        await Assert.That(VrchatRateLimiter.AppliesTo(new Uri("auth/user", UriKind.Relative))).IsFalse();
    }

    [Test]
    public async Task Upstream_policy_is_five_per_second_and_sixty_per_minute()
    {
        await Assert.That(VrchatRateLimiter.UpstreamPolicy.Select(w => (w.Limit, w.Interval)))
            .IsEquivalentTo([(5, TimeSpan.FromSeconds(1)), (60, TimeSpan.FromMinutes(1))]);
        await Assert.That(VrchatRateLimiter.Shared.Windows.Count).IsEqualTo(2);
        await Assert.That(VrchatRateLimiter.Unlimited.Windows.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Sliding_window_lets_the_burst_through_and_delays_the_next()
    {
        var limiter = new VrchatRateLimiter([new(3, TimeSpan.FromMilliseconds(300))]);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 3; i++) await limiter.AcquireAsync();
        var burst = clock.Elapsed;
        await limiter.AcquireAsync();
        var fourth = clock.Elapsed;

        await Assert.That(burst).IsLessThan(TimeSpan.FromMilliseconds(150));
        await Assert.That(fourth).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
    }

    [Test]
    public async Task A_429_on_one_client_parks_every_client_sharing_the_limiter()
    {
        var limiter = new VrchatRateLimiter([new(100, TimeSpan.FromSeconds(10))]);
        var backend = new CountingBackend(firstResponse: TooManyRequests(TimeSpan.FromMilliseconds(400)));
        using var a = VrchatApiClient.Create(inner: backend, rateLimiter: limiter);
        using var b = VrchatApiClient.Create(inner: backend, rateLimiter: limiter);
        // a gets the 429, retries after Retry-After; b, sent right after, must not jump the penalty.
        var first = a.GetUserAsync("usr_a");
        await Task.Delay(20);
        var second = b.GetUserAsync("usr_b");
        await Task.WhenAll(first, second);

        await Assert.That(backend.Calls).IsEqualTo(3);
        await Assert.That(backend.SentAt[1] - backend.SentAt[0]).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(350));
        await Assert.That(backend.SentAt[2] - backend.SentAt[0]).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(350));
    }

    // The original's executeWithBackoff: give up after the last retry and hand back the 429.
    [Test]
    public async Task After_the_last_retry_the_429_is_handed_back()
    {
        var backend = new ScriptedBackend(() => TooManyRequests(TimeSpan.Zero));
        using var http = new HttpClient(new VrchatThrottlingHandler(backend, VrchatRateLimiter.Unlimited, maxRetries: 2));

        using var response = await http.GetAsync("https://example.com/x");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(backend.Calls).IsEqualTo(3);
    }

    [Test]
    public async Task Other_errors_are_not_retried()
    {
        var backend = new ScriptedBackend(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var http = new HttpClient(new VrchatThrottlingHandler(backend, VrchatRateLimiter.Unlimited));

        using var response = await http.GetAsync("https://example.com/x");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(backend.Calls).IsEqualTo(1);
    }

    private sealed class ScriptedBackend(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(answer());
        }
    }

    [Test]
    public async Task Unlimited_never_waits()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 50; i++) await VrchatRateLimiter.Unlimited.AcquireAsync();
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromMilliseconds(200));
    }

    private static HttpResponseMessage TooManyRequests(TimeSpan retryAfter) =>
        new(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = new RetryConditionHeaderValue(retryAfter) },
            Content = new StringContent("""{"error":{"message":"Too Many Requests","status_code":429}}""", Encoding.UTF8, "application/json"),
        };

    /// <summary>Answers the first request as told and every later one with an empty user; records when each arrived.</summary>
    private sealed class CountingBackend(HttpResponseMessage firstResponse) : HttpMessageHandler
    {
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private HttpResponseMessage? _first = firstResponse;
        public int Calls { get; private set; }
        public List<TimeSpan> SentAt { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (SentAt)
            {
                Calls++;
                SentAt.Add(_clock.Elapsed);
                var first = _first;
                _first = null;
                return Task.FromResult(first ?? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"id":"usr_x","displayName":"x"}""", Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
