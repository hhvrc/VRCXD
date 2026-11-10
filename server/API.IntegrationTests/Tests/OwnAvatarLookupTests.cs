using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's refreshUserDialogAvatars(fileId): a picture of one of the account's own uploads the
// server has not stored yet is found in the own avatars read again from VRChat.
public sealed class OwnAvatarLookupTests
{
    private const string File = "file_0a1b2c3d-7777-8888-9999-444455556666";

    [Test]
    public async Task An_own_avatar_not_stored_yet_is_found_from_the_own_avatars()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Avatar("avtr_mine", "Mine", a => a
            .Set("authorId", app.AccountId)
            .Set("imageUrl", $"https://api.vrchat.cloud/api/1/file/{File}/1/file")
            .Set("thumbnailImageUrl", $"https://api.vrchat.cloud/api/1/image/{File}/1/256"));
        app.Vrchat.Json($"file/{File}", $$"""{"id":"{{File}}","ownerId":"{{app.AccountId}}","name":"Avatar - Mine - Image - 2024","tags":[]}""");

        var found = await app.GetJsonAsync(app.AccountPath($"avatars/lookup?imageUrl={Uri.EscapeDataString($"https://api.vrchat.cloud/api/1/image/{File}/1/256")}&userId=usr_wearer"));

        await Assert.That(found.GetProperty("avatarId").GetString()).IsEqualTo("avtr_mine");
        await Assert.That(found.GetProperty("ownerId").GetString()).IsEqualTo(app.AccountId);
    }

    [Test]
    public async Task An_own_upload_that_is_gone_leaves_only_the_owner()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Json($"file/{File}", $$"""{"id":"{{File}}","ownerId":"{{app.AccountId}}","name":"Avatar - Gone - Image - 2024","tags":[]}""");

        var found = await app.GetJsonAsync(app.AccountPath($"avatars/lookup?imageUrl={Uri.EscapeDataString($"https://api.vrchat.cloud/api/1/image/{File}/1/256")}&userId=usr_wearer"));

        await Assert.That(found.TryGetProperty("avatarId", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String).IsFalse();
        await Assert.That(found.GetProperty("ownerId").GetString()).IsEqualTo(app.AccountId);
    }
}
