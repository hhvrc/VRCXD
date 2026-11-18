using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VRCX.Common.Options;
using VRCX.Common.Services.Images;
using VRCX.Common.VrcxDb;

namespace VRCX.API.IntegrationTests.Tests;

// Images on disk, with the URL mapping in Postgres and VRChat replaced.
public sealed class ImageCacheTests
{
    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private sealed class CountingFetcher : IImageFetcher
    {
        public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

        public int Calls;

        public Task<DownloadedImage?> FetchAsync(Uri url, CancellationToken ct)
        {
            Calls++;
            if (url.AbsolutePath.Contains("missing")) return Task.FromResult<DownloadedImage?>(null);
            var bytes = url.AbsolutePath.EndsWith(".svg") ? "<svg onload=\"alert(1)\"/>"u8.ToArray() : Png;
            return Task.FromResult<DownloadedImage?>(new DownloadedImage(new MemoryStream(bytes)));
        }
    }

    private static Task<VRCX.Testing.VrcxScenario> WithFetcherAsync(CountingFetcher fetcher) =>
        VRCX.Testing.VrcxScenario.StartAsync(new() { ConfigureServices = s => s.Replace(ServiceDescriptor.Singleton<IImageFetcher>(fetcher)) });

    [Test]
    public async Task An_image_is_fetched_once_and_then_comes_from_disk()
    {
        var fetcher = new CountingFetcher();
        await using var host = await WithFetcherAsync(fetcher);
        using var client = host.CreateClient(authenticated: false);
        var first = "https://files.vrchat.cloud/one.png";
        var second = "https://api.vrchat.cloud/api/1/file/file_same/1/file";
        string Path(string url, string token = IsolatedHost.ApiToken) => $"/1/images?url={Uri.EscapeDataString(url)}&token={token}";

        var response = await client.GetAsync(Path(first));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("image/png");
        await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo(CountingFetcher.Png);

        await client.GetAsync(Path(first));
        await Assert.That(fetcher.Calls).IsEqualTo(1);

        // Same bytes under another URL: one file, two rows, and the row holds no image data.
        await client.GetAsync(Path(second));
        await using var db = await host.Services.GetRequiredService<IDbContextFactory<VrcxContext>>().CreateDbContextAsync();
        var rows = await db.CachedImages.Where(i => i.Url == first || i.Url == second).ToListAsync();
        await Assert.That(rows.Select(r => r.Sha256).Distinct().Count()).IsEqualTo(1);

        var dir = host.Services.GetRequiredService<ImageOptions>().Dir;
        var stored = System.IO.Path.Combine(dir, rows[0].Sha256[..2], rows[0].Sha256);
        await Assert.That(File.Exists(stored)).IsTrue();

        // A lost file is only a cache miss.
        File.Delete(stored);
        await Assert.That((await client.GetAsync(Path(first))).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(fetcher.Calls).IsEqualTo(3);

        await Assert.That((await client.GetAsync(Path(first, "wrong-token"))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_relative_cache_directory_still_gives_a_path_that_can_be_served()
    {
        var dir = "vrcx-relative-test-images-" + Guid.NewGuid().ToString("N");
        try
        {
            var cache = new ImageCache(
                Factory.Services.GetRequiredService<IDbContextFactory<VrcxContext>>(), new CountingFetcher(),
                new ImageOptions { Dir = dir }, Microsoft.Extensions.Logging.Abstractions.NullLogger<ImageCache>.Instance);

            var image = await cache.GetAsync("https://files.vrchat.cloud/relative.png", CancellationToken.None);

            await Assert.That(System.IO.Path.IsPathRooted(image!.Path)).IsTrue();
            await Assert.That(File.Exists(image.Path)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task Only_vrchat_https_urls_are_fetched_and_failures_send_the_browser_to_vrchat()
    {
        var fetcher = new CountingFetcher();
        await using var host = await WithFetcherAsync(fetcher);
        using var client = host.CreateClient(authenticated: false);
        client.DefaultRequestHeaders.Add(VRCX.Common.Constants.AuthConstants.ApiTokenHeaderName, IsolatedHost.ApiToken);

        foreach (var bad in new[]
        {
            "http://files.vrchat.cloud/a.png", "https://evil.example/a.png", "https://vrchat.cloud.evil.example/a.png",
            "https://notvrchat.cloud/a.png", "https://user@files.vrchat.cloud/a.png", "https://files.vrchat.cloud:8443/a.png", "http://10.0.0.130/a.png",
        })
        {
            var response = await client.GetAsync($"/1/images?url={Uri.EscapeDataString(bad)}");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        await Assert.That(fetcher.Calls).IsEqualTo(0);

        const string missing = "https://files.vrchat.cloud/missing.png";
        var fallback = await client.GetAsync($"/1/images?url={Uri.EscapeDataString(missing)}");
        await Assert.That(fallback.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(fallback.Headers.Location!.ToString()).IsEqualTo(missing);

        // Only what the bytes show to be a plain image is stored and served from this origin.
        var script = await client.GetAsync($"/1/images?url={Uri.EscapeDataString("https://files.vrchat.cloud/script.svg")}");
        await Assert.That(script.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
    }

    // A broken image used to be fetched again on every view of every card showing it.
    [Test]
    public async Task An_image_that_could_not_be_had_is_not_fetched_again_for_a_while()
    {
        var fetcher = new CountingFetcher();
        await using var host = await WithFetcherAsync(fetcher);
        var cache = host.Services.GetRequiredService<ImageCache>();
        const string missing = "https://files.vrchat.cloud/missing-for-now.png";

        await Assert.That(await cache.GetAsync(missing, CancellationToken.None)).IsNull();
        await Assert.That(await cache.GetAsync(missing, CancellationToken.None)).IsNull();
        await Assert.That(fetcher.Calls).IsEqualTo(1);

        host.Time.Advance(ImageCache.FailureBackoff + TimeSpan.FromSeconds(1));
        await Assert.That(await cache.GetAsync(missing, CancellationToken.None)).IsNull();
        await Assert.That(fetcher.Calls).IsEqualTo(2);
    }

    private sealed class BlockingFetcher : IImageFetcher
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _running;
        public int Started;
        public int MostAtOnce;

        public async Task<DownloadedImage?> FetchAsync(Uri url, CancellationToken ct)
        {
            Interlocked.Increment(ref Started);
            var now = Interlocked.Increment(ref _running);
            lock (this) MostAtOnce = Math.Max(MostAtOnce, now);
            await Release.Task;
            Interlocked.Decrement(ref _running);
            return new DownloadedImage(new MemoryStream(CountingFetcher.Png));
        }
    }

    // A page of cards used to start every download at once, outside the VRChat API's rate limit.
    [Test]
    public async Task Downloads_run_a_few_at_a_time_and_one_image_is_downloaded_once()
    {
        var fetcher = new BlockingFetcher();
        await using var host = await VRCX.Testing.VrcxScenario.StartAsync(new() { ConfigureServices = s => s.Replace(ServiceDescriptor.Singleton<IImageFetcher>(fetcher)) });
        var cache = host.Services.GetRequiredService<ImageCache>();
        var urls = Enumerable.Range(0, 10).Select(i => $"https://files.vrchat.cloud/card{i}.png").ToList();

        var loads = urls.Concat(urls).Select(u => cache.GetAsync(u, CancellationToken.None)).ToList();
        for (var i = 0; i < 100 && Volatile.Read(ref fetcher.Started) < ImageCache.MaxConcurrentDownloads; i++) await Task.Delay(20);
        await Task.Delay(200);
        await Assert.That(Volatile.Read(ref fetcher.Started)).IsEqualTo(ImageCache.MaxConcurrentDownloads);

        fetcher.Release.SetResult();
        var images = await Task.WhenAll(loads);

        await Assert.That(images.All(i => i is not null)).IsTrue();
        await Assert.That(fetcher.Started).IsEqualTo(urls.Count);
        await Assert.That(fetcher.MostAtOnce).IsEqualTo(ImageCache.MaxConcurrentDownloads);
    }
}
