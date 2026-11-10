using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.Services.Live;
using VRCX.Common.Services.Presence;
using VRCX.Common.VrcxDb;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;
using VRCX.Vrchat.Pipeline;

namespace VRCX.Common.Tests;

// The store against a real Postgres: what gets written, and - just as
// important - what does not.
public class StoreTests
{
    [ClassDataSource<PostgresFixture>(Shared = SharedType.PerTestSession)]
    public required PostgresFixture Postgres { get; init; }

    private readonly InMemoryPresenceCache Presence = new();
    private VrchatStore Store => field ??= new(NullLogger<VrchatStore>.Instance, Presence, new FakeAvatarImages());

    /// <summary>Every image is an avatar image published by usr_author and named after its URL.</summary>
    private sealed class FakeAvatarImages : IAvatarImageResolver
    {
        public Task<AvatarImageInfo?> ResolveAsync(string imageUrl, CancellationToken ct) =>
            Task.FromResult<AvatarImageInfo?>(new AvatarImageInfo("usr_author", "Avatar " + imageUrl[^1], null));
    }
    private static readonly DateTimeOffset T0 = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static Vrchat.Models.User ApiUser(string id, string name, string status = "active", string? bio = "hi", string avatar = "https://img/1", params string[] tags) => new()
    {
        Id = id, DisplayName = name, Status = status, Bio = bio ?? "", Tags = tags,
        CurrentAvatarImageUrl = avatar, CurrentAvatarThumbnailImageUrl = avatar + "/thumb", IconUrl = avatar,
    };

    private static CurrentUser Me(params string[] online) => new() { Id = "usr_me", DisplayName = "Me", OnlineFriends = online };

    private static LimitedUserFriend Friend(string id, string name, string location = "offline") =>
        new() { Id = id, DisplayName = name, Location = location, Tags = ["system_trust_basic"] };

    private async Task<Account> NewAccountAsync(VrcxContext db)
    {
        await Store.UpsertCurrentUserAsync(db, Me(), T0, CancellationToken.None);
        var account = new Account { UserId = "usr_me", LoggedInAt = T0 };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    [Test]
    public async Task First_sighting_writes_no_history_but_a_later_change_does()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);

        await using (var db = dbs.CreateDbContext())
        {
            await Store.UpsertUserAsync(db, ApiUser("usr_a", "Alice", tags: "system_trust_known"), T0, CancellationToken.None);
            await db.SaveChangesAsync();
        }
        await using (var db = dbs.CreateDbContext())
        {
            // Same values again: nothing new.
            await Store.UpsertUserAsync(db, ApiUser("usr_a", "Alice", tags: "system_trust_known"), T0.AddMinutes(1), CancellationToken.None);
            await db.SaveChangesAsync();
            // Then everything changes at once.
            await Store.UpsertUserAsync(db, ApiUser("usr_a", "Alicia", status: "busy", bio: "new bio", avatar: "https://img/2", tags: "system_trust_trusted"), T0.AddMinutes(2), CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using var check = dbs.CreateDbContext();
        var user = await check.Users.SingleAsync(u => u.Id == "usr_a");
        await Assert.That((user.DisplayName, user.TrustLevel, user.Status, user.Bio, user.IsStub)).IsEqualTo(("Alicia", TrustLevel.KnownUser, "busy", "new bio", false));

        var names = await check.UserDisplayNameChanges.ToListAsync();
        await Assert.That(names.Count).IsEqualTo(1);
        await Assert.That((names[0].PreviousDisplayName, names[0].DisplayName)).IsEqualTo(("Alice", "Alicia"));
        await Assert.That(await check.UserTrustChanges.CountAsync()).IsEqualTo(1);
        // Status changes to or from "offline" are presence, not a status choice, and are not history.
        await Assert.That(await check.UserStatusChanges.CountAsync()).IsEqualTo(1);
        await Assert.That(await check.UserBioChanges.CountAsync()).IsEqualTo(1);
        var avatar = await check.UserAvatarChanges.SingleAsync();
        await Assert.That((avatar.PreviousImageUrl, avatar.ImageUrl, avatar.AvatarName, avatar.AvatarOwnerId)).IsEqualTo(("https://img/1", "https://img/2", "Avatar 2", "usr_author"));
    }

    [Test]
    public async Task Blank_bios_in_user_payloads_say_nothing_and_the_profile_decides()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using var db = dbs.CreateDbContext();

        // VRChat's user payloads carry a blank bio: nothing is known yet.
        await Store.UpsertUserAsync(db, ApiUser("usr_p", "Pat", bio: ""), T0, CancellationToken.None);
        await db.SaveChangesAsync();
        var user = await db.Users.SingleAsync(u => u.Id == "usr_p");
        await Assert.That(user.Bio).IsNull();

        // The first profile read sets it quietly.
        await Store.ApplyProfileAsync(db, "usr_p", new Vrchat.Models.PublicProfile { Id = "usr_p", Bio = "dc: pat", BioLinks = ["https://a"] }, T0.AddMinutes(1), CancellationToken.None);
        await db.SaveChangesAsync();
        await Assert.That(user.Bio).IsEqualTo("dc: pat");
        await Assert.That(user.ProfileFetchedAt).IsEqualTo(T0.AddMinutes(1));
        await Assert.That(await db.UserBioChanges.CountAsync()).IsEqualTo(0);

        // Another blank payload must not wipe it or log a change.
        await Store.UpsertUserAsync(db, ApiUser("usr_p", "Pat", bio: ""), T0.AddMinutes(2), CancellationToken.None);
        await db.SaveChangesAsync();
        await Assert.That(user.Bio).IsEqualTo("dc: pat");
        await Assert.That(user.BioLinks).IsEquivalentTo(new[] { "https://a" });
        await Assert.That(await db.UserBioChanges.CountAsync()).IsEqualTo(0);

        // A later difference on the profile is a change, including clearing it.
        await Store.ApplyProfileAsync(db, "usr_p", new Vrchat.Models.PublicProfile { Id = "usr_p", Bio = "", BioLinks = [] }, T0.AddMinutes(3), CancellationToken.None);
        await db.SaveChangesAsync();
        await Assert.That(user.Bio).IsEqualTo("");
        var change = await db.UserBioChanges.SingleAsync();
        await Assert.That(change.PreviousBio).IsEqualTo("dc: pat");
    }

    [Test]
    public async Task A_stub_user_upgraded_by_a_real_fetch_records_no_changes()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using var db = dbs.CreateDbContext();

        await Store.EnsureUserAsync(db, "usr_s", "Stubby", T0, CancellationToken.None);
        await db.SaveChangesAsync();
        await Store.UpsertUserAsync(db, ApiUser("usr_s", "Real Name", tags: "system_trust_veteran"), T0.AddHours(1), CancellationToken.None);
        await db.SaveChangesAsync();

        await Assert.That(await db.UserDisplayNameChanges.CountAsync()).IsEqualTo(0);
        await Assert.That(await db.UserTrustChanges.CountAsync()).IsEqualTo(0);
        await Assert.That((await db.Users.SingleAsync()).TrustLevel).IsEqualTo(TrustLevel.TrustedUser);
    }

    [Test]
    public async Task Presence_creates_instances_and_opens_and_closes_visits()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using var db = dbs.CreateDbContext();
        var user = await Store.UpsertUserAsync(db, ApiUser("usr_a", "Alice"), T0, CancellationToken.None);

        const string first = "wrld_x:11111~hidden(usr_owner)~region(eu)";
        const string second = "wrld_y:22222~group(grp_g)~groupAccessType(public)~region(us)";
        await Store.ApplyPresenceAsync(db, user, PresenceKind.Online, first, null, "standalonewindows", T0, CancellationToken.None);
        await db.SaveChangesAsync();
        // The same report again is a no-op.
        await Assert.That(await Store.ApplyPresenceAsync(db, user, PresenceKind.Online, first, null, "standalonewindows", T0.AddSeconds(5), CancellationToken.None)).IsNull();
        await Store.ApplyPresenceAsync(db, user, PresenceKind.Location, second, null, null, T0.AddMinutes(10), CancellationToken.None);
        await db.SaveChangesAsync();
        await Store.ApplyPresenceAsync(db, user, PresenceKind.Offline, null, null, null, T0.AddMinutes(20), CancellationToken.None);
        await db.SaveChangesAsync();

        var instances = await db.Instances.OrderBy(i => i.FirstSeenAt).ToListAsync();
        await Assert.That(instances.Select(i => i.Location)).IsEquivalentTo(new[] { first, second });
        await Assert.That((instances[0].Type, instances[0].OwnerUserId, instances[0].Region)).IsEqualTo((InstanceAccessType.Hidden, "usr_owner", "eu"));
        await Assert.That((instances[1].Type, instances[1].GroupId, instances[1].GroupAccessType)).IsEqualTo((InstanceAccessType.Group, "grp_g", "public"));
        // Stubs were created for the world, the instance owner and the group.
        await Assert.That(await db.Worlds.CountAsync(w => w.IsStub)).IsEqualTo(2);
        await Assert.That(await db.Users.AnyAsync(u => u.Id == "usr_owner" && u.IsStub)).IsTrue();
        await Assert.That(await db.Groups.AnyAsync(g => g.Id == "grp_g" && g.IsStub)).IsTrue();

        var visits = await db.InstanceVisits.OrderBy(v => v.JoinedAt).ToListAsync();
        await Assert.That(visits.Count).IsEqualTo(2);
        await Assert.That((visits[0].InstanceLocation, visits[0].LeftAt)).IsEqualTo((first, (DateTimeOffset?)T0.AddMinutes(10)));
        await Assert.That((visits[1].InstanceLocation, visits[1].LeftAt)).IsEqualTo((second, (DateTimeOffset?)T0.AddMinutes(20)));

        var events = await db.PresenceEvents.OrderBy(e => e.At).Select(e => e.Kind).ToListAsync();
        await Assert.That(events).IsEquivalentTo(new[] { PresenceKind.Online, PresenceKind.Location, PresenceKind.Offline });
        var row = await Presence.GetAsync("usr_a", CancellationToken.None);
        await Assert.That((row.State, row.Location, row.CurrentInstanceLocation)).IsEqualTo((UserState.Offline, "offline", (string?)null));
    }

    [Test]
    public async Task A_user_first_seen_in_their_own_instance_saves_in_one_unit_of_work()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using var db = dbs.CreateDbContext();
        await NewAccountAsync(db);

        // Friend list poll: Bob is new to us and sitting in an instance he owns.
        await Store.SyncFriendsAsync(db, "usr_me", Me("usr_bob"), [Friend("usr_bob", "Bob", "wrld_b:5~private(usr_bob)~region(eu)")], new HashSet<string>(), T0, CancellationToken.None);
        await db.SaveChangesAsync();

        var bob = await Presence.GetAsync("usr_bob", CancellationToken.None);
        await Assert.That(bob.CurrentInstanceLocation).IsEqualTo("wrld_b:5~private(usr_bob)~region(eu)");
        await Assert.That((await db.Instances.SingleAsync()).OwnerUserId).IsEqualTo("usr_bob");
    }

    [Test]
    public async Task Nul_characters_from_vrchat_are_stripped_before_postgres_sees_them()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using var db = dbs.CreateDbContext();

        await Store.UpsertGroupAsync(db, new Vrchat.Models.Group { Id = "grp_nul", Name = "Nul Club", Description = "rules here", Links = ["https://a b"] }, T0, CancellationToken.None);
        await Store.UpsertUserAsync(db, ApiUser("usr_nul", "Na me", bio: "bio "), T0, CancellationToken.None);
        await db.SaveChangesAsync();

        var group = await db.Groups.AsNoTracking().SingleAsync(g => g.Id == "grp_nul");
        await Assert.That((group.Name, group.Description, group.Links[0])).IsEqualTo(("NulClub", "ruleshere", "https://ab"));
        await Assert.That((await db.Users.AsNoTracking().SingleAsync(u => u.Id == "usr_nul")).Bio).IsEqualTo("bio");
    }

    [Test]
    public async Task Friend_sync_numbers_new_friends_ends_missing_ones_and_renumbers_on_return()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using (var db = dbs.CreateDbContext())
        {
            await NewAccountAsync(db);
            await Store.SyncFriendsAsync(db, "usr_me", Me("usr_a"), [Friend("usr_a", "Alice", "wrld_x:1~region(eu)"), Friend("usr_b", "Bob")], new HashSet<string>(), T0, CancellationToken.None);
            await db.SaveChangesAsync();
        }
        await using (var db = dbs.CreateDbContext())
        {
            // Bob is gone, Carol is new.
            var result = await Store.SyncFriendsAsync(db, "usr_me", Me(), [Friend("usr_a", "Alice"), Friend("usr_c", "Carol")], new HashSet<string>(), T0.AddHours(1), CancellationToken.None);
            await db.SaveChangesAsync();
            await Assert.That(result.Added).IsEquivalentTo(new[] { "usr_c" });
            await Assert.That(result.Removed).IsEquivalentTo(new[] { "usr_b" });
        }
        await using (var db = dbs.CreateDbContext())
        {
            // Bob is back: a new friendship row with a new number, the old one stays ended.
            await Store.SyncFriendsAsync(db, "usr_me", Me(), [Friend("usr_a", "Alice"), Friend("usr_c", "Carol"), Friend("usr_b", "Bob")], new HashSet<string>(), T0.AddHours(2), CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using var check = dbs.CreateDbContext();
        var all = await check.Friendships.OrderBy(f => f.FriendNumber).ToListAsync();
        await Assert.That(all.Select(f => (f.FriendUserId, f.FriendNumber, f.EndedAt is null)))
            .IsEquivalentTo(new[] { ("usr_a", 1, true), ("usr_b", 2, false), ("usr_c", 3, true), ("usr_b", 4, true) });
        // Alice was online in an instance on the first sync, offline after: presence followed.
        await Assert.That(await check.PresenceEvents.CountAsync(e => e.UserId == "usr_a")).IsEqualTo(2);
        await Assert.That(await check.InstanceVisits.CountAsync(v => v.UserId == "usr_a" && v.LeftAt != null)).IsEqualTo(1);
    }

    [Test]
    public async Task Group_membership_sync_tracks_roles_and_departures()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using var db = dbs.CreateDbContext();
        await NewAccountAsync(db);

        await Store.UpsertGroupAsync(db, new Vrchat.Models.Group
        {
            Id = "grp_1", Name = "Club", ShortCode = "CLUB", Discriminator = "1234", OwnerId = "usr_owner", MemberCount = 2,
            Roles = [new Vrchat.Models.GroupRole { Id = "grol_a", Name = "Admin", IsManagementRole = true }, new Vrchat.Models.GroupRole { Id = "grol_m", Name = "Member", DefaultRole = true }],
            MyMember = new GroupMyMember { Id = "gmem_me", UserId = "usr_me", RoleIds = ["grol_a", "grol_m"], MembershipStatus = "member", IsRepresenting = true },
        }, T0, CancellationToken.None);
        await db.SaveChangesAsync();

        var me = await db.GroupMembers.Include(m => m.Roles).SingleAsync(m => m.UserId == "usr_me");
        await Assert.That(me.Roles.Select(r => r.RoleId)).IsEquivalentTo(new[] { "grol_a", "grol_m" });
        await Assert.That(me.IsRepresenting).IsTrue();

        // A later fetch says the admin role is gone - and a role we have never seen appears as a stub.
        await Store.UpsertMembershipAsync(db, "grp_1", "usr_me", new VrchatStore.MembershipFields { RoleIds = ["grol_m", "grol_new"] }, T0.AddHours(1), CancellationToken.None);
        await db.SaveChangesAsync();
        await Assert.That((await db.GroupMemberRoles.Where(r => r.UserId == "usr_me").Select(r => r.RoleId).ToListAsync())).IsEquivalentTo(new[] { "grol_m", "grol_new" });
        await Assert.That(await db.GroupRoles.AnyAsync(r => r.Id == "grol_new" && r.IsStub)).IsTrue();

        // The user's group list no longer mentions grp_1 but mentions grp_2: left one, joined the other.
        await Store.SyncUserGroupsAsync(db, "usr_me", [new LimitedUserGroup { GroupId = "grp_2", Name = "Other", IsRepresenting = false }], T0.AddHours(2), CancellationToken.None);
        await db.SaveChangesAsync();
        var memberships = await db.GroupMembers.Where(m => m.UserId == "usr_me").OrderBy(m => m.GroupId).ToListAsync();
        await Assert.That(memberships.Select(m => (m.GroupId, m.LeftAt is null))).IsEquivalentTo(new[] { ("grp_1", false), ("grp_2", true) });
        await Assert.That((await db.Groups.SingleAsync(g => g.Id == "grp_2")).Name).IsEqualTo("Other");
    }

    [Test]
    public async Task Pipeline_events_flow_into_presence_friendships_and_notifications()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using (var db = dbs.CreateDbContext()) await NewAccountAsync(db);
        var bus = new LiveEventBus(NullLogger<LiveEventBus>.Instance);
        var ingestor = new PipelineIngestor(dbs, Store, bus, new PresenceDebouncer(dbs, Store, new InMemoryPendingPresenceQueue(), bus, NullLogger<PresenceDebouncer>.Instance), NullLogger<PipelineIngestor>.Instance);

        async Task Send(string frame) => await ingestor.HandleAsync("usr_me", PipelineEvent.Parse(frame), CancellationToken.None);

        await Send("""{"type":"friend-add","content":"{\"userId\":\"usr_a\",\"user\":{\"id\":\"usr_a\",\"displayName\":\"Alice\",\"tags\":[]}}"}""");
        await Send("""{"type":"friend-online","content":"{\"userId\":\"usr_a\",\"location\":\"wrld_x:1~region(eu)\",\"platform\":\"android\",\"user\":{\"id\":\"usr_a\",\"displayName\":\"Alice\",\"tags\":[]}}"}""");
        await Send("""{"type":"friend-update","content":"{\"userId\":\"usr_a\",\"user\":{\"id\":\"usr_a\",\"displayName\":\"Alice!\",\"tags\":[],\"status\":\"busy\"}}"}""");
        await Send("""{"type":"notification","content":"{\"id\":\"not_1\",\"type\":\"invite\",\"senderUserId\":\"usr_a\",\"senderUsername\":\"alice\",\"message\":\"come\",\"details\":\"{\\\"worldId\\\":\\\"wrld_x:1~region(eu)\\\",\\\"worldName\\\":\\\"X\\\"}\",\"created_at\":\"2026-10-31T12:00:00.000Z\"}"}""");
        await Send("""{"type":"see-notification","content":"not_1"}""");
        await Send("""{"type":"friend-offline","content":"{\"userId\":\"usr_a\",\"platform\":\"android\"}"}""");
        await Send("""{"type":"friend-delete","content":"{\"userId\":\"usr_a\"}"}""");

        await using var check = dbs.CreateDbContext();
        var alice = await check.Users.SingleAsync(u => u.Id == "usr_a");
        await Assert.That((alice.DisplayName, alice.Status)).IsEqualTo(("Alice!", "busy"));
        var where = await Presence.GetAsync("usr_a", CancellationToken.None);
        await Assert.That((where.State, where.Platform)).IsEqualTo((UserState.Offline, "android"));
        await Assert.That(await check.UserDisplayNameChanges.CountAsync()).IsEqualTo(1);
        var friendship = await check.Friendships.SingleAsync();
        await Assert.That((friendship.FriendNumber, friendship.EndedAt is not null)).IsEqualTo((1, true));
        var visit = await check.InstanceVisits.SingleAsync();
        await Assert.That((visit.InstanceLocation, visit.LeftAt is not null)).IsEqualTo(("wrld_x:1~region(eu)", true));
        var notification = await check.Notifications.SingleAsync();
        await Assert.That((notification.Version, notification.Type, notification.SenderUserId, notification.Seen)).IsEqualTo((1, "invite", "usr_a", true));
        // The double-encoded details string was unwrapped into real JSON.
        await Assert.That(notification.Details!.RootElement.GetProperty("worldName").GetString()).IsEqualTo("X");
    }

    [Test]
    public async Task A_profile_refresh_and_a_world_refresh_become_live_events()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using var db = dbs.CreateDbContext();
        await Store.UpsertUserAsync(db, ApiUser("usr_live", "Live", bio: ""), T0, CancellationToken.None);
        await Store.ApplyProfileAsync(db, "usr_live", new Vrchat.Models.PublicProfile { Id = "usr_live", Bio = "first", BioLinks = [] }, T0.AddMinutes(1), CancellationToken.None);
        await db.SaveChangesAsync();

        // A scheduled profile refresh that finds a new bio, and a stub world filled in.
        await Store.ApplyProfileAsync(db, "usr_live", new Vrchat.Models.PublicProfile { Id = "usr_live", Bio = "second", BioLinks = [] }, T0.AddMinutes(2), CancellationToken.None);
        await Store.EnsureWorldAsync(db, "wrld_live", "Live World", T0, CancellationToken.None);
        var events = VRCX.Common.Services.Live.LiveChangeInterceptor.Changes(db);

        var user = events.Single(e => e.User is not null).User;
        await Assert.That(user.UserId).IsEqualTo("usr_live");
        await Assert.That(user.Changed).Contains("bio");
        var content = events.Single(e => e.Content is not null).Content;
        await Assert.That((content.Kind, content.Id)).IsEqualTo(("world", "wrld_live"));
    }

    [Test]
    public async Task Queue_ready_and_instance_closed_become_notifications()
    {
        var dbs = await Postgres.NewDatabaseAsync(Presence);
        await using (var seed = dbs.CreateDbContext())
        {
            seed.Accounts.Add(new Account { UserId = "usr_me", User = new VRCX.Common.VrcxDb.User { Id = "usr_me", DisplayName = "Me" } });
            await Store.EnsureWorldAsync(seed, "wrld_q", "Queue World", T0, CancellationToken.None);
            await seed.SaveChangesAsync();
        }
        var bus = new LiveEventBus(NullLogger<LiveEventBus>.Instance);
        var ingestor = new PipelineIngestor(dbs, Store, bus, new PresenceDebouncer(dbs, Store, new InMemoryPendingPresenceQueue(), bus, NullLogger<PresenceDebouncer>.Instance), NullLogger<PipelineIngestor>.Instance);

        await ingestor.HandleAsync("usr_me", PipelineEvent.Parse("""{"type":"instance-queue-ready","content":"{\"instanceLocation\":\"wrld_q:123~group(grp_q)~groupAccessType(public)\"}"}"""), CancellationToken.None);
        await ingestor.HandleAsync("usr_me", PipelineEvent.Parse("""{"type":"instance-closed","content":"{\"instanceLocation\":\"wrld_q:123\"}"}"""), CancellationToken.None);

        await using var check = dbs.CreateDbContext();
        var rows = await check.Notifications.Where(n => n.OwnerUserId == "usr_me").OrderBy(n => n.Type).ToListAsync();
        await Assert.That(rows.Select(n => n.Type)).IsEquivalentTo(["group.queueReady", "instance.closed"]);
        await Assert.That(rows[0].Message).StartsWith("Instance ready to join Queue World");
        await Assert.That(rows[0].Link).IsEqualTo("group:grp_q");
        await Assert.That(rows[1].Message).IsEqualTo("Instance Closed");
    }
}
