using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VRCX.Testing;
using static VRCX.API.IntegrationTests.Tests.WorldEditingTests;

namespace VRCX.API.IntegrationTests.Tests;

// The avatar pop-up's editing: Change Image, SetAvatarTagsDialog, SetAvatarStylesDialog, the gallery
// and the listings, each checked against exactly what the original sent to VRChat.
public sealed class AvatarEditingTests
{
    private const string Prefix = "https://api.vrchat.cloud/api/1";

    [Test]
    public async Task Changing_an_avatar_image_runs_the_same_flow_against_the_avatar()
    {
        await using var app = await StartAsync();
        app.Vrchat.Avatar("avtr_img", "Pic", a => a.Author("usr_me", "Me"));
        var uploads = FakeFileApi(app.Vrchat, "file_avatar", 5);
        app.Vrchat.Json("PUT", "avatars/avtr_img", $$"""{"id":"avtr_img","name":"Pic","authorId":"usr_me","imageUrl":"{{Prefix}}/file/file_avatar/5/file"}""");

        var response = await app.Api.PostAsync(app.AccountPath("avatars/avtr_img/image"), ImageForm($"{Prefix}/file/file_avatar/4/file"));

        await VrcxScenario.EnsureSuccessAsync(response, "change avatar image");
        await AssertFileFlowAsync(app, uploads, "file_avatar", 5, "PUT avatars/avtr_img", "avtr_img");
    }

    [Test]
    public async Task Styles_and_author_tags_send_style_ids_and_the_tags()
    {
        await using var app = await StartAsync();
        app.Vrchat.Avatar("avtr_s", "Styled", a => a.Author("usr_me", "Me"));
        app.Vrchat.Json("PUT", "avatars/avtr_s", """{"id":"avtr_s","name":"Styled","authorId":"usr_me"}""");

        var response = await app.Api.PutAsJsonAsync(app.AccountPath("avatars/avtr_s"), new
        {
            primaryStyle = "styl_1",
            secondaryStyle = "",
            tags = new[] { "content_horror", "author_tag_cute" },
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.Bodies["PUT avatars/avtr_s"]).IsEqualTo(
            """{"id":"avtr_s","primaryStyle":"styl_1","secondaryStyle":"","tags":["content_horror","author_tag_cute"]}""");
    }

    [Test]
    public async Task Content_tags_send_the_id_and_tags()
    {
        await using var app = await StartAsync();
        app.Vrchat.Avatar("avtr_c", "Tagged", a => a.Author("usr_me", "Me"));
        app.Vrchat.Json("PUT", "avatars/avtr_c", """{"id":"avtr_c","name":"Tagged","authorId":"usr_me"}""");

        await app.Api.PutAsJsonAsync(app.AccountPath("avatars/avtr_c"), new { tags = new[] { "content_gore", "content_spooky", "author_tag_x" } });

        await Assert.That(app.Vrchat.Bodies["PUT avatars/avtr_c"]).IsEqualTo("""{"id":"avtr_c","tags":["content_gore","content_spooky","author_tag_x"]}""");
    }

    [Test]
    public async Task Avatar_styles_come_from_vrchat()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("GET", "avatarStyles", """[{"id":"styl_1","styleName":"Anime"}]""");

        var styles = await app.GetJsonAsync(app.AccountPath("avatar-styles"));

        await Assert.That(styles[0].GetProperty("styleName").GetString()).IsEqualTo("Anime");
    }

    [Test]
    public async Task The_gallery_is_read_by_its_tag_and_gallery_id()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("GET", "files?tag=avatargallery&galleryId=avtr_g&n=100&offset=0", """[{"id":"file_a","order":1,"versions":[{"version":1,"file":{"url":"https://img/a"}}]}]""");

        var files = await app.GetJsonAsync(app.AccountPath("avatars/avtr_g/gallery"));

        await Assert.That(files[0].GetProperty("id").GetString()).IsEqualTo("file_a");
        await Assert.That(app.Vrchat.Calls).Contains("GET files?tag=avatargallery&galleryId=avtr_g&n=100&offset=0");
    }

    [Test]
    public async Task A_gallery_image_is_uploaded_with_the_gallery_tag_and_avatar()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("POST", "file/image", """{"id":"file_new","name":"g","versions":[]}""");
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(Png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "file", "blob");

        var response = await app.Api.PostAsync(app.AccountPath("avatars/avtr_g/gallery"), form);

        await VrcxScenario.EnsureSuccessAsync(response, "gallery upload");
        var body = app.Vrchat.Bodies["POST file/image"];
        var tag = body.IndexOf("avatargallery", StringComparison.Ordinal);
        var gallery = body.IndexOf("galleryId", StringComparison.Ordinal);
        var file = body.IndexOf("filename=blob", StringComparison.Ordinal);
        // The original's upload: its postData fields, then the file.
        await Assert.That(tag).IsGreaterThan(-1);
        await Assert.That(gallery).IsGreaterThan(tag);
        await Assert.That(body).Contains("avtr_g");
        await Assert.That(file).IsGreaterThan(gallery);
    }

    [Test]
    public async Task Moving_a_gallery_image_sends_the_new_order()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("PUT", "files/order", "{}");

        var response = await app.Api.PutAsJsonAsync(app.AccountPath("avatars/avtr_g/gallery/order"), new { ids = new[] { "file_b", "file_a" } });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.Bodies["PUT files/order"]).IsEqualTo("""{"ids":["file_b","file_a"],"galleryId":"avtr_g"}""");
    }

    [Test]
    public async Task Owned_listings_come_from_the_purchases_and_balance()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Json("GET", "economy/purchases", """[{"purchaseId":"p","listingId":"prod_l","purchaseActive":true,"buyerId":"usr_me","products":[{"id":"prod_x"}]}]""")
            .Json("GET", "user/usr_me/economy/balance", """{"balance":250}""");

        var owned = await app.GetJsonAsync(app.AccountPath("purchases/owned"));

        await Assert.That(owned.GetProperty("purchases")[0].GetProperty("products")[0].GetProperty("id").GetString()).IsEqualTo("prod_x");
        await Assert.That(owned.GetProperty("balance").GetInt32()).IsEqualTo(250);
        await Assert.That(app.Vrchat.Calls).Contains("GET economy/purchases?buyerId=usr_me&order=desc&sort=purchaseDate&n=50&offset=0");
    }

    [Test]
    public async Task Live_avatar_carries_its_listings_product_and_package()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("GET", "avatars/avtr_l", """{"id":"avtr_l","name":"L","authorId":"usr_x","productId":"prod_x","publishedListings":[{"listingId":"prod_l","displayName":"L","priceTokens":500,"imageId":"file_li"}],"unityPackageUrl":"https://api.vrchat.cloud/api/1/file/file_p/1/file"}""");

        var details = await app.GetJsonAsync(app.AccountPath("avatars/avtr_l/details"));
        var avatar = details.GetProperty("avatar");

        await Assert.That(avatar.GetProperty("productId").GetString()).IsEqualTo("prod_x");
        await Assert.That(avatar.GetProperty("publishedListings")[0].GetProperty("priceTokens").GetInt32()).IsEqualTo(500);
        await Assert.That(avatar.GetProperty("unityPackageUrl").GetString()).IsEqualTo("https://api.vrchat.cloud/api/1/file/file_p/1/file");
    }
}
