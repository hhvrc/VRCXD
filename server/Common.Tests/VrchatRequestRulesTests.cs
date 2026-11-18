using System.Net;
using System.Text;
using VRCX.Vrchat;

namespace VRCX.Common.Tests;

// The two rules the original's request layer has for GETs (src/services/request.js), which the
// client keeps too: a 403 or 404 is not asked again for 15 minutes, and a GET already under way
// is shared with whoever asks for the same URL.
public class VrchatRequestRulesTests
{
    [Test]
    public async Task A_404_is_not_asked_again_for_fifteen_minutes_and_still_answers_nothing()
    {
        var time = new ManualClock();
        var backend = new Backend(_ => Status(HttpStatusCode.NotFound));
        using var client = VrchatApiClient.Create(inner: backend, rates: VrchatRatePolicy.None, time: time);

        await Assert.That(await client.GetUserAsync("usr_gone")).IsNull();
        time.Advance(VrchatApiClient.FailedGetQuiet - TimeSpan.FromSeconds(1));
        await Assert.That(await client.GetUserAsync("usr_gone")).IsNull();
        await Assert.That(backend.Calls).IsEqualTo(1);

        // Another user is another path.
        await client.GetUserAsync("usr_other");
        await Assert.That(backend.Calls).IsEqualTo(2);

        time.Advance(TimeSpan.FromSeconds(2));
        await client.GetUserAsync("usr_gone");
        await Assert.That(backend.Calls).IsEqualTo(3);
    }

    [Test]
    public async Task A_403_fails_the_same_way_without_asking_and_whatever_the_query()
    {
        var time = new ManualClock();
        var backend = new Backend(_ => Status(HttpStatusCode.Forbidden));
        using var client = VrchatApiClient.Create(inner: backend, rates: VrchatRatePolicy.None, time: time);

        var first = await Assert.ThrowsAsync<VrchatApiException>(async () => await client.GetMutualFriendsAsync("usr_private"));
        // The original keys the refusal by the path without its parameters.
        var second = await Assert.ThrowsAsync<VrchatApiException>(async () => await client.GetMutualFriendsAsync("usr_private", offset: 100));

        await Assert.That(first!.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(second!.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(backend.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task Other_failures_are_asked_again()
    {
        var backend = new Backend(_ => Status(HttpStatusCode.BadGateway));
        using var client = VrchatApiClient.Create(inner: backend, rates: VrchatRatePolicy.None);

        for (var i = 0; i < 2; i++) await Assert.ThrowsAsync<VrchatApiException>(async () => await client.GetUserAsync("usr_a"));

        await Assert.That(backend.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task The_same_get_under_way_is_one_request_and_another_url_is_its_own()
    {
        var release = new TaskCompletionSource();
        var backend = new Backend(_ => User(), release.Task);
        using var client = VrchatApiClient.Create(inner: backend, rates: VrchatRatePolicy.None);

        var first = client.GetUserAsync("usr_a");
        var second = client.GetUserAsync("usr_a");
        var other = client.GetUserAsync("usr_b");
        release.SetResult();
        await Task.WhenAll(first, second, other);

        await Assert.That(backend.Calls).IsEqualTo(2);
        await Assert.That((await second)!.Id).IsEqualTo("usr_x");

        // Once it has answered, the next ask is a new request.
        await client.GetUserAsync("usr_a");
        await Assert.That(backend.Calls).IsEqualTo(3);
    }

    [Test]
    public async Task One_asker_giving_up_does_not_take_the_request_from_the_other()
    {
        var release = new TaskCompletionSource();
        var backend = new Backend(_ => User(), release.Task);
        using var client = VrchatApiClient.Create(inner: backend, rates: VrchatRatePolicy.None);
        using var gaveUp = new CancellationTokenSource();

        var first = client.GetUserAsync("usr_a", gaveUp.Token);
        var second = client.GetUserAsync("usr_a");
        gaveUp.Cancel();
        release.SetResult();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await first);
        await Assert.That(await second).IsNotNull();
        await Assert.That(backend.Calls).IsEqualTo(1);
    }

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status)
    {
        Content = new StringContent("{\"error\":{\"message\":\"" + status + "\",\"status_code\":" + (int)status + "}}", Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage User() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"id":"usr_x","displayName":"x"}""", Encoding.UTF8, "application/json"),
    };

    /// <summary>Answers every request as told, once <paramref name="gate"/> has opened, and counts them.</summary>
    private sealed class Backend(Func<HttpRequestMessage, HttpResponseMessage> answer, Task? gate = null) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (gate is not null) await gate;
            return answer(request);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
