using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services.Ingestion;
using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// What the original worked out on the client and the port works out on the server: the avatar
// behind an image (storeAvatarImage), a user by display name (findUserByDisplayName), a friend's
// platform (computeUserPlatform) and the platforms content can be played on (getAvailablePlatforms).
public sealed class StoredLookupTests
{
    private const string FilePath = "file/file_abc123";

    [Test]
    public async Task An_avatar_image_gives_its_owner_name_and_file_date_and_is_asked_for_once()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json(FilePath, """{"id":"file_abc123","name":"Avatar - Cool Robot - Image - 2024.01.01","ownerId":"usr_owner1","tags":[],"versions":[{"version":0,"created_at":"2024-01-01T00:00:00Z"}]}""");
        await app.LoginAsync();
        var resolver = app.Services.GetRequiredService<IAvatarImageResolver>();
        const string url = "https://api.vrchat.cloud/api/1/file/file_abc123/1/file";

        var info = await resolver.ResolveAsync(url, CancellationToken.None);
        var again = await resolver.ResolveAsync(url, CancellationToken.None);

        await Assert.That(info).IsNotNull();
        await Assert.That((info!.OwnerId, info.AvatarName, info.FileCreatedAt)).IsEqualTo(("usr_owner1", "Cool Robot", (DateTimeOffset?)new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await Assert.That(again).IsEqualTo(info);
        await Assert.That(app.Vrchat.CountOf("GET", FilePath)).IsEqualTo(1);
    }

    [Test]
    public async Task An_image_name_off_the_pattern_gives_an_empty_name_and_symbols_around_one_are_kept()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Json("file/file_plain", """{"id":"file_plain","name":"Some upload","ownerId":"usr_owner2","tags":[],"versions":[{"version":0}]}""");
        app.Vrchat.Json("file/file_fancy", """{"id":"file_fancy","name":"Avatar - ★ Fancy (Name) ★ - Image - v1","ownerId":"usr_owner3","tags":[],"versions":[{"version":0}]}""");
        await app.LoginAsync();
        var resolver = app.Services.GetRequiredService<IAvatarImageResolver>();

        var plain = await resolver.ResolveAsync("https://api.vrchat.cloud/api/1/file/file_plain/1/file", CancellationToken.None);
        var fancy = await resolver.ResolveAsync("https://api.vrchat.cloud/api/1/file/file_fancy/1/file", CancellationToken.None);

        await Assert.That((plain!.OwnerId, plain.AvatarName)).IsEqualTo(("usr_owner2", ""));
        await Assert.That(fancy!.AvatarName).Contains("Fancy");
    }

    [Test]
    public async Task A_name_finds_the_friend_first_then_the_latest_stored_user_and_only_with_the_same_case()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_alice_friend", "Alice");
        await app.LoginAsync();
        await using (var db = app.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Users.AddRange(
                new User { Id = "usr_alice_other", DisplayName = "Alice", FirstSeenAt = now, UpdatedAt = now.AddDays(1) },
                new User { Id = "usr_bob_old", DisplayName = "Bob", FirstSeenAt = now, UpdatedAt = now.AddDays(-2) },
                new User { Id = "usr_bob_new", DisplayName = "Bob", FirstSeenAt = now, UpdatedAt = now.AddDays(-1) });
            await db.SaveChangesAsync();
        }

        async Task<string?> LookupAsync(string name)
        {
            var found = await app.GetJsonAsync(app.AccountPath($"users/lookup?displayName={Uri.EscapeDataString(name)}"));
            return found.TryGetProperty("userId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        }

        await Assert.That(await LookupAsync("Alice")).IsEqualTo("usr_alice_friend");
        await Assert.That(await LookupAsync("Bob")).IsEqualTo("usr_bob_new");
        await Assert.That(await LookupAsync("alice")).IsNull();
    }

    [Test]
    public async Task A_friends_platform_is_vrchats_unless_offline_web_or_empty_then_the_last_one()
    {
        await using var app = await VrcxScenario.StartAsync();
        app.Vrchat.Friend("usr_plat_live", "Live", u => u.Set("platform", "android").Set("last_platform", "standalonewindows"));
        app.Vrchat.Friend("usr_plat_web", "Web", u => u.Set("platform", "web").Set("last_platform", "ios"));
        app.Vrchat.Friend("usr_plat_offline", "Offline", u => u.Offline().Set("platform", "offline").Set("last_platform", "android"));
        app.Vrchat.Friend("usr_plat_empty", "Empty", u => u.Set("platform", "").Set("last_platform", "standalonewindows"));
        app.Vrchat.Friend("usr_plat_none", "None", u => u.Set("platform", "").Set("last_platform", ""));
        await app.LoginAsync();

        var friends = (await app.GetJsonAsync(app.AccountPath("friends"))).EnumerateArray().ToDictionary(
            f => f.GetProperty("userId").GetString()!,
            f => f.TryGetProperty("platform", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null);

        await Assert.That(friends["usr_plat_live"]).IsEqualTo("android");
        await Assert.That(friends["usr_plat_web"]).IsEqualTo("ios");
        await Assert.That(friends["usr_plat_offline"]).IsEqualTo("android");
        await Assert.That(friends["usr_plat_empty"]).IsEqualTo("standalonewindows");
        await Assert.That(friends["usr_plat_none"] ?? "").IsEqualTo("");
    }

    [Test]
    public async Task Only_standard_and_security_builds_make_an_avatar_or_world_available_on_a_platform()
    {
        await using var app = await VrcxScenario.StartAsync();
        var me = app.Vrchat.SetMe("usr_platforms", "Platforms");
        app.Vrchat.Avatar("avtr_impostor_only", "Impostor", a => a.Author(me.Id, me.DisplayName).Set("unityPackages", JsonNode.Parse("""
            [
              {"id":"unp_pc","platform":"standalonewindows","variant":"standard"},
              {"id":"unp_q","platform":"android","variant":"impostor"},
              {"id":"unp_ios","platform":"ios","variant":"security"}
            ]
            """)));
        app.Vrchat.World("wrld_variants", "Variants", w => w.Set("unityPackages", JsonNode.Parse("""
            [
              {"id":"unp_w1","platform":"standalonewindows"},
              {"id":"unp_w2","platform":"android","variant":"impostor"}
            ]
            """)));
        await app.LoginAsync();

        var own = await app.GetJsonAsync(app.AccountPath("avatars"));
        var platforms = own[0].GetProperty("avatar").GetProperty("platforms").EnumerateArray().Select(p => p.GetString()!).ToList();
        await Assert.That(platforms).IsEquivalentTo(new[] { "ios", "standalonewindows" });
        var world = await app.GetJsonAsync("/1/worlds/wrld_variants?refresh=true");
        await Assert.That(world.GetProperty("platforms").EnumerateArray().Select(p => p.GetString()!).ToList()).IsEquivalentTo(new[] { "standalonewindows" });
    }
}
