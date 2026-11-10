using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services.Live;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream PR 1450: the YouTube API dialog's key test, a lookup of one known video with the key.
public sealed class YouTubeKeyTestTests
{
    private sealed class YouTubeStub : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests) Requests.Add(url);
            if (!url.Contains("key=good", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":{"code":400}}""") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"items":[{"snippet":{"title":"Never Gonna Give You Up"},"contentDetails":{"duration":"PT3M33S"}}]}"""),
            });
        }
    }

    [Test]
    public async Task A_key_is_valid_only_when_youtube_answers_with_the_video()
    {
        var stub = new YouTubeStub();
        await using var app = await VRCX.Testing.VrcxScenario.StartAsync(new()
        {
            ConfigureServices = s => s.AddHttpClient(YouTubeLookup.ClientName).ConfigurePrimaryHttpMessageHandler(() => stub),
        });
        await app.LoginAsync();

        async Task<bool> Test(string key)
        {
            using var response = await app.Api.PostAsJsonAsync(app.AccountPath("settings/youtube-key-test"), new { apiKey = key });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valid").GetBoolean();
        }

        await Assert.That(await Test("good")).IsTrue();
        await Assert.That(await Test("bad")).IsFalse();
        await Assert.That(await Test("  ")).IsFalse();
        List<string> requests;
        lock (stub.Requests) requests = [.. stub.Requests];
        // The blank key is not sent; both others asked for the same video.
        await Assert.That(requests.Count).IsEqualTo(2);
        await Assert.That(requests.All(r => r.Contains("id=dQw4w9WgXcQ", StringComparison.Ordinal))).IsTrue();
    }
}
