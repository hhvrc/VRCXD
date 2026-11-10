using System.Collections.Concurrent;

namespace VRCX.Common.Services.Sync;

/// <summary>When each account's own avatars were last fetched from VRChat, so My Avatars does not page through them on every visit.</summary>
public sealed class OwnAvatarsFetchLog
{
    public ConcurrentDictionary<string, DateTimeOffset> FetchedAt { get; } = new();
}
