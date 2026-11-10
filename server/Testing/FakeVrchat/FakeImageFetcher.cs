using System.Collections.Concurrent;
using VRCX.Common.Services.Images;

namespace VRCX.Testing.FakeVrchat;

/// <summary>
/// The image cache's downloader, faked: every VRChat image is the same 1x1 PNG, so nothing a test
/// or a Playwright page renders ever leaves the machine. Records which URLs were asked for.
/// Replace it through <see cref="ScenarioOptions.ConfigureServices"/> for other answers.
/// </summary>
public sealed class FakeImageFetcher : IImageFetcher
{
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly ConcurrentQueue<Uri> _fetched = new();

    public IReadOnlyCollection<Uri> Fetched => _fetched.ToArray();

    public Task<DownloadedImage?> FetchAsync(Uri url, CancellationToken ct)
    {
        _fetched.Enqueue(url);
        return Task.FromResult<DownloadedImage?>(new DownloadedImage(new MemoryStream(Pixel, writable: false)));
    }
}
