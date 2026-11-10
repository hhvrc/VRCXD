using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace VRCX.API.IntegrationTests.Tests;

// The Inventory tool's reads and writes, checked against the fake VRChat.
public sealed class InventoryTests
{
    private const string Account = "usr_inv";
    private const string Base = $"/1/accounts/{Account}";

    [ClassDataSource<IsolatedHost>(Shared = SharedType.None)]
    public required IsolatedHost Factory { get; init; }

    private async Task<HttpClient> LoginAsync()
    {
        Factory.Vrchat
            .Json("auth/user", $$"""{"id":"{{Account}}","displayName":"Inv","username":"inv","status":"active","tags":["system_supporter"],"userIcon":"https://api.vrchat.cloud/api/1/file/file_icon/1","onlineFriends":[],"activeFriends":[],"friendGroupNames":[]}""")
            .Json("auth/user/friends?offline=false&n=100&offset=0", "[]")
            .Json("auth/user/friends?offline=true&n=100&offset=0", "[]")
            .Json($"users/{Account}/groups", "[]")
            .Json("favorite/groups?n=100", "[]")
            .Json("favorites?n=100&offset=0", "[]")
            .Json("auth/user/playermoderations", "[]")
            .Json("userNotes?n=100&offset=0", "[]")
            .Json("auth/user/notifications?n=100&offset=0", "[]")
            .Json("notifications?n=100&offset=0", "[]");
        var client = Factory.CreateAuthenticatedClient();
        var login = await client.PostAsJsonAsync("/1/accounts/login", new { username = "inv", password = "pw" });
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return client;
    }

    private bool Called(string call)
    {
        lock (Factory.Vrchat.Calls) return Factory.Vrchat.Calls.Contains(call);
    }

    private static MultipartFormDataContent Png(string field)
    {
        var image = new ByteArrayContent([1, 2, 3]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { image, field, "blob" } };
    }

    [Test]
    public async Task Files_list_newest_first_upload_and_delete()
    {
        Factory.Vrchat
            .Json("auth/permissions", """[{"name":"permission-user-gallery","data":{"max":100}},{"name":"other","data":{}}]""")
            .Json("files?tag=gallery&n=100", """[{"id":"file_old","versions":[{"version":1,"file":{"url":"https://img/old"}}]},{"id":"file_new","versions":[{"version":1},{"version":2,"file":{"url":"https://img/new"}}]}]""")
            .Json("file/image", """{"id":"file_up","versions":[{"version":1,"file":{"url":"https://img/up"}}]}""")
            .Json("file/file_old", "{}")
            .Json($"profile/{Account}", "{}");
        using var client = await LoginAsync();

        var info = await client.GetFromJsonAsync<JsonElement>($"{Base}/gallery/info");
        await Assert.That(info.GetProperty("isSupporter").GetBoolean()).IsTrue();
        await Assert.That(info.GetProperty("limits").GetProperty("gallery").GetInt32()).IsEqualTo(100);
        await Assert.That(info.GetProperty("limits").GetProperty("icons").GetInt32()).IsEqualTo(18);

        var files = await client.GetFromJsonAsync<JsonElement>($"{Base}/gallery/files/gallery");
        await Assert.That(files[0].GetProperty("id").GetString()).IsEqualTo("file_new");
        await Assert.That(files[0].GetProperty("url").GetString()).IsEqualTo("https://img/new");

        using var form = Png("file");
        form.Add(new StringContent("emoji"), "tag");
        form.Add(new StringContent("4"), "frames");
        var uploaded = await client.PostAsync($"{Base}/gallery/files", form);
        await Assert.That(uploaded.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(Called("POST file/image")).IsTrue();

        await Assert.That((await client.DeleteAsync($"{Base}/gallery/files/file_old")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Called("DELETE file/file_old")).IsTrue();

        var icon = await client.PutAsJsonAsync($"{Base}/gallery/icon", new { fileId = "file_new" });
        var body = await icon.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("userIcon").GetString()).IsEqualTo("https://api.vrchat.cloud/api/1/file/file_new/1");
        await Assert.That(Called($"PUT profile/{Account}")).IsTrue();
    }

    [Test]
    public async Task Prints_keep_their_favorites_here()
    {
        Factory.Vrchat
            .Json($"prints/user/{Account}?n=100", """[{"id":"prnt_a","timestamp":"2026-01-01T00:00:00Z","files":{"image":"https://img/a"}},{"id":"prnt_b","timestamp":"2026-02-01T00:00:00Z","files":{"image":"https://img/b"}}]""")
            .Json("prints", """{"id":"prnt_new"}""")
            .Json("prints/prnt_a", "{}");
        using var client = await LoginAsync();

        await Assert.That((await client.PutAsync($"{Base}/gallery/prints/prnt_a/favorite", null)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await client.PutAsync($"{Base}/gallery/prints/prnt_a/favorite", null)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var list = await client.GetFromJsonAsync<JsonElement>($"{Base}/gallery/prints");
        await Assert.That(list.GetProperty("prints")[0].GetProperty("id").GetString()).IsEqualTo("prnt_b");
        await Assert.That(list.GetProperty("favorites")[0].GetString()).IsEqualTo("prnt_a");

        await Assert.That((await client.DeleteAsync($"{Base}/gallery/prints/prnt_a/favorite")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        list = await client.GetFromJsonAsync<JsonElement>($"{Base}/gallery/prints");
        await Assert.That(list.GetProperty("favorites").GetArrayLength()).IsEqualTo(0);

        using var form = Png("image");
        form.Add(new StringContent("hello"), "note");
        form.Add(new StringContent("2026-11-01T10:00:00"), "timestamp");
        await Assert.That((await client.PostAsync($"{Base}/gallery/prints", form)).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(Called("POST prints")).IsTrue();

        await Assert.That((await client.DeleteAsync($"{Base}/gallery/prints/prnt_a")).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Called("DELETE prints/prnt_a")).IsTrue();
    }

    [Test]
    public async Task Inventory_pages_adds_global_items_and_can_consume_and_redeem()
    {
        Factory.Vrchat
            .Json("inventory?n=100&offset=0&order=newest&notFlags=ugc", """{"data":[{"id":"inv_1","name":"Bundle","itemType":"bundle","created_at":"2026-01-01T00:00:00Z"}]}""")
            .Json("inventory?n=100&offset=100&order=newest&notFlags=ugc", """{"data":[]}""")
            .Json("inventory/global", """[{"id":"inv_g","name":"Global"}]""")
            .Json("inventory/inv_1/consume", "{}")
            .Json("reward/redeem", "[]");
        using var client = await LoginAsync();

        var items = await client.GetFromJsonAsync<JsonElement>($"{Base}/inventory");
        await Assert.That(items.GetArrayLength()).IsEqualTo(2);
        await Assert.That(items[1].GetProperty("id").GetString()).IsEqualTo("inv_g");

        await Assert.That((await client.PostAsync($"{Base}/inventory/inv_1/consume", null)).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Called("PUT inventory/inv_1/consume")).IsTrue();

        await Assert.That((await client.PostAsJsonAsync($"{Base}/inventory/redeem", new { code = " ABC " })).StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(Called("POST reward/redeem")).IsTrue();
    }
}
