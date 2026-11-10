using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace VRCX.API.IntegrationTests.Tests;

// VRC+ invites with a picture, and invite requests with a saved message, with VRChat faked.
public sealed class InvitePhotoTests
{
    private const string Account = "usr_photo_account";
    private const string Friend = "usr_photo_friend";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat.Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Photo","username":"photo"}""");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "photo", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    private static MultipartFormDataContent Form(params (string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "image", "image.png");
        foreach (var (name, value) in fields) form.Add(new StringContent(value), name);
        return form;
    }

    [Test]
    public async Task An_invite_photo_goes_to_the_photo_endpoint_with_its_data()
    {
        Factory.Vrchat.Json($"invite/{Friend}/photo", "{}");
        using var client = await LoginAsync();

        var response = await client.PostAsync($"/1/accounts/{Account}/invites/{Friend}/photo", Form(("location", "wrld_x:1"), ("worldName", "World"), ("messageSlot", "3")));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Calls).Contains($"POST invite/{Friend}/photo");
        await Assert.That(Factory.Vrchat.Bodies[$"POST invite/{Friend}/photo"]).Contains("\"messageSlot\":3");
    }

    [Test]
    public async Task An_invite_request_can_carry_a_saved_message()
    {
        Factory.Vrchat.Json($"requestInvite/{Friend}", "{}");
        using var client = await LoginAsync();

        var withSlot = await client.PostAsJsonAsync($"/1/accounts/{Account}/invite-requests/{Friend}", new { requestSlot = 2 });
        var body = Factory.Vrchat.Bodies[$"POST requestInvite/{Friend}"];
        var plain = await client.PostAsync($"/1/accounts/{Account}/invite-requests/{Friend}", null);

        await Assert.That(withSlot.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(body).Contains("\"requestSlot\":2");
        await Assert.That(plain.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task An_invite_answer_with_a_photo_also_hides_the_invite()
    {
        const string notification = "not_photo_answer";
        Factory.Vrchat
            .Json($"invite/{notification}/response/photo", "{}")
            .Json($"auth/user/notifications/{notification}/hide", "{}");
        using var client = await LoginAsync();

        var response = await client.PostAsync($"/1/accounts/{Account}/notifications/{notification}/invite-response/photo", Form(("responseSlot", "1")));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Factory.Vrchat.Calls).Contains($"POST invite/{notification}/response/photo");
        await Assert.That(Factory.Vrchat.Calls).Contains($"PUT auth/user/notifications/{notification}/hide");
    }
}
