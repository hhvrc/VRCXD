using Microsoft.EntityFrameworkCore;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;
using Vrc = VRCX.Vrchat.Models;
// Entity names that also exist as API models resolve to the entities here; the API side is Vrc.*.
using User = VRCX.Common.VrcxDb.User;
using World = VRCX.Common.VrcxDb.World;
using Avatar = VRCX.Common.VrcxDb.Avatar;
using Instance = VRCX.Common.VrcxDb.Instance;
using Group = VRCX.Common.VrcxDb.Group;
using GroupRole = VRCX.Common.VrcxDb.GroupRole;
using GroupMember = VRCX.Common.VrcxDb.GroupMember;
using Notification = VRCX.Common.VrcxDb.Notification;
using Favorite = VRCX.Common.VrcxDb.Favorite;
using FavoriteGroup = VRCX.Common.VrcxDb.FavoriteGroup;
using PlayerModeration = VRCX.Common.VrcxDb.PlayerModeration;
using UserNote = VRCX.Common.VrcxDb.UserNote;

namespace VRCX.Common.Services.Ingestion;

/// <summary>
/// The one place VRChat API objects become rows. Every method takes the
/// caller's DbContext and does not save: the caller decides the unit of
/// work. Upserts compare against what is stored and write *Change rows only
/// when a value we already knew actually changed - a first sighting is never
/// a "change", and an endpoint that does not carry a field never clears it.
/// </summary>
public sealed class VrchatStore(ILogger<VrchatStore> logger, IPresenceCache presenceCache, IAvatarImageResolver? avatarImages = null)
{
    // ---- users ------------------------------------------------------------

    public Task<User> UpsertUserAsync(VrcxContext db, Vrc.User api, DateTimeOffset now, CancellationToken ct) =>
        ApplyUserAsync(db, api.Id, now, new UserFields
        {
            DisplayName = api.DisplayName,
            Bio = api.Bio,
            BioLinks = api.BioLinks.ToArray(),
            Pronouns = api.Pronouns,
            Status = api.Status,
            StatusDescription = api.StatusDescription,
            State = api.State,
            Tags = api.Tags.ToArray(),
            DeveloperType = api.DeveloperType,
            CurrentAvatarImageUrl = api.CurrentAvatarImageUrl,
            CurrentAvatarThumbnailImageUrl = api.CurrentAvatarThumbnailImageUrl,
            CurrentAvatarTags = api.CurrentAvatarTags.ToArray(),
            ProfilePicOverride = api.ProfilePicOverride,
            UserIcon = api.UserIcon,
            IconUrl = api.IconUrl,
            BannerUrl = api.BannerUrl,
            BannerType = api.BannerType,
            IconFrame = api.IconFrame,
            NameplateEffect = api.NameplateEffect,
            ProfileEffect = api.ProfileEffect,
            DiscordId = api.DiscordId,
            Platform = api.Platform,
            LastPlatform = api.LastPlatform,
            LastLogin = api.LastLogin,
            LastActivity = api.LastActivity,
            LastMobile = api.LastMobile,
            DateJoined = api.DateJoined,
            AgeVerificationStatus = api.AgeVerificationStatus,
            AgeVerified = api.AgeVerified,
            AllowAvatarCopying = api.AllowAvatarCopying,
            IsFullFetch = true,
        }, ct);

    public Task<User> UpsertCurrentUserAsync(VrcxContext db, CurrentUser api, CancellationToken ct) =>
        UpsertCurrentUserAsync(db, api, DateTimeOffset.UtcNow, ct);

    public Task<User> UpsertCurrentUserAsync(VrcxContext db, CurrentUser api, DateTimeOffset now, CancellationToken ct) =>
        ApplyUserAsync(db, api.Id, now, new UserFields
        {
            DisplayName = api.DisplayName,
            Bio = api.Bio,
            BioLinks = api.BioLinks.ToArray(),
            Pronouns = api.Pronouns,
            Status = api.Status,
            StatusDescription = api.StatusDescription,
            // auth/user always says offline for state; presence is told separately.
            Tags = api.Tags.ToArray(),
            DeveloperType = api.DeveloperType,
            CurrentAvatarImageUrl = api.CurrentAvatarImageUrl,
            CurrentAvatarThumbnailImageUrl = api.CurrentAvatarThumbnailImageUrl,
            CurrentAvatarTags = api.CurrentAvatarTags.ToArray(),
            ProfilePicOverride = api.ProfilePicOverride,
            UserIcon = api.UserIcon,
            IconUrl = api.IconUrl,
            BannerUrl = api.BannerUrl,
            BannerType = api.BannerType,
            IconFrame = api.IconFrame,
            NameplateEffect = api.NameplateEffect,
            ProfileEffect = api.ProfileEffect,
            DiscordId = api.DiscordId,
            LastPlatform = api.LastPlatform,
            LastLogin = api.LastLogin,
            LastActivity = api.LastActivity,
            LastMobile = api.LastMobile,
            DateJoined = api.DateJoined,
            AgeVerificationStatus = api.AgeVerificationStatus,
            AgeVerified = api.AgeVerified,
            AllowAvatarCopying = api.AllowAvatarCopying,
            IsFullFetch = true,
        }, ct);

    /// <summary>
    /// Stores what profile/{id} says, the original's updateUserProfile: the first read sets the
    /// bio quietly, a later difference is a bio change for the feed.
    /// </summary>
    public async Task ApplyProfileAsync(VrcxContext db, string userId, PublicProfile profile, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return;
        user.ProfileFetchedAt = now;
        if (profile.Badges is not null) user.Badges = profile.Badges.ToList();
        if (profile.Bio is null) return;

        var links = profile.BioLinks?.ToArray() ?? [];
        if (user.Bio is not null && (user.Bio != profile.Bio || !links.SequenceEqual(user.BioLinks)))
        {
            db.UserBioChanges.Add(new UserBioChange
            {
                UserId = userId, ChangedAt = now,
                Bio = profile.Bio, BioLinks = links,
                PreviousBio = user.Bio, PreviousBioLinks = user.BioLinks,
            });
        }
        user.Bio = profile.Bio;
        user.BioLinks = links;
    }

    public Task<User> UpsertFriendAsync(VrcxContext db, LimitedUserFriend api, DateTimeOffset now, CancellationToken ct) =>
        ApplyUserAsync(db, api.Id, now, new UserFields
        {
            DisplayName = api.DisplayName,
            Bio = api.Bio,
            BioLinks = api.BioLinks?.ToArray(),
            Status = api.Status,
            StatusDescription = api.StatusDescription,
            Tags = api.Tags.ToArray(),
            DeveloperType = api.DeveloperType,
            CurrentAvatarImageUrl = api.CurrentAvatarImageUrl,
            CurrentAvatarThumbnailImageUrl = api.CurrentAvatarThumbnailImageUrl,
            CurrentAvatarTags = api.CurrentAvatarTags?.ToArray(),
            ProfilePicOverride = api.ProfilePicOverride,
            UserIcon = api.UserIcon,
            IconUrl = api.IconUrl,
            BannerUrl = api.BannerUrl,
            BannerType = api.BannerType,
            IconFrame = api.IconFrame,
            NameplateEffect = api.NameplateEffect,
            ProfileEffect = api.ProfileEffect,
            Platform = api.Platform,
            LastPlatform = api.LastPlatform,
            LastLogin = api.LastLogin,
            LastActivity = api.LastActivity,
            LastMobile = api.LastMobile,
        }, ct);

    public Task<User> UpsertSearchResultAsync(VrcxContext db, LimitedUserSearch api, DateTimeOffset now, CancellationToken ct) =>
        ApplyUserAsync(db, api.Id, now, new UserFields
        {
            DisplayName = api.DisplayName,
            Bio = api.Bio,
            BioLinks = api.BioLinks?.ToArray(),
            Pronouns = api.Pronouns,
            Status = api.Status,
            StatusDescription = api.StatusDescription,
            Tags = api.Tags.ToArray(),
            DeveloperType = api.DeveloperType,
            CurrentAvatarImageUrl = api.CurrentAvatarImageUrl,
            CurrentAvatarThumbnailImageUrl = api.CurrentAvatarThumbnailImageUrl,
            ProfilePicOverride = api.ProfilePicOverride,
            UserIcon = api.UserIcon,
            IconUrl = api.IconUrl,
            BannerUrl = api.BannerUrl,
            BannerType = api.BannerType,
            IconFrame = api.IconFrame,
            NameplateEffect = api.NameplateEffect,
            ProfileEffect = api.ProfileEffect,
            LastPlatform = api.LastPlatform,
        }, ct);

    /// <summary>A row for a user we only know by reference (author id, log line). Never downgrades a real row.</summary>
    public async Task<User> EnsureUserAsync(VrcxContext db, string userId, string? displayName, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is not null)
        {
            if (user.IsStub && !string.IsNullOrEmpty(displayName) && user.DisplayName != displayName)
            {
                user.DisplayName = displayName;
                user.UpdatedAt = now;
            }
            return user;
        }
        user = new User
        {
            Id = userId,
            DisplayName = string.IsNullOrEmpty(displayName) ? userId : displayName,
            IsStub = true,
            FirstSeenAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        return user;
    }

    private sealed record UserFields
    {
        public required string DisplayName { get; init; }
        public string? Bio { get; init; }
        public string[]? BioLinks { get; init; }
        public string? Pronouns { get; init; }
        public string? Status { get; init; }
        public string? StatusDescription { get; init; }
        public string? State { get; init; }
        public string[]? Tags { get; init; }
        public string? DeveloperType { get; init; }
        public string? CurrentAvatarImageUrl { get; init; }
        public string? CurrentAvatarThumbnailImageUrl { get; init; }
        public string[]? CurrentAvatarTags { get; init; }
        public string? ProfilePicOverride { get; init; }
        public string? UserIcon { get; init; }
        public string? IconUrl { get; init; }
        public string? BannerUrl { get; init; }
        public string? BannerType { get; init; }
        public string? IconFrame { get; init; }
        public string? NameplateEffect { get; init; }
        public string? ProfileEffect { get; init; }
        public string? DiscordId { get; init; }
        public string? Platform { get; init; }
        public string? LastPlatform { get; init; }
        public DateTimeOffset? LastLogin { get; init; }
        public DateTimeOffset? LastActivity { get; init; }
        public DateTimeOffset? LastMobile { get; init; }
        public DateOnly? DateJoined { get; init; }
        public string? AgeVerificationStatus { get; init; }
        public bool? AgeVerified { get; init; }
        public bool? AllowAvatarCopying { get; init; }
        public bool IsFullFetch { get; init; }
    }

    private async Task<User> ApplyUserAsync(VrcxContext db, string id, DateTimeOffset now, UserFields f, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([id], ct);
        var isNew = user is null;
        if (user is null)
        {
            user = new User { Id = id, DisplayName = f.DisplayName, FirstSeenAt = now, UpdatedAt = now, IsStub = true };
            db.Users.Add(user);
        }
        // A stub (or brand-new row) has no known previous values, so nothing
        // below counts as a change for it.
        var known = !isNew && !user.IsStub;

        if (user.DisplayName != f.DisplayName)
        {
            if (known)
                db.UserDisplayNameChanges.Add(new UserDisplayNameChange { UserId = id, ChangedAt = now, PreviousDisplayName = user.DisplayName, DisplayName = f.DisplayName });
            user.DisplayName = f.DisplayName;
        }

        if (f.Tags is not null)
        {
            var trust = TrustLevel.FromTags(f.Tags);
            if (known && user.TrustLevel != trust)
                db.UserTrustChanges.Add(new UserTrustChange { UserId = id, ChangedAt = now, PreviousTrustLevel = user.TrustLevel, TrustLevel = trust });
            user.Tags = f.Tags;
            user.TrustLevel = trust;
        }

        if (f.Status is not null)
        {
            // The original's sanitizeUserJson: look-alike symbols back to ASCII and no emojis. A row
            // stored before that is compared clean, so the cleanup alone is no status change.
            var description = VrchatText.StatusDescription(f.StatusDescription);
            // The original ignores status changes to or from "offline": that is presence, not a status choice.
            if (known && f.Status != "offline" && user.Status != "offline" && (user.Status != f.Status || VrchatText.StatusDescription(user.StatusDescription) != description))
            {
                db.UserStatusChanges.Add(new UserStatusChange
                {
                    UserId = id, ChangedAt = now,
                    Status = f.Status, StatusDescription = description,
                    PreviousStatus = user.Status, PreviousStatusDescription = user.StatusDescription,
                });
            }
            user.Status = f.Status;
            user.StatusDescription = description;
        }

        // User payloads carry a blank bio and no links now (the real ones are on profile/{id}), so a
        // blank says nothing. ApplyProfileAsync is what can clear a bio.
        if (!string.IsNullOrEmpty(f.Bio))
        {
            var links = f.BioLinks ?? user.BioLinks;
            // Bio null means "never fetched": the first bio we see is not a change.
            if (known && user.Bio is not null && (user.Bio != f.Bio || !links.SequenceEqual(user.BioLinks)))
            {
                db.UserBioChanges.Add(new UserBioChange
                {
                    UserId = id, ChangedAt = now,
                    Bio = f.Bio, BioLinks = links,
                    PreviousBio = user.Bio, PreviousBioLinks = user.BioLinks,
                });
            }
            user.Bio = f.Bio;
            user.BioLinks = links;
        }
        else if (f.BioLinks is { Length: > 0 })
        {
            user.BioLinks = f.BioLinks;
        }

        // The original's sanitizeUserJson: VRChat's default robot stands in for an avatar it will not
        // show (upstream issue 275), so it does not replace the last real one.
        if (!string.IsNullOrEmpty(f.CurrentAvatarImageUrl) && !f.CurrentAvatarImageUrl.Contains(RobotAvatarFileId, StringComparison.Ordinal))
        {
            user.CurrentAvatarImageUrl = f.CurrentAvatarImageUrl;
            user.CurrentAvatarThumbnailImageUrl = f.CurrentAvatarThumbnailImageUrl;
        }
        // Avatar changes follow the original: the profile image VRChat shows (the banner when it is an
        // avatar banner, else the icon) resolved through GET file/{id}; only images that are avatar
        // images (not custom icons) count, and only when the owner is known.
        if (f.BannerType is not null) user.BannerType = f.BannerType;
        var usesBanner = (f.BannerType ?? user.BannerType) == "avatarBanner";
        var newImage = usesBanner ? f.BannerUrl : f.IconUrl;
        var oldImage = usesBanner ? user.BannerUrl : user.IconUrl;
        if (!string.IsNullOrEmpty(newImage) && known && !string.IsNullOrEmpty(oldImage) && oldImage != newImage && avatarImages is not null)
        {
            var current = await avatarImages.ResolveAsync(newImage, ct);
            if (current is { OwnerId.Length: > 0 })
            {
                var previous = await avatarImages.ResolveAsync(oldImage, ct);
                db.UserAvatarChanges.Add(new UserAvatarChange
                {
                    UserId = id, ChangedAt = now,
                    ImageUrl = newImage, ThumbnailImageUrl = newImage,
                    PreviousImageUrl = previous is { OwnerId.Length: > 0 } ? oldImage : null,
                    PreviousThumbnailImageUrl = previous is { OwnerId.Length: > 0 } ? oldImage : null,
                    AvatarName = current.AvatarName, AvatarOwnerId = current.OwnerId,
                    PreviousAvatarName = previous?.AvatarName, PreviousAvatarOwnerId = previous?.OwnerId,
                    AvatarId = await ResolveAvatarIdByImageAsync(db, newImage, ct),
                });
            }
        }
        // Null means the payload did not say; an empty string is a cosmetic taken off.
        if (f.IconFrame is not null) user.IconFrame = f.IconFrame;
        if (f.NameplateEffect is not null) user.NameplateEffect = f.NameplateEffect;
        if (f.ProfileEffect is not null) user.ProfileEffect = f.ProfileEffect;
        if (f.DiscordId is not null) user.DiscordId = f.DiscordId;
        if (f.IconUrl is not null) user.IconUrl = f.IconUrl;
        if (f.BannerUrl is not null) user.BannerUrl = f.BannerUrl;
        if (f.CurrentAvatarTags is not null) user.CurrentAvatarTags = f.CurrentAvatarTags;

        if (f.Pronouns is not null) user.Pronouns = f.Pronouns;
        if (f.DeveloperType is not null) user.DeveloperType = f.DeveloperType;
        if (f.ProfilePicOverride is not null) user.ProfilePicOverride = f.ProfilePicOverride;
        if (f.UserIcon is not null) user.UserIcon = f.UserIcon;
        if (f.LastPlatform is not null) user.LastPlatform = f.LastPlatform;
        if (f.LastLogin is not null) user.LastLogin = f.LastLogin;
        if (f.LastActivity is not null) user.LastActivity = f.LastActivity;
        if (f.LastMobile is not null) user.LastMobile = f.LastMobile;
        if (f.DateJoined is not null) user.DateJoined = f.DateJoined;
        if (f.AgeVerificationStatus is not null) user.AgeVerificationStatus = f.AgeVerificationStatus;
        if (f.AgeVerified is not null) user.AgeVerified = f.AgeVerified.Value;
        if (f.AllowAvatarCopying is not null) user.AllowAvatarCopying = f.AllowAvatarCopying.Value;
        if (f.IsFullFetch) user.LastFullFetchAt = now;

        if (f.State is not null || f.Platform is not null)
        {
            var before = await GetPresenceAsync(db, user.Id, ct);
            var after = before with { State = f.State is null ? before.State : VrcxEnums.ParseUserState(f.State), Platform = f.Platform ?? before.Platform };
            if (after != before) db.Presence.Change(user.Id, before, after);
        }

        user.IsStub = false;
        user.UpdatedAt = now;
        return user;
    }

    private static async Task<string?> ResolveAvatarIdByImageAsync(VrcxContext db, string imageUrl, CancellationToken ct)
    {
        var local = db.Avatars.Local.FirstOrDefault(a => a.ImageUrl == imageUrl);
        if (local is not null) return local.Id;
        return await db.Avatars.Where(a => a.ImageUrl == imageUrl).Select(a => a.Id).FirstOrDefaultAsync(ct);
    }

    // ---- presence ---------------------------------------------------------

    /// <summary>
    /// Records where a user is now. Kind is online/active/offline/location;
    /// repeated identical reports are ignored, so polling the friend list is
    /// safe. Opens and closes pipeline-sourced instance visits as the
    /// location changes.
    /// </summary>
    /// <summary>Where a user is, including changes this unit of work has not saved yet.</summary>
    public async Task<UserPresence> GetPresenceAsync(VrcxContext db, string userId, CancellationToken ct)
    {
        if (db.Presence.TryGet(userId, out var presence)) return presence;
        presence = await presenceCache.GetAsync(userId, ct);
        db.Presence.Remember(userId, presence);
        return presence;
    }

    public async Task<PresenceEvent?> ApplyPresenceAsync(
        VrcxContext db, User user, PresenceKind kind, string? location, string? travelingToLocation, string? platform,
        DateTimeOffset now, CancellationToken ct)
    {
        var current = await GetPresenceAsync(db, user.Id, ct);
        var (state, newLocation) = kind switch
        {
            PresenceKind.Online => (UserState.Online, location ?? "private"),
            PresenceKind.Active => (UserState.Active, "offline"),
            PresenceKind.Offline => (UserState.Offline, "offline"),
            _ => (UserState.Online, location ?? current.Location ?? "private"),
        };
        var traveling = string.IsNullOrEmpty(travelingToLocation) ? null : travelingToLocation;

        if (current.PresenceChangedAt is not null && current.State == state && current.Location == newLocation && current.TravelingToLocation == traveling)
        {
            if (platform is not null && platform != current.Platform) db.Presence.Change(user.Id, current, current with { Platform = platform });
            return null;
        }

        // "traveling" is not a place: remember where they were and wait for the arrival, like the
        // original does (it logs the move from the pre-travel location once the new one is known).
        if (kind == PresenceKind.Location && newLocation == "traveling")
        {
            db.Presence.Change(user.Id, current, current with
            {
                LocationBeforeTraveling = current.Location != "traveling" ? current.Location : current.LocationBeforeTraveling,
                TravelingSince = current.Location != "traveling" ? now : current.TravelingSince,
                Location = "traveling",
                TravelingToLocation = traveling,
                PresenceChangedAt = now,
            });
            return null;
        }

        var previousState = current.PresenceChangedAt is null ? (UserState?)null : current.State;
        var previousLocation = current.PresenceChangedAt is null ? null : current.Location == "traveling" ? current.LocationBeforeTraveling : current.Location;
        var arrivedAtPrevious = current.LocationSince;
        var leftPreviousAt = current.Location == "traveling" && current.TravelingSince is { } travelingSince ? travelingSince : now;
        TimeSpan? durationAtPrevious = arrivedAtPrevious is { } since && previousLocation is not null && previousLocation != "offline"
            ? leftPreviousAt - since
            : null;
        if (durationAtPrevious < TimeSpan.Zero) durationAtPrevious = TimeSpan.Zero;

        var parsed = VrchatLocation.Parse(newLocation);
        Instance? instance = parsed.IsRealInstance ? await EnsureInstanceAsync(db, parsed, now, ct) : null;

        var presence = new PresenceEvent
        {
            UserId = user.Id,
            At = now,
            Kind = kind,
            Location = newLocation,
            PreviousLocation = previousLocation,
            TravelingToLocation = traveling,
            InstanceLocation = instance?.Location,
            Platform = platform ?? current.Platform,
            PreviousState = previousState,
            DurationAtPrevious = durationAtPrevious,
        };
        db.PresenceEvents.Add(presence);

        db.Presence.Change(user.Id, current, new UserPresence
        {
            State = state,
            Location = newLocation,
            TravelingToLocation = traveling,
            CurrentInstanceLocation = instance?.Location,
            LocationSince = previousLocation == newLocation ? current.LocationSince ?? now : now,
            Platform = platform ?? current.Platform,
            PresenceChangedAt = now,
            StateSince = previousState == state ? current.StateSince ?? current.PresenceChangedAt ?? now : now,
        });
        return presence;
    }

    // ---- worlds / avatars / instances ------------------------------------

    public async Task<World> UpsertWorldAsync(VrcxContext db, Vrc.World api, DateTimeOffset now, CancellationToken ct)
    {
        var world = await db.Worlds.FindAsync([api.Id], ct);
        if (world is null)
        {
            world = new World { Id = api.Id, Name = api.Name, FirstSeenAt = now };
            db.Worlds.Add(world);
        }
        await EnsureUserAsync(db, api.AuthorId, api.AuthorName, now, ct);
        // The original's sanitizeEntityJson on every world it applies.
        world.Name = VrchatText.ReplaceBioSymbols(api.Name);
        world.Description = VrchatText.ReplaceBioSymbolsOrNull(api.Description);
        world.AuthorId = api.AuthorId;
        world.AuthorName = api.AuthorName;
        world.Capacity = api.Capacity;
        world.RecommendedCapacity = api.RecommendedCapacity;
        world.ImageUrl = api.ImageUrl;
        world.ThumbnailImageUrl = api.ThumbnailImageUrl;
        world.ReleaseStatus = api.ReleaseStatus;
        world.Organization = api.Organization;
        world.Tags = api.Tags.ToArray();
        world.Version = api.Version;
        world.Visits = api.Visits;
        world.Favorites = api.Favorites;
        world.Featured = api.Featured;
        world.PreviewYoutubeId = api.PreviewYoutubeId;
        world.PublicationDate = api.PublicationDate;
        world.LabsPublicationDate = api.LabsPublicationDate;
        world.Platforms = BuildPlatforms(api.UnityPackages.Select(p => (p.Platform, p.Variant)));
        world.CreatedAt = api.CreatedAt;
        world.UpdatedAt = api.UpdatedAt;
        world.IsStub = false;
        world.FetchedAt = now;
        return world;
    }

    // The original's getAvailablePlatforms: only standard and security builds count, so an
    // impostor alone does not make content available on a platform.
    private static string[] BuildPlatforms(IEnumerable<(string Platform, string? Variant)> packages) =>
        packages.Where(p => string.IsNullOrEmpty(p.Variant) || p.Variant is "standard" or "security")
            .Select(p => p.Platform).Distinct().Order().ToArray();

    public async Task<World> EnsureWorldAsync(VrcxContext db, string worldId, string? name, DateTimeOffset now, CancellationToken ct)
    {
        var world = await db.Worlds.FindAsync([worldId], ct);
        if (world is not null)
        {
            if (world.IsStub && !string.IsNullOrEmpty(name) && world.Name != name) world.Name = name;
            return world;
        }
        world = new World { Id = worldId, Name = string.IsNullOrEmpty(name) ? worldId : name, IsStub = true, FirstSeenAt = now };
        db.Worlds.Add(world);
        return world;
    }

    public async Task<Avatar> UpsertAvatarAsync(VrcxContext db, Vrc.Avatar api, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await db.Avatars.FindAsync([api.Id], ct);
        if (avatar is null)
        {
            avatar = new Avatar { Id = api.Id, Name = api.Name, FirstSeenAt = now };
            db.Avatars.Add(avatar);
        }
        await EnsureUserAsync(db, api.AuthorId, api.AuthorName, now, ct);
        // The original's sanitizeEntityJson on every avatar it applies.
        avatar.Name = VrchatText.ReplaceBioSymbols(api.Name);
        avatar.Description = VrchatText.ReplaceBioSymbolsOrNull(api.Description);
        avatar.AuthorId = api.AuthorId;
        avatar.AuthorName = api.AuthorName;
        avatar.ImageUrl = api.ImageUrl;
        avatar.ThumbnailImageUrl = api.ThumbnailImageUrl;
        avatar.ReleaseStatus = api.ReleaseStatus;
        avatar.Tags = api.Tags.ToArray();
        avatar.Version = api.Version;
        avatar.Featured = api.Featured;
        avatar.PerformancePc = api.Performance?.Standalonewindows;
        avatar.PerformanceAndroid = api.Performance?.Android;
        avatar.PerformanceIos = api.Performance?.Ios;
        avatar.StylePrimary = api.Styles?.Primary;
        avatar.StyleSecondary = api.Styles?.Secondary;
        avatar.Platforms = BuildPlatforms(api.UnityPackages.Select(p => (p.Platform, p.Variant)));
        // The original's Impostor column: the last impostor package's version (upstream PR 1717).
        avatar.ImpostorVersion = api.UnityPackages.LastOrDefault(p => p.Variant == "impostor")?.ImpostorizerVersion;
        avatar.CreatedAt = api.CreatedAt;
        avatar.UpdatedAt = api.UpdatedAt;
        avatar.IsStub = false;
        avatar.FetchedAt = now;
        return avatar;
    }

    /// <summary>
    /// The account is wearing <paramref name="avatarId"/> as of <paramref name="now"/>. On a swap
    /// the time since the previous one went on is added to its avatar_usages row (the original's
    /// addAvatarWearTime), and the new one's row is touched.
    /// </summary>
    public async Task RecordAvatarWearAsync(VrcxContext db, Account account, string? avatarId, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(avatarId)) return;
        if (account.CurrentAvatarId == avatarId)
        {
            account.CurrentAvatarSince ??= now;
            return;
        }
        if (account.CurrentAvatarId is { } previous && account.CurrentAvatarSince is { } since)
        {
            var usage = await db.AvatarUsages.FindAsync([account.UserId, previous], ct);
            if (usage is not null)
            {
                usage.TimeSpent += now - since;
                usage.LastUsedAt = now;
            }
        }
        await EnsureAvatarAsync(db, avatarId, null, now, ct);
        var current = await db.AvatarUsages.FindAsync([account.UserId, avatarId], ct);
        if (current is null) db.AvatarUsages.Add(new AvatarUsage { OwnerUserId = account.UserId, AvatarId = avatarId, LastUsedAt = now, TimeSpent = TimeSpan.Zero });
        else current.LastUsedAt = now;
        account.CurrentAvatarId = avatarId;
        account.CurrentAvatarSince = now;
    }

    public async Task<Avatar> EnsureAvatarAsync(VrcxContext db, string avatarId, string? name, DateTimeOffset now, CancellationToken ct)
    {
        var avatar = await db.Avatars.FindAsync([avatarId], ct);
        if (avatar is not null) return avatar;
        avatar = new Avatar { Id = avatarId, Name = string.IsNullOrEmpty(name) ? avatarId : name, IsStub = true, FirstSeenAt = now };
        db.Avatars.Add(avatar);
        return avatar;
    }

    /// <summary>The instances row for a location tag, created from the tag alone if needed (world/owner/group stubs included).</summary>
    public async Task<Instance> EnsureInstanceAsync(VrcxContext db, VrchatLocation location, DateTimeOffset now, CancellationToken ct)
    {
        if (!location.IsRealInstance) throw new ArgumentException($"'{location.Tag}' is not an instance location", nameof(location));
        var instance = await db.Instances.FindAsync([location.InstanceLocation], ct);
        if (instance is not null) return instance;

        await EnsureWorldAsync(db, location.WorldId, null, now, ct);
        if (location.OwnerUserId is not null) await EnsureUserAsync(db, location.OwnerUserId, null, now, ct);
        if (location.GroupId is not null) await EnsureGroupAsync(db, location.GroupId, null, now, ct);

        instance = new Instance
        {
            Location = location.InstanceLocation,
            WorldId = location.WorldId,
            InstanceId = location.InstanceId,
            InstanceName = location.InstanceName,
            Type = VrcxEnums.ParseInstanceAccessType(location.AccessType),
            Region = location.Region,
            OwnerUserId = location.OwnerUserId,
            GroupId = location.GroupId,
            GroupAccessType = location.GroupAccessType,
            CanRequestInvite = location.CanRequestInvite,
            AgeGate = location.AgeGate,
            FirstSeenAt = now,
        };
        db.Instances.Add(instance);
        return instance;
    }

    public async Task<Instance> UpsertInstanceAsync(VrcxContext db, Vrc.Instance api, DateTimeOffset now, CancellationToken ct)
    {
        var instance = await EnsureInstanceAsync(db, VrchatLocation.Parse(api.Location), now, ct);
        if (api.World is not null) await UpsertWorldAsync(db, api.World, now, ct);
        instance.Type = VrcxEnums.ParseInstanceAccessType(api.Type);
        instance.Region = api.Region;
        instance.Name = api.Name;
        instance.DisplayName = VrchatText.ReplaceBioSymbolsOrNull(api.DisplayName);
        instance.Capacity = api.Capacity;
        instance.QueueEnabled = api.QueueEnabled;
        instance.Tags = api.Tags.ToArray();
        instance.Permanent = api.Permanent;
        instance.ClientNumber = api.ClientNumber;
        instance.ClosedAt = api.ClosedAt;
        instance.GroupAccessType ??= api.GroupAccessType;
        if (api.AgeGate is { } ageGate) instance.AgeGate = ageGate;
        var owner = api.OwnerIdString;
        if (owner is not null)
        {
            if (owner.StartsWith("grp_", StringComparison.Ordinal))
            {
                await EnsureGroupAsync(db, owner, null, now, ct);
                instance.GroupId = owner;
            }
            else
            {
                await EnsureUserAsync(db, owner, null, now, ct);
                instance.OwnerUserId = owner;
            }
        }
        instance.FetchedAt = now;

        if (api.Users is not null)
        {
            foreach (var occupant in api.Users)
            {
                await ApplyUserAsync(db, occupant.Id, now, new UserFields
                {
                    DisplayName = occupant.DisplayName,
                    Status = occupant.Status,
                    StatusDescription = occupant.StatusDescription,
                    Tags = occupant.Tags.ToArray(),
                    DeveloperType = occupant.DeveloperType,
                    CurrentAvatarImageUrl = occupant.CurrentAvatarImageUrl,
                    CurrentAvatarThumbnailImageUrl = occupant.CurrentAvatarThumbnailImageUrl,
                    ProfilePicOverride = occupant.ProfilePicOverride,
                    UserIcon = occupant.UserIcon,
                    IconUrl = occupant.IconUrl,
                }, ct);
            }
        }
        return instance;
    }

    // ---- groups -----------------------------------------------------------

    public async Task<Group> UpsertGroupAsync(VrcxContext db, Vrc.Group api, DateTimeOffset now, CancellationToken ct)
    {
        var group = await EnsureGroupAsync(db, api.Id, api.Name, now, ct);
        if (api.OwnerId is not null) await EnsureUserAsync(db, api.OwnerId, null, now, ct);
        // The original's sanitizeEntityJson on every group it applies: name, description and rules.
        group.Name = VrchatText.ReplaceBioSymbols(api.Name);
        group.ShortCode = api.ShortCode;
        group.Discriminator = api.Discriminator;
        group.Description = VrchatText.ReplaceBioSymbolsOrNull(api.Description);
        group.IconUrl = api.IconUrl;
        group.BannerUrl = api.BannerUrl;
        group.OwnerUserId = api.OwnerId;
        group.Privacy = api.Privacy;
        group.JoinState = api.JoinState;
        group.Rules = VrchatText.ReplaceBioSymbolsOrNull(api.Rules);
        group.Links = api.Links.ToArray();
        group.Languages = api.Languages.ToArray();
        group.Tags = api.Tags.ToArray();
        group.Badges = api.Badges.ToArray();
        group.MemberCount = api.MemberCount;
        group.IsVerified = api.IsVerified;
        group.CreatedAt = api.CreatedAt;
        group.UpdatedAt = api.UpdatedAt;
        group.IsStub = false;
        group.FetchedAt = now;

        if (api.Roles is not null)
            foreach (var role in api.Roles) await UpsertGroupRoleAsync(db, api.Id, role, now, ct);

        if (api.MyMember is { UserId: not null } me)
        {
            await UpsertMembershipAsync(db, api.Id, me.UserId, new MembershipFields
            {
                MembershipId = me.Id,
                MembershipStatus = me.MembershipStatus ?? api.MembershipStatus ?? GroupMemberStatusValues.Member,
                Visibility = me.Visibility,
                IsRepresenting = me.IsRepresenting,
                ManagerNotes = me.ManagerNotes,
                JoinedAt = me.JoinedAt,
                BannedAt = me.BannedAt,
                RoleIds = me.RoleIds,
            }, now, ct);
        }
        return group;
    }

    public async Task<Group> EnsureGroupAsync(VrcxContext db, string groupId, string? name, DateTimeOffset now, CancellationToken ct)
    {
        var group = await db.Groups.FindAsync([groupId], ct);
        if (group is not null)
        {
            if (group.IsStub && !string.IsNullOrEmpty(name) && group.Name != name) group.Name = name;
            return group;
        }
        group = new Group { Id = groupId, Name = string.IsNullOrEmpty(name) ? groupId : name, IsStub = true, FirstSeenAt = now };
        db.Groups.Add(group);
        return group;
    }

    public async Task<GroupRole> UpsertGroupRoleAsync(VrcxContext db, string groupId, Vrc.GroupRole api, DateTimeOffset now, CancellationToken ct)
    {
        var role = await EnsureGroupRoleAsync(db, groupId, api.Id, ct);
        role.Name = VrchatText.ReplaceBioSymbols(api.Name);
        role.Description = VrchatText.ReplaceBioSymbolsOrNull(api.Description);
        role.IsSelfAssignable = api.IsSelfAssignable;
        role.IsManagementRole = api.IsManagementRole;
        role.IsAddedOnJoin = api.IsAddedOnJoin;
        role.DefaultRole = api.DefaultRole;
        role.RequiresTwoFactor = api.RequiresTwoFactor;
        role.RequiresPurchase = api.RequiresPurchase;
        role.Permissions = api.Permissions.ToArray();
        role.Order = api.Order;
        role.CreatedAt = api.CreatedAt;
        role.UpdatedAt = api.UpdatedAt;
        role.IsStub = false;
        role.FetchedAt = now;
        return role;
    }

    private static async Task<GroupRole> EnsureGroupRoleAsync(VrcxContext db, string groupId, string roleId, CancellationToken ct)
    {
        var role = await db.GroupRoles.FindAsync([roleId], ct);
        if (role is not null) return role;
        role = new GroupRole { Id = roleId, GroupId = groupId, Name = roleId, IsStub = true };
        db.GroupRoles.Add(role);
        return role;
    }

    public sealed record MembershipFields
    {
        public string? MembershipId { get; init; }
        public string MembershipStatus { get; init; } = GroupMemberStatusValues.Member;
        public string? Visibility { get; init; }
        public bool IsRepresenting { get; init; }
        public string? ManagerNotes { get; init; }
        public DateTimeOffset? JoinedAt { get; init; }
        public DateTimeOffset? BannedAt { get; init; }
        /// <summary>Null leaves the roles as they are (the endpoint did not say).</summary>
        public IReadOnlyList<string>? RoleIds { get; init; }
    }

    public async Task<GroupMember> UpsertMembershipAsync(VrcxContext db, string groupId, string userId, MembershipFields f, DateTimeOffset now, CancellationToken ct)
    {
        await EnsureGroupAsync(db, groupId, null, now, ct);
        await EnsureUserAsync(db, userId, null, now, ct);
        var member = await db.GroupMembers.Include(m => m.Roles).FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct)
            ?? db.GroupMembers.Local.FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId);
        if (member is null)
        {
            member = new GroupMember { GroupId = groupId, UserId = userId, FirstSeenAt = now, FetchedAt = now };
            db.GroupMembers.Add(member);
        }
        member.MembershipId = f.MembershipId ?? member.MembershipId;
        member.MembershipStatus = f.MembershipStatus;
        member.Visibility = f.Visibility ?? member.Visibility;
        member.IsRepresenting = f.IsRepresenting;
        member.ManagerNotes = f.ManagerNotes ?? member.ManagerNotes;
        member.JoinedAt = f.JoinedAt ?? member.JoinedAt;
        member.BannedAt = f.BannedAt;
        member.LeftAt = null;
        member.FetchedAt = now;

        if (f.RoleIds is not null)
        {
            var wanted = f.RoleIds.ToHashSet(StringComparer.Ordinal);
            foreach (var existing in member.Roles.ToList())
            {
                if (!wanted.Remove(existing.RoleId)) db.GroupMemberRoles.Remove(existing);
            }
            foreach (var roleId in wanted)
            {
                await EnsureGroupRoleAsync(db, groupId, roleId, ct);
                member.Roles.Add(new GroupMemberRole { GroupId = groupId, UserId = userId, RoleId = roleId });
            }
        }
        return member;
    }

    /// <summary>The original's groupChange: a notice VRChat never sends, made locally when a group the account is in changes.</summary>
    public static Notification GroupChangeNotice(string accountId, string groupId, string groupName, string? iconUrl, string message, DateTimeOffset now) => new()
    {
        Id = $"vrcx_groupchange_{Guid.CreateVersion7():N}",
        OwnerUserId = accountId,
        Type = "groupChange",
        SenderUserId = null,
        SenderUsername = groupName,
        Message = message,
        ImageUrl = iconUrl,
        Link = $"group:{groupId}",
        Details = System.Text.Json.JsonSerializer.SerializeToDocument(new Dictionary<string, string?> { ["groupId"] = groupId, ["imageUrl"] = iconUrl }),
        CreatedAt = now,
        FetchedAt = now,
    };

    /// <summary>The file of VRChat's default robot avatar image (the original's getRobotUrl).</summary>
    public const string RobotAvatarFileId = "file_0e8c4e32-7444-44ea-ade4-313c010d4bae";

    public async Task UpsertGroupMemberAsync(VrcxContext db, Vrc.GroupMember api, DateTimeOffset now, CancellationToken ct)
    {
        if (api.User is not null) await EnsureUserAsync(db, api.User.Id, api.User.DisplayName, now, ct);
        await UpsertMembershipAsync(db, api.GroupId, api.UserId, new MembershipFields
        {
            MembershipId = api.Id,
            MembershipStatus = api.MembershipStatus ?? GroupMemberStatusValues.Member,
            Visibility = api.Visibility,
            IsRepresenting = api.IsRepresenting,
            ManagerNotes = api.ManagerNotes,
            JoinedAt = api.JoinedAt,
            BannedAt = api.BannedAt,
            RoleIds = api.RoleIds,
        }, now, ct);
    }

    /// <summary>GET users/{id}/groups: upserts every listed group + membership and marks the ones no longer listed as left.</summary>
    public async Task SyncUserGroupsAsync(VrcxContext db, string userId, IReadOnlyList<LimitedUserGroup> groups, DateTimeOffset now, CancellationToken ct)
    {
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            listed.Add(g.GroupId);
            var group = await EnsureGroupAsync(db, g.GroupId, g.Name, now, ct);
            if (group.IsStub || group.FetchedAt is null)
            {
                group.Name = VrchatText.ReplaceBioSymbols(g.Name);
                group.ShortCode = g.ShortCode;
                group.Discriminator = g.Discriminator;
                group.Description = VrchatText.ReplaceBioSymbolsOrNull(g.Description);
                group.IconUrl = g.IconUrl;
                group.BannerUrl = g.BannerUrl;
                group.OwnerUserId = g.OwnerId;
                group.Privacy = g.Privacy;
                group.MemberCount = g.MemberCount;
                if (g.OwnerId is not null) await EnsureUserAsync(db, g.OwnerId, null, now, ct);
                group.IsStub = false;
            }
            await UpsertMembershipAsync(db, g.GroupId, userId, new MembershipFields
            {
                MembershipId = g.Id,
                Visibility = g.MemberVisibility,
                IsRepresenting = g.IsRepresenting,
            }, now, ct);
        }

        var current = await db.GroupMembers.Where(m => m.UserId == userId && m.LeftAt == null).ToListAsync(ct);
        foreach (var membership in current)
        {
            if (!listed.Contains(membership.GroupId)) membership.LeftAt = now;
        }
    }

    // ---- friends ----------------------------------------------------------

    public sealed record FriendSyncResult(IReadOnlyList<string> Added, IReadOnlyList<string> Removed);

    /// <summary>
    /// Diffs a complete friend list against the account's open friendships:
    /// new ids get a friendship with the next friend number, missing ids get
    /// their friendship ended unless <paramref name="stillFriends"/> says the
    /// list just missed them. Presence is applied from the current user's
    /// online/active id lists (the list endpoint has no state field).
    /// </summary>
    public async Task<FriendSyncResult> SyncFriendsAsync(
        VrcxContext db, string ownerId, CurrentUser me, IReadOnlyList<LimitedUserFriend> friends, IReadOnlySet<string> stillFriends, DateTimeOffset now, CancellationToken ct)
    {
        var online = me.OnlineFriends.ToHashSet(StringComparer.Ordinal);
        var active = me.ActiveFriends.ToHashSet(StringComparer.Ordinal);

        var open = await db.Friendships.Where(f => f.OwnerUserId == ownerId && f.EndedAt == null).ToDictionaryAsync(f => f.FriendUserId, ct);
        var nextNumber = await db.Friendships.Where(f => f.OwnerUserId == ownerId).Select(f => (int?)f.FriendNumber).MaxAsync(ct) ?? 0;

        var known = await presenceCache.GetManyAsync(friends.Select(f => f.Id).ToArray(), ct);
        foreach (var friend in friends) db.Presence.Remember(friend.Id, known.GetValueOrDefault(friend.Id) ?? UserPresence.Unknown);

        var added = new List<string>();
        foreach (var friend in friends)
        {
            var user = await UpsertFriendAsync(db, friend, now, ct);
            var kind = online.Contains(friend.Id) ? PresenceKind.Online : active.Contains(friend.Id) ? PresenceKind.Active : PresenceKind.Offline;
            await ApplyPresenceAsync(db, user, kind, friend.Location, null, friend.Platform, now, ct);

            if (open.Remove(friend.Id, out var friendship))
            {
                friendship.LastConfirmedAt = now;
                continue;
            }
            db.Friendships.Add(new Friendship
            {
                OwnerUserId = ownerId,
                FriendUserId = friend.Id,
                FriendNumber = ++nextNumber,
                StartedAt = now,
                LastConfirmedAt = now,
            });
            added.Add(friend.Id);
        }

        var removed = new List<string>();
        foreach (var (friendId, friendship) in open)
        {
            if (stillFriends.Contains(friendId))
            {
                friendship.LastConfirmedAt = now;
                continue;
            }
            friendship.EndedAt = now;
            removed.Add(friendId);
        }

        if (added.Count > 0 || removed.Count > 0)
            logger.LogInformation("Friend sync for {Owner}: +{Added} -{Removed}", ownerId, added.Count, removed.Count);
        return new FriendSyncResult(added, removed);
    }

    /// <summary>friend-add from the pipeline: opens a friendship if none is open. Returns it, or null when one was already open.</summary>
    public async Task<Friendship?> AddFriendAsync(VrcxContext db, string ownerId, string friendId, DateTimeOffset now, CancellationToken ct)
    {
        await EnsureUserAsync(db, friendId, null, now, ct);
        var exists = await db.Friendships.AnyAsync(f => f.OwnerUserId == ownerId && f.FriendUserId == friendId && f.EndedAt == null, ct);
        if (exists) return null;
        var nextNumber = await db.Friendships.Where(f => f.OwnerUserId == ownerId).Select(f => (int?)f.FriendNumber).MaxAsync(ct) ?? 0;
        var friendship = new Friendship
        {
            OwnerUserId = ownerId, FriendUserId = friendId, FriendNumber = nextNumber + 1, StartedAt = now, LastConfirmedAt = now,
        };
        db.Friendships.Add(friendship);
        return friendship;
    }

    /// <summary>friend-delete from the pipeline: ends the open friendship. Returns it, or null when none was open.</summary>
    public async Task<Friendship?> RemoveFriendAsync(VrcxContext db, string ownerId, string friendId, DateTimeOffset now, CancellationToken ct)
    {
        var friendship = await db.Friendships.FirstOrDefaultAsync(f => f.OwnerUserId == ownerId && f.FriendUserId == friendId && f.EndedAt == null, ct);
        if (friendship is not null) friendship.EndedAt = now;
        return friendship;
    }

    // ---- notifications ----------------------------------------------------

    public async Task<Notification> UpsertNotificationAsync(VrcxContext db, string ownerId, Vrc.Notification api, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.Notifications.FindAsync([ownerId, api.Id], ct);
        if (row is null)
        {
            row = new Notification { Id = api.Id, OwnerUserId = ownerId, Type = api.Type, Version = 1, CreatedAt = api.CreatedAt ?? now };
            db.Notifications.Add(row);
        }
        if (!string.IsNullOrEmpty(api.SenderUserId))
        {
            await EnsureUserAsync(db, api.SenderUserId, api.SenderUsername, now, ct);
            row.SenderUserId = api.SenderUserId;
        }
        row.Type = api.Type;
        row.SenderUsername = api.SenderUsername;
        row.ReceiverUserId = api.ReceiverUserId;
        // The original's sanitizeNotificationJson.
        row.Message = VrchatText.ReplaceBioSymbols(api.Message);
        row.Details = ToDocument(api.Details) ?? row.Details;
        row.Seen = api.Seen;
        row.FetchedAt = now;
        return row;
    }

    public async Task<Notification> UpsertNotificationAsync(VrcxContext db, string ownerId, NotificationV2 api, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.Notifications.FindAsync([ownerId, api.Id], ct);
        if (row is null)
        {
            row = new Notification { Id = api.Id, OwnerUserId = ownerId, Type = api.Type, Version = 2, CreatedAt = api.CreatedAt ?? now };
            db.Notifications.Add(row);
        }
        if (!string.IsNullOrEmpty(api.SenderUserId))
        {
            await EnsureUserAsync(db, api.SenderUserId, api.SenderUsername, now, ct);
            row.SenderUserId = api.SenderUserId;
        }
        row.Type = api.Type;
        row.SenderUsername = api.SenderUsername;
        row.ReceiverUserId = api.ReceiverUserId;
        row.Title = VrchatText.ReplaceBioSymbolsOrNull(api.Title);
        row.Message = VrchatText.ReplaceBioSymbols(api.Message);
        row.ImageUrl = api.ImageUrl;
        row.Link = api.Link;
        row.LinkText = api.LinkText;
        row.Details = ToDocument(api.Data) ?? row.Details;
        row.Responses = api.Responses is null ? row.Responses : System.Text.Json.JsonSerializer.SerializeToDocument(api.Responses, VrchatJson.Options);
        row.Seen = api.Seen;
        row.UpdatedAt = api.UpdatedAt;
        row.ExpiresAt = api.ExpiresAt;
        row.FetchedAt = now;
        return row;
    }

    public async Task MarkNotificationAsync(VrcxContext db, string ownerId, string notificationId, bool? seen, bool? hidden, CancellationToken ct)
    {
        var row = await db.Notifications.FindAsync([ownerId, notificationId], ct);
        if (row is null) return;
        if (seen is { } s) row.Seen = s;
        if (hidden is { } h) row.Hidden = h;
    }

    private static System.Text.Json.JsonDocument? ToDocument(System.Text.Json.JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.Null) return null;
        // VRChat occasionally double-encodes details as a JSON string.
        if (element.Value.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var text = element.Value.GetString();
            if (!string.IsNullOrEmpty(text) && text[0] is '{' or '[')
            {
                try { return System.Text.Json.JsonDocument.Parse(text); }
                catch (System.Text.Json.JsonException) { /* fall through: keep it as the string it is */ }
            }
        }
        return System.Text.Json.JsonDocument.Parse(element.Value.GetRawText());
    }

    // ---- favorites / moderations / notes ----------------------------------

    /// <summary>Makes sure the row a favorite points at exists, so it has a name to show.</summary>
    public async Task EnsureFavoriteTargetAsync(VrcxContext db, FavoriteType type, string targetId, DateTimeOffset now, CancellationToken ct)
    {
        switch (type)
        {
            case FavoriteType.Friend: await EnsureUserAsync(db, targetId, null, now, ct); break;
            case FavoriteType.World: await EnsureWorldAsync(db, targetId, null, now, ct); break;
            case FavoriteType.Avatar: await EnsureAvatarAsync(db, targetId, null, now, ct); break;
        }
    }

    /// <summary>Stores one favorite VRChat just accepted, without waiting for the next full sync.</summary>
    public async Task UpsertFavoriteAsync(VrcxContext db, string ownerId, Vrc.Favorite favorite, DateTimeOffset now, CancellationToken ct)
    {
        if (VrcxEnums.TryParseFavoriteType(favorite.Type) is not { } type) return;
        await EnsureFavoriteTargetAsync(db, type, favorite.FavoriteId, now, ct);
        var row = await db.Favorites.FirstOrDefaultAsync(f => f.OwnerUserId == ownerId && f.Type == type && f.TargetId == favorite.FavoriteId, ct);
        if (row is null)
        {
            row = new Favorite { Id = favorite.Id, OwnerUserId = ownerId, Type = type, TargetId = favorite.FavoriteId, FirstSeenAt = now };
            db.Favorites.Add(row);
        }
        row.GroupName = favorite.Tags.Count > 0 ? favorite.Tags[0] : null;
        row.FetchedAt = now;
    }

    public async Task SyncFavoritesAsync(
        VrcxContext db, string ownerId, IReadOnlyList<Vrc.FavoriteGroup> groups, IReadOnlyList<Vrc.Favorite> favorites, DateTimeOffset now, CancellationToken ct)
    {
        var existingGroups = await db.FavoriteGroups.Where(g => g.OwnerUserId == ownerId).ToDictionaryAsync(g => g.Id, ct);
        foreach (var g in groups)
        {
            if (!existingGroups.Remove(g.Id, out var row))
            {
                row = new FavoriteGroup { Id = g.Id, OwnerUserId = ownerId, Type = g.Type, Name = g.Name };
                db.FavoriteGroups.Add(row);
            }
            row.Type = g.Type;
            row.Name = g.Name;
            row.DisplayName = g.DisplayName;
            row.Visibility = g.Visibility;
            row.FetchedAt = now;
        }
        db.FavoriteGroups.RemoveRange(existingGroups.Values);

        var existing = await db.Favorites.Where(f => f.OwnerUserId == ownerId).ToDictionaryAsync(f => f.Id, ct);
        foreach (var f in favorites)
        {
            if (VrcxEnums.TryParseFavoriteType(f.Type) is not { } type)
            {
                logger.LogWarning("Skipping favorite {Id} of unknown type '{Type}'", f.Id, f.Type);
                continue;
            }
            await EnsureFavoriteTargetAsync(db, type, f.FavoriteId, now, ct);
            if (!existing.Remove(f.Id, out var row))
            {
                row = new Favorite { Id = f.Id, OwnerUserId = ownerId, Type = type, TargetId = f.FavoriteId, FirstSeenAt = now };
                db.Favorites.Add(row);
            }
            row.Type = type;
            row.TargetId = f.FavoriteId;
            row.GroupName = f.Tags.FirstOrDefault(t => t.StartsWith("group_", StringComparison.Ordinal)) ?? (f.Tags.Count > 0 ? f.Tags[0] : null);
            row.FetchedAt = now;
        }
        db.Favorites.RemoveRange(existing.Values);
    }

    public async Task SyncPlayerModerationsAsync(VrcxContext db, string ownerId, IReadOnlyList<Vrc.PlayerModeration> moderations, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.PlayerModerations.Where(m => m.OwnerUserId == ownerId).ToDictionaryAsync(m => m.Id, ct);
        foreach (var m in moderations)
        {
            await EnsureUserAsync(db, m.TargetUserId, m.TargetDisplayName, now, ct);
            if (!existing.Remove(m.Id, out var row))
            {
                row = new PlayerModeration { Id = m.Id, OwnerUserId = ownerId, TargetUserId = m.TargetUserId, Type = m.Type };
                db.PlayerModerations.Add(row);
            }
            row.TargetUserId = m.TargetUserId;
            row.Type = m.Type;
            row.CreatedAt = m.Created;
            row.FetchedAt = now;
        }
        db.PlayerModerations.RemoveRange(existing.Values);
    }

    /// <summary>
    /// The original's checkNote: a user object carries the account's note on them. An emptied note
    /// is deleted and a changed one updated. True when it is a note not stored yet, which needs
    /// VRChat's notes read again for its id.
    /// </summary>
    public async Task<bool> ApplySeenNoteAsync(VrcxContext db, string ownerId, string targetUserId, string? note, DateTimeOffset now, CancellationToken ct)
    {
        if (note is null) return false;
        var row = await db.UserNotes.FirstOrDefaultAsync(n => n.OwnerUserId == ownerId && n.TargetUserId == targetUserId, ct);
        if (note.Length == 0)
        {
            if (row is not null) db.UserNotes.Remove(row);
            return false;
        }
        if (row is null) return true;
        var text = VrchatText.ReplaceBioSymbols(note);
        if (row.Note != text)
        {
            row.Note = text;
            row.FetchedAt = now;
        }
        return false;
    }

    /// <summary>Stores VRChat's notes. A full list also drops the stored notes it no longer has; a page only adds and updates.</summary>
    public async Task SyncUserNotesAsync(VrcxContext db, string ownerId, IReadOnlyList<Vrc.UserNote> notes, DateTimeOffset now, CancellationToken ct, bool complete = true)
    {
        var existing = await db.UserNotes.Where(n => n.OwnerUserId == ownerId).ToDictionaryAsync(n => n.Id, ct);
        foreach (var n in notes)
        {
            await EnsureUserAsync(db, n.TargetUserId, null, now, ct);
            if (!existing.Remove(n.Id, out var row))
            {
                row = new UserNote { Id = n.Id, OwnerUserId = ownerId, TargetUserId = n.TargetUserId };
                db.UserNotes.Add(row);
            }
            row.TargetUserId = n.TargetUserId;
            row.Note = VrchatText.ReplaceBioSymbols(n.Note);
            row.CreatedAt = n.CreatedAt;
            row.FetchedAt = now;
        }
        if (complete) db.UserNotes.RemoveRange(existing.Values);
    }
}
