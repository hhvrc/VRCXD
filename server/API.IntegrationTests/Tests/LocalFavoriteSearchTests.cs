using VRCX.Common.VrcxDb;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream issue 735: the favorites search left out local favorites and author names. The local list
// now carries the author name and tags the search matches on.
public sealed class LocalFavoriteSearchTests
{
    [Test]
    public async Task Local_world_favorites_carry_the_author_and_tags()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.CreateDbContext())
        {
            db.Worlds.Add(new World { Id = "wrld_735", Name = "Cozy Cabin", AuthorName = "Builder", Tags = ["author_tag_chill", "system_approved"], FirstSeenAt = now });
            db.LocalFavorites.Add(new LocalFavorite { OwnerUserId = app.AccountId!, Type = FavoriteType.World, GroupName = "Mine", TargetId = "wrld_735", CreatedAt = now });
            await db.SaveChangesAsync();
        }

        var local = (await app.GetJsonAsync(app.AccountPath("favorites/local?type=World"))).EnumerateArray().Single();

        await Assert.That(local.GetProperty("targetName").GetString()).IsEqualTo("Cozy Cabin");
        await Assert.That(local.GetProperty("authorName").GetString()).IsEqualTo("Builder");
        await Assert.That(local.GetProperty("tags").EnumerateArray().Select(t => t.GetString())).Contains("author_tag_chill");
    }

    // Upstream issue 984: the export's Author ID column was empty for local favorites.
    [Test]
    public async Task Local_avatar_favorites_carry_the_author_id()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = app.CreateDbContext())
        {
            db.Users.Add(new User { Id = "usr_984_maker", DisplayName = "Maker", FirstSeenAt = now });
            db.Avatars.Add(new Avatar { Id = "avtr_984", Name = "Fox", AuthorId = "usr_984_maker", AuthorName = "Maker", ReleaseStatus = "private", FirstSeenAt = now });
            db.LocalFavorites.Add(new LocalFavorite { OwnerUserId = app.AccountId!, Type = FavoriteType.Avatar, GroupName = "Mine", TargetId = "avtr_984", CreatedAt = now });
            await db.SaveChangesAsync();
        }

        var local = (await app.GetJsonAsync(app.AccountPath("favorites/local?type=Avatar"))).EnumerateArray().Single();

        await Assert.That(local.GetProperty("authorId").GetString()).IsEqualTo("usr_984_maker");
        await Assert.That(local.GetProperty("authorName").GetString()).IsEqualTo("Maker");
        await Assert.That(local.GetProperty("releaseStatus").GetString()).IsEqualTo("private");
    }
}
