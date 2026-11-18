using System.Collections.Concurrent;
using VRCX.Vrchat.Models;

namespace VRCX.Common.Services.Sessions;

/// <summary>
/// The last auth/user VRChat sent for each account, so the sidebar's own row (GET /me, read after
/// every visit, desktop and account event) does not ask VRChat each time. The friend sync reads
/// auth/user every 5 minutes and every login, edit and status change returns it, and all of those
/// pass through <c>VrchatStore.UpsertCurrentUserAsync</c>,
/// which keeps this current; the pipeline's user-update and user-location change it in between.
/// The original held the current user in memory the same way and refreshed it on those events.
/// </summary>
public sealed class CurrentUserCache(TimeProvider time)
{
    /// <summary>How old a copy may be and still answer GET /me without a call to VRChat.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private sealed record Entry(CurrentUser User, DateTimeOffset FetchedAt);

    private readonly ConcurrentDictionary<string, Entry> _byAccount = new(StringComparer.Ordinal);

    /// <summary>A whole auth/user, as VRChat just sent it.</summary>
    public void Set(CurrentUser user)
    {
        // The 2FA step of a login answers with nothing but the methods: not the account.
        if (user.RequiresTwoFactorAuth is { Count: > 0 } || string.IsNullOrEmpty(user.Id)) return;
        _byAccount[user.Id] = new Entry(user, time.GetUtcNow());
    }

    /// <summary>
    /// The pipeline's user-update: part of the user. The fields it carries replace the stored ones;
    /// the rest (status history, options, friend lists) stay, and so does the copy's age, so those
    /// are still read again from VRChat once it is older than <see cref="MaxAge"/>.
    /// </summary>
    public void ApplyUpdate(CurrentUser update)
    {
        if (!_byAccount.TryGetValue(update.Id, out var entry)) return;
        var u = entry.User;
        _byAccount.TryUpdate(update.Id, entry with
        {
            User = u with
            {
                DisplayName = update.DisplayName,
                Status = update.Status,
                StatusDescription = update.StatusDescription,
                Bio = update.Bio,
                BioLinks = update.BioLinks,
                Pronouns = update.Pronouns,
                Tags = update.Tags.Count > 0 ? update.Tags : u.Tags,
                CurrentAvatar = update.CurrentAvatar ?? u.CurrentAvatar,
                FallbackAvatar = update.FallbackAvatar ?? u.FallbackAvatar,
                CurrentAvatarImageUrl = update.CurrentAvatarImageUrl.Length > 0 ? update.CurrentAvatarImageUrl : u.CurrentAvatarImageUrl,
                CurrentAvatarThumbnailImageUrl = update.CurrentAvatarThumbnailImageUrl.Length > 0 ? update.CurrentAvatarThumbnailImageUrl : u.CurrentAvatarThumbnailImageUrl,
                ProfilePicOverride = update.ProfilePicOverride ?? u.ProfilePicOverride,
                ProfilePicOverrideThumbnail = update.ProfilePicOverrideThumbnail ?? u.ProfilePicOverrideThumbnail,
                UserIcon = update.UserIcon ?? u.UserIcon,
                IconUrl = update.IconUrl ?? u.IconUrl,
                IconFrame = update.IconFrame ?? u.IconFrame,
            },
        }, entry);
    }

    /// <summary>The pipeline's user-location: where the account is now.</summary>
    public void ApplyLocation(string accountId, string? location, string? travelingToLocation)
    {
        if (!_byAccount.TryGetValue(accountId, out var entry)) return;
        _byAccount.TryUpdate(accountId, entry with { User = entry.User with { Location = location, TravelingToLocation = travelingToLocation } }, entry);
    }

    /// <summary>The stored copy when it is younger than <paramref name="maxAge"/> (default <see cref="MaxAge"/>).</summary>
    public CurrentUser? Get(string accountId, TimeSpan? maxAge = null) =>
        _byAccount.TryGetValue(accountId, out var entry) && time.GetUtcNow() - entry.FetchedAt < (maxAge ?? MaxAge) ? entry.User : null;

    /// <summary>Something changed the account on VRChat without sending it back: the next read asks VRChat.</summary>
    public void Invalidate(string accountId) => _byAccount.TryRemove(accountId, out _);

    /// <summary>
    /// <see cref="Invalidate"/>, unless auth/user was stored at or after <paramref name="since"/>: a
    /// write that answered with the account (a status change, the friend refresh) already put it here.
    /// </summary>
    public void InvalidateUnlessSetSince(string accountId, DateTimeOffset since)
    {
        if (_byAccount.TryGetValue(accountId, out var entry) && entry.FetchedAt < since) _byAccount.TryRemove(KeyValuePair.Create(accountId, entry));
    }
}
