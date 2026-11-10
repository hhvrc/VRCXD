using System.Net;
using VRCX.Vrchat;
using VRCX.Vrchat.Models;
using VRCX.Vrchat.Pipeline;

namespace VRCX.Common.Tests;

// The wire layer on its own: JSON leniency, locations, pipeline frames,
// login flows. No database.
public class VrchatClientTests
{
    [Test]
    public async Task Current_user_json_tolerates_empty_timestamps_and_unknown_fields()
    {
        var user = VrchatJson.Deserialize<CurrentUser>("""
        {"id":"usr_me","displayName":"Me","last_login":"","date_joined":"2019-05-04","status":"join me",
         "tags":["system_trust_veteran","language_eng"],"bioLinks":["https://x"],"somethingNew":{"a":1},
         "onlineFriends":["usr_a"],"last_platform":"standalonewindows","presence":{"world":"wrld_1","instance":"123~private(usr_me)"}}
        """)!;

        await Assert.That(user.LastLogin).IsNull();
        await Assert.That(user.DateJoined).IsEqualTo(new DateOnly(2019, 5, 4));
        await Assert.That(user.Status).IsEqualTo("join me");
        await Assert.That(TrustLevel.FromTags(user.Tags)).IsEqualTo(TrustLevel.TrustedUser);
        await Assert.That(user.OnlineFriends).IsEquivalentTo(new[] { "usr_a" });
        await Assert.That(user.Presence!.World).IsEqualTo("wrld_1");
    }

    [Test]
    public async Task Booleans_that_arrive_as_numbers_or_strings_still_read()
    {
        var notification = VrchatJson.Deserialize<NotificationV2>("""
        {"id":"not_1","type":"group.announcement","seen":"false","expiryAfterSeen":3600,"requireSeen":0,"canDelete":true}
        """)!;
        await Assert.That((notification.Seen, notification.ExpiryAfterSeen, notification.RequireSeen, notification.CanDelete)).IsEqualTo((false, true, false, true));
    }

    [Test]
    public async Task Location_tags_parse_every_access_type()
    {
        var friendsPlus = VrchatLocation.Parse("wrld_a:12345~hidden(usr_b)~region(eu)~nonce(abc)");
        await Assert.That((friendsPlus.WorldId, friendsPlus.InstanceName, friendsPlus.AccessType, friendsPlus.OwnerUserId, friendsPlus.Region))
            .IsEqualTo(("wrld_a", "12345", "hidden", "usr_b", "eu"));
        await Assert.That(friendsPlus.InstanceLocation).IsEqualTo("wrld_a:12345~hidden(usr_b)~region(eu)~nonce(abc)");

        var group = VrchatLocation.Parse("wrld_a:99~group(grp_c)~groupAccessType(plus)~region(us)");
        await Assert.That((group.AccessType, group.GroupId, group.GroupAccessType, group.AccessTypeName)).IsEqualTo(("group", "grp_c", "plus", "Group+"));

        var invitePlus = VrchatLocation.Parse("wrld_a:1~private(usr_b)~canRequestInvite~region(jp)");
        await Assert.That(invitePlus.AccessTypeName).IsEqualTo("Invite+");

        var withShortName = VrchatLocation.Parse("wrld_a:1~private(usr_b)&shortName=xyz");
        await Assert.That((withShortName.ShortName, withShortName.InstanceId)).IsEqualTo(("xyz", "1~private(usr_b)"));

        await Assert.That(VrchatLocation.Parse("offline").IsOffline).IsTrue();
        await Assert.That(VrchatLocation.Parse("private").IsRealInstance).IsFalse();
        await Assert.That(VrchatLocation.Parse("traveling").IsTraveling).IsTrue();
        await Assert.That(VrchatLocation.Parse("wrld_only").WorldId).IsEqualTo("wrld_only");
        await Assert.That(VrchatLocation.Parse("wrld_only").IsRealInstance).IsFalse();
    }

    [Test]
    public async Task Pipeline_frames_decode_double_encoded_content_and_bare_ids()
    {
        var located = PipelineEvent.Parse("""
        {"type":"friend-location","content":"{\"userId\":\"usr_a\",\"location\":\"wrld_x:1~region(eu)\",\"travelingToLocation\":\"\",\"worldId\":\"wrld_x\",\"canRequestInvite\":true,\"user\":{\"id\":\"usr_a\",\"displayName\":\"A\",\"status\":\"active\",\"tags\":[]}}"}
        """);
        var friend = (FriendEvent)located;
        await Assert.That((friend.UserId, friend.Payload.Location, friend.Payload.User!.DisplayName)).IsEqualTo(("usr_a", "wrld_x:1~region(eu)", "A"));

        var seen = (NotificationIdEvent)PipelineEvent.Parse("""{"type":"see-notification","content":"not_123"}""");
        await Assert.That(seen.NotificationId).IsEqualTo("not_123");

        var objectContent = (FriendEvent)PipelineEvent.Parse("""{"type":"friend-offline","content":{"userId":"usr_a","platform":"web"}}""");
        await Assert.That(objectContent.Payload.Platform).IsEqualTo("web");

        var unknown = PipelineEvent.Parse("""{"type":"made-up-event","content":"{\"x\":1}"}""");
        await Assert.That(unknown).IsTypeOf<UnknownEvent>();

        // A payload that does not fit the model degrades to Unknown instead of throwing.
        var broken = PipelineEvent.Parse("""{"type":"friend-online","content":"{\"nope\":true}"}""");
        await Assert.That(broken).IsTypeOf<UnknownEvent>();
    }

    [Test]
    public async Task Login_without_two_factor_returns_the_user()
    {
        var fake = new FakeVrchat().Json("auth/user", """{"id":"usr_me","displayName":"Me"}""");
        using var client = fake.Client();

        var result = await client.LoginAsync("me", "p@ss word", CancellationToken.None);

        var ok = (LoginResult.LoggedIn)result;
        await Assert.That(ok.User.Id).IsEqualTo("usr_me");
    }

    [Test]
    public async Task Login_with_two_factor_then_verify()
    {
        var authenticated = false;
        var fake = new FakeVrchat()
            .Handle("auth/user", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(authenticated
                    ? """{"id":"usr_me","displayName":"Me"}"""
                    : """{"requiresTwoFactorAuth":["totp","otp"]}"""),
            })
            .Handle("auth/twofactorauth/totp/verify", _ => { authenticated = true; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"verified":true}""") }; });
        using var client = fake.Client();

        var first = await client.LoginAsync("me", "pw", CancellationToken.None);
        await Assert.That(first).IsTypeOf<LoginResult.RequiresTwoFactor>();
        await Assert.That(((LoginResult.RequiresTwoFactor)first).IsEmailOtp).IsFalse();

        await Assert.That(await client.VerifyTwoFactorAsync(TwoFactorMethod.Totp, "123456", CancellationToken.None)).IsTrue();
        var me = await client.GetCurrentUserAsync(CancellationToken.None);
        await Assert.That(me.Id).IsEqualTo("usr_me");
    }

    [Test]
    public async Task Bad_credentials_and_dead_sessions_are_distinguishable()
    {
        var fake = new FakeVrchat()
            .Status("auth/user", HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid Username/Email or Password","status_code":401}}""")
            .Status("users/usr_x", HttpStatusCode.Unauthorized, """{"error":{"message":"Missing Credentials","status_code":401}}""")
            .Status("users/usr_gone", HttpStatusCode.NotFound);
        using var client = fake.Client();

        var login = await client.LoginAsync("me", "wrong", CancellationToken.None);
        await Assert.That(((LoginResult.InvalidCredentials)login).Message).IsEqualTo("Invalid Username/Email or Password");

        var dead = await Assert.ThrowsAsync<VrchatUnauthorizedException>(async () => await client.GetUserAsync("usr_x", CancellationToken.None));
        await Assert.That(dead!.VrchatMessage).IsEqualTo("Missing Credentials");

        await Assert.That(await client.GetUserAsync("usr_gone", CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task All_friends_pages_both_lists_until_a_short_page()
    {
        static string Page(int from, int count) =>
            "[" + string.Join(",", Enumerable.Range(from, count).Select(i => $$"""{"id":"usr_{{i}}","displayName":"U{{i}}","tags":[]}""")) + "]";
        var fake = new FakeVrchat()
            .Json("auth/user/friends?offline=false&n=100&offset=0", Page(0, 100))
            .Json("auth/user/friends?offline=false&n=100&offset=100", Page(100, 3))
            .Json("auth/user/friends?offline=true&n=100&offset=0", Page(103, 10));
        using var client = fake.Client();

        var friends = await client.GetAllFriendsAsync(CancellationToken.None);

        await Assert.That(friends.Count).IsEqualTo(113);
        await Assert.That(fake.Calls.Count).IsEqualTo(3);
    }
}
