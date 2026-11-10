using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services.Sessions;
using VRCX.Vrchat;

namespace VRCX.Common.Services.Ingestion;

/// <summary>What VRChat's file record says about an avatar image: whose avatar, and its name.</summary>
public sealed record AvatarImageInfo(string OwnerId, string AvatarName, DateTimeOffset? FileCreatedAt);

/// <summary>
/// Turns a profile-image URL into the avatar behind it, the way VRCX does: GET file/{file_id},
/// skip files tagged as icons (custom user icons are not avatars), and read the avatar's name
/// out of the file name ("Avatar - Name - Image - ..."). Cached per file id for the process.
/// </summary>
public interface IAvatarImageResolver
{
    /// <summary>Null when the URL has no file id, no account can ask VRChat, or the file is not an avatar image.</summary>
    Task<AvatarImageInfo?> ResolveAsync(string imageUrl, CancellationToken ct);
}

/// <remarks>The session manager is resolved per call: it depends on the store, which depends on this.</remarks>
public sealed partial class AvatarImageResolver(IServiceProvider services, ILogger<AvatarImageResolver> logger) : IAvatarImageResolver
{
    private static readonly AvatarImageInfo NotAnAvatar = new("", "-", null);
    private readonly ConcurrentDictionary<string, AvatarImageInfo> _cache = new();

    public async Task<AvatarImageInfo?> ResolveAsync(string imageUrl, CancellationToken ct)
    {
        var fileId = VrchatApiClient.ExtractFileId(imageUrl);
        if (fileId is null) return null;
        if (_cache.TryGetValue(fileId, out var cached)) return cached.OwnerId.Length > 0 ? cached : null;

        var sessions = services.GetRequiredService<IAccountSessionManager>();
        var accountId = sessions.ActiveAccountIds.FirstOrDefault();
        if (accountId is null || sessions.Get(accountId) is not { } client) return null;

        try
        {
            var file = await client.GetFileAsync(fileId, ct);
            if (file is null || file.Tags.Contains("icon") || string.IsNullOrEmpty(file.OwnerId))
            {
                _cache[fileId] = NotAnAvatar;
                return null;
            }
            var match = AvatarName().Match(file.Name);
            var info = new AvatarImageInfo(file.OwnerId, match.Success ? VrchatText.ReplaceBioSymbols(match.Groups[1].Value) : "", file.Versions.FirstOrDefault()?.CreatedAt);
            _cache[fileId] = info;
            return info;
        }
        catch (Exception ex) when (ex is VrchatApiException or HttpRequestException)
        {
            logger.LogDebug(ex, "Could not resolve avatar image {FileId}", fileId);
            return null;
        }
    }

    [GeneratedRegex("Avatar - (.*) - Image -", RegexOptions.IgnoreCase)]
    private static partial Regex AvatarName();
}
