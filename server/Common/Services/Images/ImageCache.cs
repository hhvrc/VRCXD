using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VRCX.Common.Options;
using VRCX.Common.VrcxDb;

namespace VRCX.Common.Services.Images;

public sealed record CachedImageFile(string Path, string ContentType, string Sha256);

public sealed record DownloadedImage(Stream Content);

public interface IImageFetcher
{
    /// <summary>Null when the file could not be had: not found, or it left VRChat's hosts.</summary>
    Task<DownloadedImage?> FetchAsync(Uri url, CancellationToken ct);
}

/// <summary>
/// VRChat's images on disk, named by their content so identical images share one file. Postgres
/// only knows which URL is which file. Nothing here is the only copy: a lost file is fetched again.
/// </summary>
public sealed class ImageCache(IDbContextFactory<VrcxContext> dbFactory, IImageFetcher fetcher, ImageOptions options, ILogger<ImageCache> logger)
{
    public const long MaxBytes = 32 * 1024 * 1024;

    // Files are served by path, which has to be absolute whatever the configured directory looks like.
    private readonly string _dir = Path.GetFullPath(options.Dir);

    private readonly ConcurrentDictionary<string, Lazy<Task<CachedImageFile?>>> _inFlight = new();

    /// <summary>Only VRChat's own https hosts: this fetches on request, so it must not reach anything else.</summary>
    public static bool IsAllowed(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo)
        && (IsHostOrSubdomain(url.Host, "vrchat.cloud") || IsHostOrSubdomain(url.Host, "vrchat.com"));

    /// <summary>
    /// The type from the file itself. VRChat serves many images as application/octet-stream, and
    /// anything that is not a plain image format, such as SVG or HTML, must not be served from this origin.
    /// </summary>
    public static string? SniffContentType(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
        if (head.StartsWith<byte>([0xFF, 0xD8, 0xFF])) return "image/jpeg";
        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8)) return "image/gif";
        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    private static bool IsHostOrSubdomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    public async Task<CachedImageFile?> GetAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsAllowed(uri)) return null;

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var known = await db.CachedImages.AsNoTracking().FirstOrDefaultAsync(i => i.Url == url, ct);
            if (known is not null)
            {
                var path = PathFor(known.Sha256);
                if (File.Exists(path)) return new CachedImageFile(path, known.ContentType, known.Sha256);
            }
        }

        // Several cards showing one image at once make one download, which outlives any one request.
        var download = _inFlight.GetOrAdd(url, _ => new Lazy<Task<CachedImageFile?>>(() => DownloadAsync(url, uri)));
        try
        {
            return await download.Value.WaitAsync(ct);
        }
        finally
        {
            if (download.IsValueCreated && download.Value.IsCompleted) _inFlight.TryRemove(url, out _);
        }
    }

    private async Task<CachedImageFile?> DownloadAsync(string url, Uri uri)
    {
        var temp = Path.Combine(_dir, "tmp", Guid.NewGuid().ToString("N"));
        try
        {
            var image = await fetcher.FetchAsync(uri, CancellationToken.None);
            if (image is null) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            string sha256;
            string contentType;
            long size;
            await using (image.Content)
            await using (var file = File.Create(temp))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                var read = await image.Content.ReadAtLeastAsync(buffer, 12, throwOnEndOfStream: false);
                if (SniffContentType(buffer.AsSpan(0, read)) is not { } sniffed)
                {
                    logger.LogInformation("Not caching {Url}: not an image format served from here", url);
                    return null;
                }
                contentType = sniffed;
                size = 0;
                while (read > 0)
                {
                    size += read;
                    if (size > MaxBytes)
                    {
                        logger.LogWarning("Not caching {Url}: larger than {Max} bytes", url, MaxBytes);
                        return null;
                    }
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    read = await image.Content.ReadAsync(buffer);
                }
                sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            var path = PathFor(sha256);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Move(temp, path, overwrite: true);

            await using var db = await dbFactory.CreateDbContextAsync();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO cached_images (url, sha256, content_type, size_bytes, fetched_at)
                VALUES ({url}, {sha256}, {contentType}, {size}, {now})
                ON CONFLICT (url) DO UPDATE SET sha256 = excluded.sha256, content_type = excluded.content_type,
                    size_bytes = excluded.size_bytes, fetched_at = excluded.fetched_at
                """);
            return new CachedImageFile(path, contentType, sha256);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not cache image {Url}", url);
            return null;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private string PathFor(string sha256) => Path.Combine(_dir, sha256[..2], sha256);
}

/// <summary>Follows redirects by hand so that every hop has to be one of VRChat's own hosts.</summary>
public sealed class VrchatImageFetcher : IImageFetcher, IDisposable
{
    private const int MaxRedirects = 5;

    private readonly HttpClient _http = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public VrchatImageFetcher() => _http.DefaultRequestHeaders.UserAgent.ParseAdd(Vrchat.VrchatApiClient.UserAgent);

    public async Task<DownloadedImage?> FetchAsync(Uri url, CancellationToken ct)
    {
        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!ImageCache.IsAllowed(current)) return null;
            var response = await _http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                current = new Uri(current, location);
                response.Dispose();
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                return null;
            }
            return new DownloadedImage(await response.Content.ReadAsStreamAsync(ct));
        }
        return null;
    }

    public void Dispose() => _http.Dispose();
}
