using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VRCX.Common.VrcxDb;

/// <summary>
/// This is meant for use in migrations only.
/// </summary>
public sealed class MigrationVrcxContext : VrcxContext
{
    private readonly string _connectionString;
    private readonly bool _debug;
    private readonly ILoggerFactory? _loggerFactory;

    public MigrationVrcxContext(string connectionString, bool debug, ILoggerFactory? loggerFactory = null)
    {
        _connectionString = connectionString;
        _debug = debug;
        _loggerFactory = loggerFactory;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ConfigureOptionsBuilder(optionsBuilder, _connectionString, _debug);

        if (_loggerFactory is not null)
            optionsBuilder.UseLoggerFactory(_loggerFactory);
    }
}

/// <summary>
/// Main VRCX DB Context. Code-first: this model is the schema. Change it, then
/// <c>dotnet ef migrations add &lt;Name&gt; --project Common --startup-project MigrationHelper -o Migrations</c>;
/// the API host applies pending migrations on startup (the Cron host waits for them).
/// Table and column names are explicit snake_case; timestamps are timestamptz, arrays text[], JSON jsonb.
/// </summary>
public class VrcxContext : DbContext
{
    protected VrcxContext()
    {
    }

    public VrcxContext(DbContextOptions<VrcxContext> options)
        : base(options)
    {
    }

    public static void ConfigureOptionsBuilder(DbContextOptionsBuilder optionsBuilder, string connectionString, bool debug)
    {
        optionsBuilder.UseNpgsql(connectionString, npgsqlBuilder => npgsqlBuilder.MapPgEnums());

        if (debug)
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
        }
    }

    /// <summary>Presence read or changed in this unit of work; see PresenceFlushInterceptor.</summary>
    public VRCX.Common.Services.Presence.PresenceUnitOfWork Presence { get; } = new();

    public override void Dispose()
    {
        Presence.Clear();
        base.Dispose();
    }

    public override ValueTask DisposeAsync()
    {
        Presence.Clear();
        return base.DisposeAsync();
    }

    public DbSet<CachedImage> CachedImages { get; set; }

    public DbSet<User> Users { get; set; }

    public DbSet<Account> Accounts { get; set; }

    public DbSet<Friendship> Friendships { get; set; }

    public DbSet<UserDisplayNameChange> UserDisplayNameChanges { get; set; }

    public DbSet<UserTrustChange> UserTrustChanges { get; set; }

    public DbSet<UserStatusChange> UserStatusChanges { get; set; }

    public DbSet<UserBioChange> UserBioChanges { get; set; }

    public DbSet<UserAvatarChange> UserAvatarChanges { get; set; }

    public DbSet<PresenceEvent> PresenceEvents { get; set; }

    public DbSet<InstanceVisit> InstanceVisits { get; set; }

    public DbSet<UserNote> UserNotes { get; set; }

    public DbSet<PlayerModeration> PlayerModerations { get; set; }
    public DbSet<GameLogEvent> GameLogEvents { get; set; }
    public DbSet<GameLogHiddenEntry> GameLogHiddenEntries { get; set; }
    public DbSet<FriendRequestEvent> FriendRequestEvents { get; set; }
    public DbSet<FriendLogHiddenEntry> FriendLogHiddenEntries { get; set; }

    public DbSet<UserMemo> UserMemos { get; set; }
    public DbSet<CustomUserTag> CustomUserTags { get; set; }

    public DbSet<World> Worlds { get; set; }

    public DbSet<Avatar> Avatars { get; set; }

    public DbSet<Instance> Instances { get; set; }

    public DbSet<WorldMemo> WorldMemos { get; set; }

    public DbSet<AvatarMemo> AvatarMemos { get; set; }

    public DbSet<AvatarTag> AvatarTags { get; set; }

    public DbSet<AvatarUsage> AvatarUsages { get; set; }

    public DbSet<Group> Groups { get; set; }

    public DbSet<GroupRole> GroupRoles { get; set; }

    public DbSet<GroupMember> GroupMembers { get; set; }

    public DbSet<GroupMemberRole> GroupMemberRoles { get; set; }

    public DbSet<Notification> Notifications { get; set; }

    public DbSet<Favorite> Favorites { get; set; }
    public DbSet<LocalFavorite> LocalFavorites { get; set; }
    public DbSet<LocalFavoriteGroup> LocalFavoriteGroups { get; set; }
    public DbSet<ServerSetting> ServerSettings { get; set; }
    public DbSet<LegacySetting> LegacySettings { get; set; }
    public DbSet<UnidentifiedVisit> UnidentifiedVisits { get; set; }
    public DbSet<ModerationAgainst> ModerationsAgainst { get; set; }
    public DbSet<PrintFavorite> PrintFavorites { get; set; }

    public DbSet<FavoriteGroup> FavoriteGroups { get; set; }

    public DbSet<VideoPlay> VideoPlays { get; set; }

    public DbSet<StickerSpawn> StickerSpawns { get; set; }

    public DbSet<ResourceLoad> ResourceLoads { get; set; }

    public DbSet<GameLogClient> GameLogClients { get; set; }

    public DbSet<MutualFriendLink> MutualFriendLinks { get; set; }

    public DbSet<MutualGraphMeta> MutualGraphMetas { get; set; }

    public DbSet<PortalSpawn> PortalSpawns { get; set; }

    public DbSet<SavedCredential> SavedCredentials { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=vrcx;Username=vrcx;Password=vrcx");
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Postgres text cannot hold NUL (22021 "invalid byte sequence for encoding UTF8: 0x00"),
        // and VRChat lets users paste one into a bio, a group description or rules. Strip it on
        // the way in for every string and string[] column rather than chasing each field.
        configurationBuilder.Properties<string>().HaveConversion<NulStrippingStringConverter>();
        configurationBuilder.Properties<string[]>().HaveConversion<NulStrippingStringArrayConverter, StringArrayComparer>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.RegisterPgEnums();

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id).HasName("users_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.Bio).HasColumnName("bio");
            entity.Property(x => x.BioLinks).HasColumnName("bio_links");
            entity.Property(x => x.Pronouns).HasColumnName("pronouns");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.StatusDescription).HasColumnName("status_description");
            entity.Property(x => x.Tags).HasColumnName("tags");
            entity.Property(x => x.TrustLevel).HasColumnName("trust_level");
            entity.Property(x => x.DeveloperType).HasColumnName("developer_type");
            entity.Property(x => x.CurrentAvatarImageUrl).HasColumnName("current_avatar_image_url");
            entity.Property(x => x.CurrentAvatarThumbnailImageUrl).HasColumnName("current_avatar_thumbnail_image_url");
            entity.Property(x => x.CurrentAvatarTags).HasColumnName("current_avatar_tags");
            entity.Property(x => x.ProfilePicOverride).HasColumnName("profile_pic_override");
            entity.Property(x => x.UserIcon).HasColumnName("user_icon");
            entity.Property(x => x.IconUrl).HasColumnName("icon_url");
            entity.Property(x => x.BannerUrl).HasColumnName("banner_url");
            entity.Property(x => x.BannerType).HasColumnName("banner_type");
            entity.Property(x => x.LastPlatform).HasColumnName("last_platform");
            entity.Property(x => x.LastLogin).HasColumnName("last_login");
            entity.Property(x => x.LastActivity).HasColumnName("last_activity");
            entity.Property(x => x.LastMobile).HasColumnName("last_mobile");
            entity.Property(x => x.DateJoined).HasColumnName("date_joined");
            entity.Property(x => x.AgeVerificationStatus).HasColumnName("age_verification_status");
            entity.Property(x => x.AgeVerified).HasColumnName("age_verified");
            entity.Property(x => x.AllowAvatarCopying).HasColumnName("allow_avatar_copying");
            entity.Property(x => x.IsStub).HasColumnName("is_stub");
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.LastFullFetchAt).HasColumnName("last_full_fetch_at");
            entity.Property(x => x.ProfileFetchedAt).HasColumnName("profile_fetched_at");
            entity.Property(x => x.Badges)
                .HasColumnName("badges")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Web),
                    v => JsonSerializer.Deserialize<List<VRCX.Vrchat.Models.Badge>>(v, JsonSerializerOptions.Web) ?? new List<VRCX.Vrchat.Models.Badge>(),
                    new ValueComparer<List<VRCX.Vrchat.Models.Badge>>(
                        (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Web) == JsonSerializer.Serialize(b, JsonSerializerOptions.Web),
                        v => JsonSerializer.Serialize(v, JsonSerializerOptions.Web).GetHashCode(),
                        v => JsonSerializer.Deserialize<List<VRCX.Vrchat.Models.Badge>>(JsonSerializer.Serialize(v, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!));

            entity.HasIndex(x => x.DisplayName);
            entity.HasIndex(x => x.TrustLevel);
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(x => x.UserId).HasName("accounts_pkey");

            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Username).HasColumnName("username");
            entity.Property(x => x.Cookies).HasColumnName("cookies");
            entity.Property(x => x.HomeLocation).HasColumnName("home_location");
            entity.Property(x => x.SharedConnectionsOptOut).HasColumnName("shared_connections_opt_out");
            entity.Property(x => x.CurrentAvatarId).HasColumnName("current_avatar_id");
            entity.Property(x => x.CurrentAvatarSince).HasColumnName("current_avatar_since");
            entity.Property(x => x.AvatarHistoryClearedAt).HasColumnName("avatar_history_cleared_at");
            entity.Property(x => x.FriendGroupNames).HasColumnName("friend_group_names");
            entity.Property(x => x.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            entity.Property(x => x.SessionValid).HasColumnName("session_valid");
            entity.Property(x => x.LoggedInAt).HasColumnName("logged_in_at");
            entity.Property(x => x.LastFriendSyncAt).HasColumnName("last_friend_sync_at");
            entity.Property(x => x.LastFullSyncAt).HasColumnName("last_full_sync_at");

            entity.HasOne(x => x.User).WithOne().HasForeignKey<Account>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_accounts_user_id");
            // A plain POCO list stored as jsonb through a converter.
            entity.Property(x => x.Cookies)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Web),
                    v => JsonSerializer.Deserialize<List<StoredCookie>>(v, JsonSerializerOptions.Web) ?? new List<StoredCookie>(),
                    new ValueComparer<List<StoredCookie>>(
                        (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Web) == JsonSerializer.Serialize(b, JsonSerializerOptions.Web),
                        v => JsonSerializer.Serialize(v, JsonSerializerOptions.Web).GetHashCode(),
                        v => JsonSerializer.Deserialize<List<StoredCookie>>(JsonSerializer.Serialize(v, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!));
            // The app settings record, the same way.
            entity.Property(x => x.Settings)
                .HasColumnName("settings")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonSerializerOptions.Web),
                    v => JsonSerializer.Deserialize<AccountSettings>(v, JsonSerializerOptions.Web) ?? new AccountSettings(),
                    new ValueComparer<AccountSettings>(
                        (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Web) == JsonSerializer.Serialize(b, JsonSerializerOptions.Web),
                        v => JsonSerializer.Serialize(v, JsonSerializerOptions.Web).GetHashCode(),
                        v => v));
        });

        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.ToTable("friendships");
            entity.HasKey(x => x.Id).HasName("friendships_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.FriendUserId).HasColumnName("friend_user_id");
            entity.Property(x => x.FriendNumber).HasColumnName("friend_number");
            entity.Property(x => x.StartedAt).HasColumnName("started_at");
            entity.Property(x => x.EndedAt).HasColumnName("ended_at");
            entity.Property(x => x.LastConfirmedAt).HasColumnName("last_confirmed_at");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.EndedDisplayName).HasColumnName("ended_display_name");

            entity.HasOne(x => x.Owner).WithMany(a => a.Friendships).HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_friendships_owner_user_id");
            entity.HasOne(x => x.Friend).WithMany(u => u.FriendshipsAsFriend).HasForeignKey(x => x.FriendUserId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_friendships_friend_user_id");
            // One open friendship per (owner, friend); ended ones can repeat.
            entity.HasIndex(x => new { x.OwnerUserId, x.FriendUserId }).IsUnique().HasFilter("ended_at IS NULL");
            entity.HasIndex(x => new { x.OwnerUserId, x.FriendNumber });
            entity.HasIndex(x => new { x.OwnerUserId, x.StartedAt });
        });

        modelBuilder.Entity<UserDisplayNameChange>(entity =>
        {
            entity.ToTable("user_display_name_changes");
            entity.HasKey(x => x.Id).HasName("user_display_name_changes_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ChangedAt).HasColumnName("changed_at");
            entity.Property(x => x.PreviousDisplayName).HasColumnName("previous_display_name");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");

            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_display_name_changes_user_id");
            entity.HasIndex(x => new { x.UserId, x.ChangedAt });
            entity.HasIndex(x => x.ChangedAt);
        });

        modelBuilder.Entity<UserTrustChange>(entity =>
        {
            entity.ToTable("user_trust_changes");
            entity.HasKey(x => x.Id).HasName("user_trust_changes_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ChangedAt).HasColumnName("changed_at");
            entity.Property(x => x.PreviousTrustLevel).HasColumnName("previous_trust_level");
            entity.Property(x => x.TrustLevel).HasColumnName("trust_level");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");

            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_trust_changes_user_id");
            entity.HasIndex(x => new { x.UserId, x.ChangedAt });
            entity.HasIndex(x => x.ChangedAt);
        });

        modelBuilder.Entity<UserStatusChange>(entity =>
        {
            entity.ToTable("user_status_changes");
            entity.HasKey(x => x.Id).HasName("user_status_changes_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ChangedAt).HasColumnName("changed_at");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.StatusDescription).HasColumnName("status_description");
            entity.Property(x => x.PreviousStatus).HasColumnName("previous_status");
            entity.Property(x => x.PreviousStatusDescription).HasColumnName("previous_status_description");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");

            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_status_changes_user_id");
            entity.HasIndex(x => new { x.UserId, x.ChangedAt });
            entity.HasIndex(x => x.ChangedAt);
        });

        modelBuilder.Entity<UserBioChange>(entity =>
        {
            entity.ToTable("user_bio_changes");
            entity.HasKey(x => x.Id).HasName("user_bio_changes_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ChangedAt).HasColumnName("changed_at");
            entity.Property(x => x.Bio).HasColumnName("bio");
            entity.Property(x => x.BioLinks).HasColumnName("bio_links");
            entity.Property(x => x.PreviousBio).HasColumnName("previous_bio");
            entity.Property(x => x.PreviousBioLinks).HasColumnName("previous_bio_links");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");

            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_bio_changes_user_id");
            entity.HasIndex(x => new { x.UserId, x.ChangedAt });
            entity.HasIndex(x => x.ChangedAt);
        });

        modelBuilder.Entity<UserAvatarChange>(entity =>
        {
            entity.ToTable("user_avatar_changes");
            entity.HasKey(x => x.Id).HasName("user_avatar_changes_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ChangedAt).HasColumnName("changed_at");
            entity.Property(x => x.ImageUrl).HasColumnName("image_url");
            entity.Property(x => x.ThumbnailImageUrl).HasColumnName("thumbnail_image_url");
            entity.Property(x => x.PreviousImageUrl).HasColumnName("previous_image_url");
            entity.Property(x => x.PreviousThumbnailImageUrl).HasColumnName("previous_thumbnail_image_url");
            entity.Property(x => x.AvatarName).HasColumnName("avatar_name");
            entity.Property(x => x.AvatarOwnerId).HasColumnName("avatar_owner_id");
            entity.Property(x => x.PreviousAvatarName).HasColumnName("previous_avatar_name");
            entity.Property(x => x.PreviousAvatarOwnerId).HasColumnName("previous_avatar_owner_id");
            entity.Property(x => x.AvatarId).HasColumnName("avatar_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");

            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_avatar_changes_user_id");
            entity.HasOne(x => x.Avatar).WithMany().HasForeignKey(x => x.AvatarId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_user_avatar_changes_avatar_id");
            entity.HasIndex(x => new { x.UserId, x.ChangedAt });
            entity.HasIndex(x => x.ChangedAt);
        });

        modelBuilder.Entity<PresenceEvent>(entity =>
        {
            entity.ToTable("presence_events");
            // Partitioned by month on at, so the key has to include it.
            entity.HasKey(x => new { x.Id, x.At }).HasName("presence_events_pkey");

            entity.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.At).HasColumnName("at");
            entity.Property(x => x.Kind).HasColumnName("kind");
            entity.Property(x => x.Location).HasColumnName("location");
            entity.Property(x => x.PreviousLocation).HasColumnName("previous_location");
            entity.Property(x => x.TravelingToLocation).HasColumnName("traveling_to_location");
            entity.Property(x => x.InstanceLocation).HasColumnName("instance_location");
            entity.Property(x => x.Platform).HasColumnName("platform");
            entity.Property(x => x.PreviousState).HasColumnName("previous_state");
            entity.Property(x => x.DurationAtPrevious).HasColumnName("duration_at_previous");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.WorldName).HasColumnName("world_name");
            entity.Property(x => x.GroupName).HasColumnName("group_name");

            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_presence_events_user_id");
            entity.HasOne(x => x.Instance).WithMany().HasForeignKey(x => x.InstanceLocation)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_presence_events_instance_location");
            entity.HasIndex(x => new { x.UserId, x.At });
            entity.HasIndex(x => x.At);
        });

        modelBuilder.Entity<InstanceVisit>(entity =>
        {
            // Reads come from the view: stored game log visits plus the pipeline visits worked out from
            // presence_events. Writes go to the table, which only game log visits still need.
            entity.ToTable("stored_instance_visits");
            entity.ToView("instance_visits");
            entity.HasKey(x => x.Id).HasName("instance_visits_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.InstanceLocation).HasColumnName("instance_location");
            entity.Property(x => x.JoinedAt).HasColumnName("joined_at");
            entity.Property(x => x.LeftAt).HasColumnName("left_at");
            entity.Property(x => x.Source).HasColumnName("source");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.WorldName).HasColumnName("world_name");
            entity.Property(x => x.GroupName).HasColumnName("group_name");

            entity.HasOne(x => x.User).WithMany(u => u.Visits).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_instance_visits_user_id");
            entity.HasOne(x => x.Instance).WithMany(i => i.Visits).HasForeignKey(x => x.InstanceLocation)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_instance_visits_instance_location");
            entity.HasIndex(x => new { x.UserId, x.JoinedAt });
            entity.HasIndex(x => new { x.InstanceLocation, x.JoinedAt });
            // At most one open visit per user per source.
            entity.HasIndex(x => new { x.UserId, x.Source }).IsUnique().HasFilter("left_at IS NULL");
        });

        modelBuilder.Entity<UserNote>(entity =>
        {
            entity.ToTable("user_notes");
            entity.HasKey(x => x.Id).HasName("user_notes_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.TargetUserId).HasColumnName("target_user_id");
            entity.Property(x => x.Note).HasColumnName("note");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_notes_owner_user_id");
            entity.HasOne(x => x.Target).WithMany().HasForeignKey(x => x.TargetUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_notes_target_user_id");
            entity.HasIndex(x => new { x.OwnerUserId, x.TargetUserId }).IsUnique();
        });

        modelBuilder.Entity<PlayerModeration>(entity =>
        {
            entity.ToTable("player_moderations");
            entity.HasKey(x => x.Id).HasName("player_moderations_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.TargetUserId).HasColumnName("target_user_id");
            entity.Property(x => x.Type).HasColumnName("type");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_player_moderations_owner_user_id");
            entity.HasOne(x => x.Target).WithMany().HasForeignKey(x => x.TargetUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_player_moderations_target_user_id");
            entity.HasIndex(x => new { x.OwnerUserId, x.TargetUserId, x.Type }).IsUnique();
        });

        modelBuilder.Entity<FriendRequestEvent>(entity =>
        {
            entity.ToTable("friend_request_events");
            entity.HasKey(x => x.Id).HasName("friend_request_events_pkey");
            entity.HasIndex(x => new { x.OwnerUserId, x.CreatedAt }, "idx_friend_request_events_owner_created").IsDescending(false, true);

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.TargetUserId).HasColumnName("target_user_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.Type).HasColumnName("type");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_friend_request_events_owner_user_id");
        });

        modelBuilder.Entity<FriendLogHiddenEntry>(entity =>
        {
            entity.ToTable("friend_log_hidden_entries");
            entity.HasKey(x => new { x.OwnerUserId, x.EntryKey }).HasName("friend_log_hidden_entries_pkey");

            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.EntryKey).HasColumnName("entry_key");
            entity.Property(x => x.HiddenAt).HasColumnName("hidden_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_friend_log_hidden_entries_owner_user_id");
        });

        modelBuilder.Entity<UserMemo>(entity =>
        {
            entity.ToTable("user_memos");
            entity.HasKey(x => x.UserId).HasName("user_memos_pkey");

            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Memo).HasColumnName("memo");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(x => x.User).WithOne().HasForeignKey<UserMemo>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_user_memos_user_id");
        });

        modelBuilder.Entity<CustomUserTag>(entity =>
        {
            entity.ToTable("custom_user_tags");
            entity.HasKey(x => new { x.OwnerUserId, x.UserId }).HasName("custom_user_tags_pkey");

            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Tag).HasColumnName("tag");
            entity.Property(x => x.Colour).HasColumnName("colour");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_custom_user_tags_owner_user_id");
        });

        modelBuilder.Entity<World>(entity =>
        {
            entity.ToTable("worlds");
            entity.HasKey(x => x.Id).HasName("worlds_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.Description).HasColumnName("description");
            entity.Property(x => x.AuthorId).HasColumnName("author_id");
            entity.Property(x => x.AuthorName).HasColumnName("author_name");
            entity.Property(x => x.Capacity).HasColumnName("capacity");
            entity.Property(x => x.RecommendedCapacity).HasColumnName("recommended_capacity");
            entity.Property(x => x.ImageUrl).HasColumnName("image_url");
            entity.Property(x => x.ThumbnailImageUrl).HasColumnName("thumbnail_image_url");
            entity.Property(x => x.ReleaseStatus).HasColumnName("release_status");
            entity.Property(x => x.Organization).HasColumnName("organization");
            entity.Property(x => x.Tags).HasColumnName("tags");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.Property(x => x.Visits).HasColumnName("visits");
            entity.Property(x => x.Favorites).HasColumnName("favorites");
            entity.Property(x => x.Featured).HasColumnName("featured");
            entity.Property(x => x.PreviewYoutubeId).HasColumnName("preview_youtube_id");
            entity.Property(x => x.PublicationDate).HasColumnName("publication_date");
            entity.Property(x => x.LabsPublicationDate).HasColumnName("labs_publication_date");
            entity.Property(x => x.Platforms).HasColumnName("platforms");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.IsStub).HasColumnName("is_stub");
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_worlds_author_id");
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.AuthorId);
        });

        modelBuilder.Entity<Avatar>(entity =>
        {
            entity.ToTable("avatars");
            entity.HasKey(x => x.Id).HasName("avatars_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.Description).HasColumnName("description");
            entity.Property(x => x.AuthorId).HasColumnName("author_id");
            entity.Property(x => x.AuthorName).HasColumnName("author_name");
            entity.Property(x => x.ImageUrl).HasColumnName("image_url");
            entity.Property(x => x.ThumbnailImageUrl).HasColumnName("thumbnail_image_url");
            entity.Property(x => x.ReleaseStatus).HasColumnName("release_status");
            entity.Property(x => x.Tags).HasColumnName("tags");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.Property(x => x.Featured).HasColumnName("featured");
            entity.Property(x => x.PerformancePc).HasColumnName("performance_pc");
            entity.Property(x => x.PerformanceAndroid).HasColumnName("performance_android");
            entity.Property(x => x.PerformanceIos).HasColumnName("performance_ios");
            entity.Property(x => x.StylePrimary).HasColumnName("style_primary");
            entity.Property(x => x.StyleSecondary).HasColumnName("style_secondary");
            entity.Property(x => x.Platforms).HasColumnName("platforms");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.IsStub).HasColumnName("is_stub");
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_avatars_author_id");
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.AuthorId);
            // Avatar changes are matched to avatars by image URL, so look it up fast.
            entity.HasIndex(x => x.ImageUrl);
        });

        modelBuilder.Entity<Instance>(entity =>
        {
            entity.ToTable("instances");
            entity.HasKey(x => x.Location).HasName("instances_pkey");

            entity.Property(x => x.Location).HasColumnName("location");
            entity.Property(x => x.WorldId).HasColumnName("world_id");
            entity.Property(x => x.InstanceId).HasColumnName("instance_id");
            entity.Property(x => x.InstanceName).HasColumnName("instance_name");
            entity.Property(x => x.Type).HasColumnName("type");
            entity.Property(x => x.Region).HasColumnName("region");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.GroupId).HasColumnName("group_id");
            entity.Property(x => x.GroupAccessType).HasColumnName("group_access_type");
            entity.Property(x => x.CanRequestInvite).HasColumnName("can_request_invite");
            entity.Property(x => x.AgeGate).HasColumnName("age_gate");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.Capacity).HasColumnName("capacity");
            entity.Property(x => x.QueueEnabled).HasColumnName("queue_enabled");
            entity.Property(x => x.Tags).HasColumnName("tags");
            entity.Property(x => x.Permanent).HasColumnName("permanent");
            entity.Property(x => x.ClientNumber).HasColumnName("client_number");
            entity.Property(x => x.ClosedAt).HasColumnName("closed_at");
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.World).WithMany(w => w.Instances).HasForeignKey(x => x.WorldId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_instances_world_id");
            entity.HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_instances_owner_user_id");
            entity.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_instances_group_id");
            entity.HasIndex(x => new { x.WorldId, x.FirstSeenAt });
            entity.HasIndex(x => x.GroupId);
            entity.HasIndex(x => x.OwnerUserId);
        });

        modelBuilder.Entity<WorldMemo>(entity =>
        {
            entity.ToTable("world_memos");
            entity.HasKey(x => x.WorldId).HasName("world_memos_pkey");

            entity.Property(x => x.WorldId).HasColumnName("world_id");
            entity.Property(x => x.Memo).HasColumnName("memo");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(x => x.World).WithOne().HasForeignKey<WorldMemo>(x => x.WorldId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_world_memos_world_id");
        });

        modelBuilder.Entity<AvatarMemo>(entity =>
        {
            entity.ToTable("avatar_memos");
            entity.HasKey(x => x.AvatarId).HasName("avatar_memos_pkey");

            entity.Property(x => x.AvatarId).HasColumnName("avatar_id");
            entity.Property(x => x.Memo).HasColumnName("memo");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(x => x.Avatar).WithOne().HasForeignKey<AvatarMemo>(x => x.AvatarId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_avatar_memos_avatar_id");
        });

        modelBuilder.Entity<AvatarTag>(entity =>
        {
            entity.ToTable("avatar_tags");
            entity.HasKey(x => new { x.AvatarId, x.Tag }).HasName("avatar_tags_pkey");

            entity.Property(x => x.AvatarId).HasColumnName("avatar_id");
            entity.Property(x => x.Tag).HasColumnName("tag");
            entity.Property(x => x.Color).HasColumnName("color");

            entity.HasOne(x => x.Avatar).WithMany().HasForeignKey(x => x.AvatarId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_avatar_tags_avatar_id");
        });

        modelBuilder.Entity<AvatarUsage>(entity =>
        {
            entity.ToTable("avatar_usages");
            entity.HasKey(x => new { x.OwnerUserId, x.AvatarId }).HasName("avatar_usages_pkey");

            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.AvatarId).HasColumnName("avatar_id");
            entity.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(x => x.TimeSpent).HasColumnName("time_spent");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_avatar_usages_owner_user_id");
            entity.HasOne(x => x.Avatar).WithMany().HasForeignKey(x => x.AvatarId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_avatar_usages_avatar_id");
            entity.HasIndex(x => new { x.OwnerUserId, x.LastUsedAt });
        });

        modelBuilder.Entity<Group>(entity =>
        {
            entity.ToTable("groups");
            entity.HasKey(x => x.Id).HasName("groups_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.ShortCode).HasColumnName("short_code");
            entity.Property(x => x.Discriminator).HasColumnName("discriminator");
            entity.Property(x => x.Description).HasColumnName("description");
            entity.Property(x => x.IconUrl).HasColumnName("icon_url");
            entity.Property(x => x.BannerUrl).HasColumnName("banner_url");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.Privacy).HasColumnName("privacy");
            entity.Property(x => x.JoinState).HasColumnName("join_state");
            entity.Property(x => x.Rules).HasColumnName("rules");
            entity.Property(x => x.Links).HasColumnName("links");
            entity.Property(x => x.Languages).HasColumnName("languages");
            entity.Property(x => x.Tags).HasColumnName("tags");
            entity.Property(x => x.Badges).HasColumnName("badges");
            entity.Property(x => x.MemberCount).HasColumnName("member_count");
            entity.Property(x => x.IsVerified).HasColumnName("is_verified");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.IsStub).HasColumnName("is_stub");
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_groups_owner_user_id");
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => new { x.ShortCode, x.Discriminator });
        });

        modelBuilder.Entity<GroupRole>(entity =>
        {
            entity.ToTable("group_roles");
            entity.HasKey(x => x.Id).HasName("group_roles_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.GroupId).HasColumnName("group_id");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.Description).HasColumnName("description");
            entity.Property(x => x.IsSelfAssignable).HasColumnName("is_self_assignable");
            entity.Property(x => x.IsManagementRole).HasColumnName("is_management_role");
            entity.Property(x => x.IsAddedOnJoin).HasColumnName("is_added_on_join");
            entity.Property(x => x.DefaultRole).HasColumnName("default_role");
            entity.Property(x => x.RequiresTwoFactor).HasColumnName("requires_two_factor");
            entity.Property(x => x.RequiresPurchase).HasColumnName("requires_purchase");
            entity.Property(x => x.Permissions).HasColumnName("permissions");
            entity.Property(x => x.Order).HasColumnName("order");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.IsStub).HasColumnName("is_stub");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Group).WithMany(g => g.Roles).HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_group_roles_group_id");
            entity.HasIndex(x => x.GroupId);
        });

        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.ToTable("group_members");
            entity.HasKey(x => new { x.GroupId, x.UserId }).HasName("group_members_pkey");

            entity.Property(x => x.GroupId).HasColumnName("group_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.MembershipId).HasColumnName("membership_id");
            entity.Property(x => x.MembershipStatus).HasColumnName("membership_status");
            entity.Property(x => x.Visibility).HasColumnName("visibility");
            entity.Property(x => x.IsRepresenting).HasColumnName("is_representing");
            entity.Property(x => x.ManagerNotes).HasColumnName("manager_notes");
            entity.Property(x => x.JoinedAt).HasColumnName("joined_at");
            entity.Property(x => x.BannedAt).HasColumnName("banned_at");
            entity.Property(x => x.LeftAt).HasColumnName("left_at");
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Group).WithMany(g => g.Members).HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_group_members_group_id");
            entity.HasOne(x => x.User).WithMany(u => u.GroupMemberships).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_group_members_user_id");
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.MembershipId);
        });

        modelBuilder.Entity<GroupMemberRole>(entity =>
        {
            entity.ToTable("group_member_roles");
            entity.HasKey(x => new { x.GroupId, x.UserId, x.RoleId }).HasName("group_member_roles_pkey");

            entity.Property(x => x.GroupId).HasColumnName("group_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.RoleId).HasColumnName("role_id");

            entity.HasOne(x => x.Member).WithMany(m => m.Roles).HasForeignKey(x => new { x.GroupId, x.UserId })
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_group_member_roles_group_id_user_id");
            entity.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_group_member_roles_role_id");
            entity.HasIndex(x => x.RoleId);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(x => x.LogHidden).HasColumnName("log_hidden");
            entity.ToTable("notifications");
            entity.HasKey(x => new { x.OwnerUserId, x.Id }).HasName("notifications_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.Property(x => x.Type).HasColumnName("type");
            entity.Property(x => x.SenderUserId).HasColumnName("sender_user_id");
            entity.Property(x => x.SenderUsername).HasColumnName("sender_username");
            entity.Property(x => x.ReceiverUserId).HasColumnName("receiver_user_id");
            entity.Property(x => x.Title).HasColumnName("title");
            entity.Property(x => x.Message).HasColumnName("message");
            entity.Property(x => x.ImageUrl).HasColumnName("image_url");
            entity.Property(x => x.Link).HasColumnName("link");
            entity.Property(x => x.LinkText).HasColumnName("link_text");
            entity.Property(x => x.Details).HasColumnName("details");
            entity.Property(x => x.Responses).HasColumnName("responses");
            entity.Property(x => x.V2Details).HasColumnName("v2_details").HasColumnType("jsonb");
            entity.Property(x => x.Seen).HasColumnName("seen");
            entity.Property(x => x.Hidden).HasColumnName("hidden");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_notifications_owner_user_id");
            entity.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderUserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_notifications_sender_user_id");
            entity.Property(x => x.Details).HasColumnType("jsonb");
            entity.Property(x => x.Responses).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.OwnerUserId, x.CreatedAt });
            entity.HasIndex(x => new { x.OwnerUserId, x.Type, x.CreatedAt });
        });

        modelBuilder.Entity<Favorite>(entity =>
        {
            entity.ToTable("favorites");
            entity.HasKey(x => x.Id).HasName("favorites_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.Type).HasColumnName("type");
            entity.Property(x => x.TargetId).HasColumnName("target_id");
            entity.Property(x => x.GroupName).HasColumnName("group_name");
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_favorites_owner_user_id");
            entity.HasIndex(x => new { x.OwnerUserId, x.Type, x.TargetId }).IsUnique();
        });

        modelBuilder.Entity<ServerSetting>(entity =>
        {
            entity.ToTable("server_settings");
            entity.HasKey(x => x.Key).HasName("server_settings_pkey");
            entity.Property(x => x.Key).HasColumnName("key");
            entity.Property(x => x.Value).HasColumnName("value");
        });

        modelBuilder.Entity<LegacySetting>(entity =>
        {
            entity.ToTable("legacy_settings");
            entity.HasKey(x => x.Key).HasName("legacy_settings_pkey");
            entity.Property(x => x.Key).HasColumnName("key");
            entity.Property(x => x.Value).HasColumnName("value");
            entity.Property(x => x.ImportedAt).HasColumnName("imported_at");
        });

        modelBuilder.Entity<UnidentifiedVisit>(entity =>
        {
            entity.ToTable("unidentified_visits");
            entity.HasKey(x => x.Id).HasName("unidentified_visits_pkey");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.InstanceLocation).HasColumnName("instance_location");
            entity.Property(x => x.JoinedAt).HasColumnName("joined_at");
            entity.Property(x => x.LeftAt).HasColumnName("left_at");
            entity.HasOne(x => x.Instance).WithMany().HasForeignKey(x => x.InstanceLocation)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_unidentified_visits_instance_location");
            entity.HasIndex(x => new { x.InstanceLocation, x.JoinedAt });
            entity.HasIndex(x => new { x.DisplayName, x.JoinedAt });
        });

        modelBuilder.Entity<ModerationAgainst>(entity =>
        {
            entity.ToTable("moderations_against");
            entity.HasKey(x => new { x.OwnerUserId, x.UserId }).HasName("moderations_against_pkey");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.Block).HasColumnName("block");
            entity.Property(x => x.Mute).HasColumnName("mute");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_moderations_against_owner_user_id");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_moderations_against_user_id");
        });

        modelBuilder.Entity<LocalFavoriteGroup>(entity =>
        {
            entity.ToTable("local_favorite_groups");
            entity.HasKey(x => new { x.OwnerUserId, x.Type, x.Name }).HasName("local_favorite_groups_pkey");

            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.Type).HasColumnName("type");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_local_favorite_groups_owner_user_id");
        });

        modelBuilder.Entity<LocalFavorite>(entity =>
        {
            entity.ToTable("local_favorites");
            entity.HasKey(x => new { x.OwnerUserId, x.Type, x.GroupName, x.TargetId }).HasName("local_favorites_pkey");

            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.Type).HasColumnName("type");
            entity.Property(x => x.GroupName).HasColumnName("group_name");
            entity.Property(x => x.TargetId).HasColumnName("target_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_local_favorites_owner_user_id");
            entity.HasIndex(x => new { x.OwnerUserId, x.TargetId });
        });

        modelBuilder.Entity<PrintFavorite>(entity =>
        {
            entity.ToTable("print_favorites");
            entity.HasKey(x => new { x.OwnerUserId, x.PrintId }).HasName("print_favorites_pkey");

            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.PrintId).HasColumnName("print_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("print_favorites_owner_user_id_fkey");
        });

        modelBuilder.Entity<FavoriteGroup>(entity =>
        {
            entity.ToTable("favorite_groups");
            entity.HasKey(x => x.Id).HasName("favorite_groups_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.Type).HasColumnName("type");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.Visibility).HasColumnName("visibility");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_favorite_groups_owner_user_id");
            entity.HasIndex(x => new { x.OwnerUserId, x.Type, x.Name }).IsUnique();
        });

        modelBuilder.Entity<GameLogEvent>(entity =>
        {
            entity.ToTable("game_log_events");
            entity.HasKey(x => x.Id).HasName("game_log_events_pkey");
            entity.HasIndex(x => x.At, "idx_game_log_events_at").IsDescending();

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.At).HasColumnName("at");
            entity.Property(x => x.InstanceLocation).HasColumnName("instance_location");
            entity.Property(x => x.Kind).HasColumnName("kind");
            entity.Property(x => x.Data).HasColumnName("data");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");

            entity.HasOne(x => x.Instance).WithMany().HasForeignKey(x => x.InstanceLocation)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_game_log_events_instance_location");
        });

        modelBuilder.Entity<GameLogHiddenEntry>(entity =>
        {
            entity.ToTable("game_log_hidden_entries");
            entity.HasKey(x => new { x.OwnerUserId, x.EntryKey }).HasName("game_log_hidden_entries_pkey");

            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.EntryKey).HasColumnName("entry_key");
            entity.Property(x => x.HiddenAt).HasColumnName("hidden_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_game_log_hidden_entries_owner_user_id");
        });

        modelBuilder.Entity<VideoPlay>(entity =>
        {
            entity.ToTable("video_plays");
            entity.HasKey(x => x.Id).HasName("video_plays_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.At).HasColumnName("at");
            entity.Property(x => x.InstanceLocation).HasColumnName("instance_location");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.VideoUrl).HasColumnName("video_url");
            entity.Property(x => x.VideoName).HasColumnName("video_name");
            entity.Property(x => x.VideoId).HasColumnName("video_id");

            entity.HasOne(x => x.Instance).WithMany().HasForeignKey(x => x.InstanceLocation)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_video_plays_instance_location");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_video_plays_user_id");
            entity.HasIndex(x => x.At);
            entity.HasIndex(x => new { x.At, x.VideoUrl }).IsUnique();
        });

        modelBuilder.Entity<StickerSpawn>(entity =>
        {
            entity.ToTable("sticker_spawns");
            entity.HasKey(x => x.Id).HasName("sticker_spawns_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.At).HasColumnName("at");
            entity.Property(x => x.InstanceLocation).HasColumnName("instance_location");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.InventoryId).HasColumnName("inventory_id");

            entity.HasOne(x => x.Instance).WithMany().HasForeignKey(x => x.InstanceLocation)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_sticker_spawns_instance_location");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_sticker_spawns_user_id");
            entity.HasIndex(x => x.At);
        });

        modelBuilder.Entity<MutualFriendLink>(entity =>
        {
            entity.ToTable("mutual_friend_links");
            entity.HasKey(x => x.Id).HasName("mutual_friend_links_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.FriendUserId).HasColumnName("friend_user_id");
            entity.Property(x => x.MutualUserId).HasColumnName("mutual_user_id");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_mutual_friend_links_owner_user_id");
            entity.HasOne(x => x.Friend).WithMany().HasForeignKey(x => x.FriendUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_mutual_friend_links_friend_user_id");
            entity.HasOne(x => x.Mutual).WithMany().HasForeignKey(x => x.MutualUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_mutual_friend_links_mutual_user_id");
            entity.HasIndex(x => new { x.OwnerUserId, x.FriendUserId, x.MutualUserId }).IsUnique();
            entity.HasIndex(x => new { x.OwnerUserId, x.MutualUserId });
        });

        modelBuilder.Entity<MutualGraphMeta>(entity =>
        {
            entity.ToTable("mutual_graph_meta");
            entity.HasKey(x => new { x.OwnerUserId, x.FriendUserId }).HasName("mutual_graph_meta_pkey");

            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.FriendUserId).HasColumnName("friend_user_id");
            entity.Property(x => x.LastFetchedAt).HasColumnName("last_fetched_at");
            entity.Property(x => x.OptedOut).HasColumnName("opted_out");

            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_mutual_graph_meta_owner_user_id");
            entity.HasOne(x => x.Friend).WithMany().HasForeignKey(x => x.FriendUserId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_mutual_graph_meta_friend_user_id");
        });

        modelBuilder.Entity<ResourceLoad>(entity =>
        {
            entity.ToTable("resource_loads");
            entity.HasKey(x => x.Id).HasName("resource_loads_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.At).HasColumnName("at");
            entity.Property(x => x.InstanceLocation).HasColumnName("instance_location");
            entity.Property(x => x.Url).HasColumnName("url");
            entity.Property(x => x.Kind).HasColumnName("kind");

            entity.HasOne(x => x.Instance).WithMany().HasForeignKey(x => x.InstanceLocation)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_resource_loads_instance_location");
            entity.HasIndex(x => x.At);
            entity.HasIndex(x => new { x.At, x.Url }).IsUnique();
        });

        modelBuilder.Entity<CachedImage>(entity =>
        {
            entity.ToTable("cached_images");
            entity.HasKey(x => x.Url).HasName("cached_images_pkey");

            entity.Property(x => x.Url).HasColumnName("url");
            entity.Property(x => x.Sha256).HasColumnName("sha256");
            entity.Property(x => x.ContentType).HasColumnName("content_type");
            entity.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            entity.Property(x => x.FetchedAt).HasColumnName("fetched_at");
        });

        modelBuilder.Entity<GameLogClient>(entity =>
        {
            entity.ToTable("game_log_clients");
            entity.HasKey(x => x.ClientId).HasName("game_log_clients_pkey");

            entity.Property(x => x.ClientId).HasColumnName("client_id");
            entity.Property(x => x.ClientName).HasColumnName("client_name");
            entity.Property(x => x.AccountId).HasColumnName("account_id");
            entity.Property(x => x.LastEntryAt).HasColumnName("last_entry_at");
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");

            entity.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_game_log_clients_account_id");
        });

        modelBuilder.Entity<PortalSpawn>(entity =>
        {
            entity.ToTable("portal_spawns");
            entity.HasKey(x => x.Id).HasName("portal_spawns_pkey");

            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.At).HasColumnName("at");
            entity.Property(x => x.InstanceLocation).HasColumnName("instance_location");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.TargetInstanceLocation).HasColumnName("target_instance_location");
            entity.Property(x => x.TargetWorldName).HasColumnName("target_world_name");

            entity.HasOne(x => x.Instance).WithMany().HasForeignKey(x => x.InstanceLocation)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_portal_spawns_instance_location");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_portal_spawns_user_id");
            entity.HasIndex(x => x.At);
        });

        modelBuilder.Entity<SavedCredential>(entity =>
        {
            entity.ToTable("saved_credentials", t => t.HasCheckConstraint("ck_saved_credentials_protection",
                $"protection IN ('{CredentialProtection.DataProtection}', '{CredentialProtection.PrimaryPassword}')"));
            entity.HasKey(x => x.AccountId).HasName("saved_credentials_pkey");

            entity.Property(x => x.AccountId).HasColumnName("account_id");
            entity.Property(x => x.Username).HasColumnName("username");
            entity.Property(x => x.Protection).HasColumnName("protection");
            entity.Property(x => x.Secret).HasColumnName("secret");
            entity.Property(x => x.Salt).HasColumnName("salt");
            entity.Property(x => x.Nonce).HasColumnName("nonce");
            entity.Property(x => x.Iterations).HasColumnName("iterations");
            entity.Property(x => x.SavedAt).HasColumnName("saved_at");

            entity.HasOne(x => x.Account).WithOne().HasForeignKey<SavedCredential>(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_saved_credentials_account_id");
        });
    }
}

internal static class NulStripping
{
    public static string Strip(string value) => value.Contains(' ') ? value.Replace(" ", "", StringComparison.Ordinal) : value;
}

internal sealed class NulStrippingStringConverter() : ValueConverter<string, string>(v => NulStripping.Strip(v), v => v);

internal sealed class NulStrippingStringArrayConverter() : ValueConverter<string[], string[]>(
    v => v.Select(NulStripping.Strip).ToArray(), v => v);

internal sealed class StringArrayComparer() : ValueComparer<string[]>(
    (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
    v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
    v => v.ToArray());
