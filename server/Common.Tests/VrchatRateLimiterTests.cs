using System.Net;
using System.Net.Http.Headers;
using System.Text;
using VRCX.Vrchat;

namespace VRCX.Common.Tests;

// How the server paces what it sends to VRChat: exactly the original's two paced reads, a 429
// handled as the original does, and VRChat's own Retry-After honoured for every client.
public class VrchatRateLimiterTests
{
    private static readonly Uri Api = new("https://api.vrchat.cloud/api/1/");

    [Test]
    public async Task The_policy_paces_what_the_original_paces_and_nothing_else()
    {
        var policy = VrchatRatePolicy.Upstream;

        // bulkRefreshFriends in src/stores/friend.js: 60 a minute, five retries from one second.
        var friends = policy.For(new Uri(Api, "auth/user/friends?offline=false&n=100&offset=0"))!;
        await Assert.That(friends.Limiter.Windows.Select(w => (w.Limit, w.Interval))).IsEquivalentTo([(60, TimeSpan.FromMinutes(1))]);
        await Assert.That((friends.MaxRetries, friends.RetryBaseDelay)).IsEqualTo((5, TimeSpan.FromSeconds(1)));

        // fetchMutualGraph and fetchMutualFriendsForUser in src/stores/charts.js: 5 a second, four retries from half a second.
        var mutuals = policy.For(new Uri(Api, "users/usr_a/mutuals/friends?n=100&offset=0"))!;
        await Assert.That(mutuals.Limiter.Windows.Select(w => (w.Limit, w.Interval))).IsEquivalentTo([(5, TimeSpan.FromSeconds(1))]);
        await Assert.That((mutuals.MaxRetries, mutuals.RetryBaseDelay)).IsEqualTo((4, TimeSpan.FromMilliseconds(500)));

        foreach (var path in new[] { "auth/user", "users/usr_a", "users/usr_a/mutuals", "users/usr_a/friendStatus", "worlds/wrld_a", "instances/wrld_a:1", "file/file_a/1/file", "auth/user/friends/usr_a" })
            await Assert.That(policy.For(new Uri(Api, path))).IsNull();
        await Assert.That(VrchatRatePolicy.None.Rules.Count).IsEqualTo(0);
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

    // The freeze this replaced: a profile, an instance and the inventory waiting a minute behind a
    // sync, because every request shared the friend list's 60 a minute.
    [Test]
    public async Task Requests_the_original_does_not_pace_go_out_as_they_come()
    {
        var backend = new ScriptedBackend(() => Ok());
        using var client = VrchatApiClient.Create(inner: backend, rates: VrchatRatePolicy.Upstream);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => client.GetUserAsync($"usr_{i}")));

        await Assert.That(backend.Calls).IsEqualTo(200);
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task A_paced_request_waits_for_its_pace()
    {
        var rule = new VrchatRateRule("users", new System.Text.RegularExpressions.Regex("/users/"), new VrchatRateLimiter([new(2, TimeSpan.FromMilliseconds(300))]), 0, TimeSpan.Zero);
        var backend = new ScriptedBackend(() => Ok());
        using var client = VrchatApiClient.Create(inner: backend, rates: new VrchatRatePolicy([rule]));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 3; i++) await client.GetUserAsync($"usr_{i}");

        await Assert.That(clock.Elapsed).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
    }

    // The original's request layer throws a 429 like any other status; only the two paced reads retry it.
    [Test]
    public async Task A_429_on_an_unpaced_request_is_handed_back_at_once()
    {
        var backend = new ScriptedBackend(() => TooManyRequests(null));
        using var http = new HttpClient(new VrchatThrottlingHandler(backend, VrchatRatePolicy.None));

        using var response = await http.GetAsync(new Uri(Api, "users/usr_a"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(backend.Calls).IsEqualTo(1);
    }

    // The original's executeWithBackoff: retry with a doubling delay, then hand back the 429.
    [Test]
    public async Task A_429_on_a_paced_request_is_retried_with_backoff_and_then_handed_back()
    {
        var rule = new VrchatRateRule("users", new System.Text.RegularExpressions.Regex("/users/"), new VrchatRateLimiter([]), MaxRetries: 2, RetryBaseDelay: TimeSpan.FromMilliseconds(100));
        var backend = new ScriptedBackend(() => TooManyRequests(null));
        using var http = new HttpClient(new VrchatThrottlingHandler(backend, new VrchatRatePolicy([rule])));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        using var response = await http.GetAsync(new Uri(Api, "users/usr_a"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(backend.Calls).IsEqualTo(3);
        // 100 ms, then 200 ms.
        await Assert.That(clock.Elapsed).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(280));
    }

    [Test]
    public async Task A_retry_after_pauses_every_client_sharing_the_policy_and_the_request_is_sent_again()
    {
        var policy = new VrchatRatePolicy([]);
        var backend = new CountingBackend(firstResponse: TooManyRequests(TimeSpan.FromMilliseconds(400)));
        using var a = VrchatApiClient.Create(inner: backend, rates: policy);
        using var b = VrchatApiClient.Create(inner: backend, rates: policy);
        // a gets the 429 and is sent again after Retry-After; b, sent right after, must not jump the pause.
        var first = a.GetUserAsync("usr_a");
        await Task.Delay(20);
        var second = b.GetUserAsync("usr_b");
        await Task.WhenAll(first, second);

        await Assert.That(backend.Calls).IsEqualTo(3);
        await Assert.That(backend.SentAt[1] - backend.SentAt[0]).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(350));
        await Assert.That(backend.SentAt[2] - backend.SentAt[0]).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(350));
    }

    [Test]
    public async Task A_request_refused_with_a_retry_after_every_time_is_handed_back_in_the_end()
    {
        var backend = new ScriptedBackend(() => TooManyRequests(TimeSpan.Zero));
        using var http = new HttpClient(new VrchatThrottlingHandler(backend, new VrchatRatePolicy([])));

        using var response = await http.GetAsync(new Uri(Api, "users/usr_a"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(backend.Calls).IsEqualTo(1 + VrchatRatePolicy.RetryAfterRetries);
    }

    [Test]
    public async Task Other_errors_are_not_retried()
    {
        var backend = new ScriptedBackend(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var http = new HttpClient(new VrchatThrottlingHandler(backend, VrchatRatePolicy.Upstream));

        using var response = await http.GetAsync(new Uri(Api, "auth/user/friends"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(backend.Calls).IsEqualTo(1);
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"id":"usr_x","displayName":"x"}""", Encoding.UTF8, "application/json"),
    };

    private sealed class ScriptedBackend(Func<HttpResponseMessage> answer) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(answer());
        }
    }

    private static HttpResponseMessage TooManyRequests(TimeSpan? retryAfter) =>
        new(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = retryAfter is { } delay ? new RetryConditionHeaderValue(delay) : null },
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
