using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using VRCX.Testing;
using VRCX.Testing.FakeVrchat;
using VRCX.Vrchat;

namespace VRCX.API.IntegrationTests.Tests;

// The world pop-up's editing: SetWorldTagsDialog, WorldAllowedDomainsDialog, the Store tab and Change
// Image (the file and signature upload flow, shared with avatars), each checked against exactly what
// the original sent to VRChat.
public sealed class WorldEditingTests
{
    private const string Prefix = "https://api.vrchat.cloud/api/1";

    internal static async Task<VrcxScenario> StartAsync()
    {
        var app = await VrcxScenario.StartAsync();
        app.Vrchat.SetMe("usr_me", "Me");
        await app.LoginAsync(sync: false);
        return app;
    }

    // ---- worlds -------------------------------------------------------------

    [Test]
    public async Task World_tags_send_the_id_tags_and_disabled_prop_abilities()
    {
        await using var app = await StartAsync();
        app.Vrchat.World("wrld_t", "Tagged", w => w.Author("usr_me", "Me"));
        app.Vrchat.Json("PUT", "worlds/wrld_t", """{"id":"wrld_t","name":"Tagged","authorId":"usr_me"}""");

        var response = await app.Api.PutAsJsonAsync(app.AccountPath("worlds/wrld_t"), new
        {
            tags = new[] { "feature_prints_disabled", "content_gore", "author_tag_cozy" },
            disabledPropAbilities = new[] { "player_movement" },
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.Bodies["PUT worlds/wrld_t"]).IsEqualTo(
            """{"id":"wrld_t","tags":["feature_prints_disabled","content_gore","author_tag_cozy"],"disabledPropAbilities":["player_movement"]}""");
    }

    [Test]
    public async Task Allowed_video_player_domains_send_the_url_list()
    {
        await using var app = await StartAsync();
        app.Vrchat.World("wrld_d", "Domains", w => w.Author("usr_me", "Me"));
        app.Vrchat.Json("PUT", "worlds/wrld_d", """{"id":"wrld_d","name":"Domains","authorId":"usr_me"}""");

        var response = await app.Api.PutAsJsonAsync(app.AccountPath("worlds/wrld_d"), new { urlList = new[] { "youtube.com", "" } });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(app.Vrchat.Bodies["PUT worlds/wrld_d"]).IsEqualTo("""{"id":"wrld_d","urlList":["youtube.com",""]}""");
    }

    [Test]
    public async Task A_rename_sends_the_id_with_the_name_as_the_original()
    {
        await using var app = await StartAsync();
        app.Vrchat.World("wrld_r", "Old", w => w.Author("usr_me", "Me"));
        app.Vrchat.Json("PUT", "worlds/wrld_r", """{"id":"wrld_r","name":"New","authorId":"usr_me"}""");

        await app.Api.PutAsJsonAsync(app.AccountPath("worlds/wrld_r"), new { name = "New" });

        await Assert.That(app.Vrchat.Bodies["PUT worlds/wrld_r"]).IsEqualTo("""{"id":"wrld_r","name":"New"}""");
    }

    [Test]
    public async Task The_store_tab_reads_the_worlds_store_with_hydrated_listings()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("GET", "economy/store?storeId=esto_w&hydrateListings=true&hydrateContext=false",
            """{"id":"esto_w","listings":[{"id":"prod_1","displayName":"Hat","priceTokens":1200}]}""");

        var listings = await app.GetJsonAsync(app.AccountPath("worlds/wrld_s/store?storeId=esto_w"));

        await Assert.That(listings.GetArrayLength()).IsEqualTo(1);
        await Assert.That(listings[0].GetProperty("displayName").GetString()).IsEqualTo("Hat");
    }

    [Test]
    public async Task Live_world_carries_what_the_editing_dialogs_need()
    {
        await using var app = await StartAsync();
        app.Vrchat.Json("GET", "worlds/wrld_f", """{"id":"wrld_f","name":"F","authorId":"usr_me","urlList":["a.com"],"disabledPropAbilities":["player_movement"],"storeId":"esto_f","unityPackageUrl":"https://api.vrchat.cloud/api/1/file/file_pkg/1/file"}""");

        var world = await app.GetJsonAsync(app.AccountPath("worlds/wrld_f/live"));

        await Assert.That(world.GetProperty("urlList")[0].GetString()).IsEqualTo("a.com");
        await Assert.That(world.GetProperty("disabledPropAbilities")[0].GetString()).IsEqualTo("player_movement");
        await Assert.That(world.GetProperty("storeId").GetString()).IsEqualTo("esto_f");
        await Assert.That(world.GetProperty("unityPackageUrl").GetString()).IsEqualTo("https://api.vrchat.cloud/api/1/file/file_pkg/1/file");
    }

    // ---- Change Image ---------------------------------------------------------

    internal static byte[] Png { get; } = Enumerable.Range(0, 3000).Select(i => (byte)(i % 256)).ToArray();

    internal static MultipartFormDataContent ImageForm(string imageUrl)
    {
        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(Png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "image", "image.png");
        form.Add(new StringContent(imageUrl), "imageUrl");
        return form;
    }

    internal sealed record Upload(string ContentType, string? Md5, byte[] Data);

    /// <summary>The file API steps, with the two uploads going to fake addresses under the API so they are recorded.</summary>
    internal static List<Upload> FakeFileApi(FakeVrchatApi vrchat, string fileId, int version)
    {
        var uploads = new List<Upload>();
        HttpResponseMessage Capture(FakeRequest r)
        {
            var content = r.Message.Content!;
            var md5 = content.Headers.ContentMD5 is { } hash ? Convert.ToBase64String(hash) : null;
            uploads.Add(new Upload(content.Headers.ContentType!.MediaType!, md5, content.ReadAsByteArrayAsync().GetAwaiter().GetResult()));
            return FakeVrchatApi.Response(HttpStatusCode.OK, "");
        }
        vrchat
            .Json("POST", $"file/{fileId}", $$"""{"id":"{{fileId}}","versions":[{"version":0},{"version":{{version}}}]}""")
            .Json("PUT", $"file/{fileId}/{version}/file/start", $$"""{"url":"{{Prefix}}/fake-s3/file"}""")
            .Json("PUT", $"file/{fileId}/{version}/file/finish", "{}")
            .Json("PUT", $"file/{fileId}/{version}/signature/start", $$"""{"url":"{{Prefix}}/fake-s3/signature"}""")
            .Json("PUT", $"file/{fileId}/{version}/signature/finish", "{}")
            .On("PUT", "fake-s3/file", Capture)
            .On("PUT", "fake-s3/signature", Capture);
        return uploads;
    }

    internal static async Task AssertFileFlowAsync(VrcxScenario app, List<Upload> uploads, string fileId, int version, string setCall, string entityId)
    {
        var signature = RsyncSignature.Compute(Png);
        var fileMd5 = Convert.ToBase64String(MD5.HashData(Png));
        var signatureMd5 = Convert.ToBase64String(MD5.HashData(signature));
        var steps = app.Vrchat.Calls.Where(c => c.Contains("file/") || c.Contains("fake-s3") || c == setCall).ToList();
        await Assert.That(string.Join("\n", steps)).IsEqualTo(string.Join("\n", new[]
        {
            $"POST file/{fileId}",
            $"PUT file/{fileId}/{version}/file/start",
            "PUT fake-s3/file",
            $"PUT file/{fileId}/{version}/file/finish",
            $"PUT file/{fileId}/{version}/signature/start",
            "PUT fake-s3/signature",
            $"PUT file/{fileId}/{version}/signature/finish",
            setCall,
        }));
        var bodies = app.Vrchat.Bodies;
        await Assert.That(bodies[$"POST file/{fileId}"]).IsEqualTo(
            $$"""{"fileMd5":"{{fileMd5}}","fileSizeInBytes":{{Png.Length}},"signatureMd5":"{{signatureMd5}}","signatureSizeInBytes":{{signature.Length}}}""");
        await Assert.That(bodies[$"PUT file/{fileId}/{version}/file/start"]).IsEqualTo("{}");
        await Assert.That(bodies[$"PUT file/{fileId}/{version}/file/finish"]).IsEqualTo("""{"maxParts":0,"nextPartNumber":0}""");
        await Assert.That(bodies[$"PUT file/{fileId}/{version}/signature/start"]).IsEqualTo("{}");
        await Assert.That(bodies[$"PUT file/{fileId}/{version}/signature/finish"]).IsEqualTo("""{"maxParts":0,"nextPartNumber":0}""");
        await Assert.That(bodies[setCall]).IsEqualTo($$"""{"id":"{{entityId}}","imageUrl":"{{Prefix}}/file/{{fileId}}/{{version}}/file"}""");

        await Assert.That(uploads.Count).IsEqualTo(2);
        await Assert.That(uploads[0].ContentType).IsEqualTo("image/png");
        await Assert.That(uploads[0].Md5).IsEqualTo(fileMd5);
        await Assert.That(Convert.ToHexString(uploads[0].Data)).IsEqualTo(Convert.ToHexString(Png));
        await Assert.That(uploads[1].ContentType).IsEqualTo("application/x-rsync-signature");
        await Assert.That(uploads[1].Md5).IsEqualTo(signatureMd5);
        await Assert.That(Convert.ToHexString(uploads[1].Data)).IsEqualTo(Convert.ToHexString(signature));
    }

    [Test]
    public async Task Changing_a_world_image_runs_the_file_and_signature_uploads_then_sets_it()
    {
        await using var app = await StartAsync();
        app.Vrchat.World("wrld_img", "Pic", w => w.Author("usr_me", "Me"));
        var uploads = FakeFileApi(app.Vrchat, "file_world", 3);
        app.Vrchat.Json("PUT", "worlds/wrld_img", $$"""{"id":"wrld_img","name":"Pic","authorId":"usr_me","imageUrl":"{{Prefix}}/file/file_world/3/file"}""");

        var response = await app.Api.PostAsync(app.AccountPath("worlds/wrld_img/image"), ImageForm($"{Prefix}/file/file_world/2/file"));

        await VrcxScenario.EnsureSuccessAsync(response, "change world image");
        var answer = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        await Assert.That(answer.GetProperty("imageUrl").GetString()).IsEqualTo($"{Prefix}/file/file_world/3/file");
        await AssertFileFlowAsync(app, uploads, "file_world", 3, "PUT worlds/wrld_img", "wrld_img");
    }

    [Test]
    public async Task A_refused_file_version_finishes_the_old_version_and_fails()
    {
        await using var app = await StartAsync();
        app.Vrchat
            .Fail("POST", "file/file_bad", HttpStatusCode.BadRequest, "nope")
            .Json("GET", "file/file_bad", """{"id":"file_bad","versions":[{"version":0},{"version":1}]}""")
            .Json("PUT", "file/file_bad/1/signature/finish", "{}")
            .Json("PUT", "file/file_bad/1/file/finish", "{}");

        var response = await app.Api.PostAsync(app.AccountPath("worlds/wrld_bad/image"), ImageForm($"{Prefix}/file/file_bad/1/file"));

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(app.Vrchat.Bodies["PUT file/file_bad/1/signature/finish"]).IsEqualTo("{}");
        await Assert.That(app.Vrchat.Bodies["PUT file/file_bad/1/file/finish"]).IsEqualTo("{}");
        app.Vrchat.AssertNotCalled("PUT", "worlds/wrld_bad");
    }

    [Test]
    public async Task An_image_that_does_not_stick_is_an_error()
    {
        await using var app = await StartAsync();
        FakeFileApi(app.Vrchat, "file_stuck", 2);
        app.Vrchat.Json("PUT", "worlds/wrld_stuck", $$"""{"id":"wrld_stuck","name":"S","authorId":"usr_me","imageUrl":"{{Prefix}}/file/file_stuck/1/file"}""");

        var response = await app.Api.PostAsync(app.AccountPath("worlds/wrld_stuck/image"), ImageForm($"{Prefix}/file/file_stuck/1/file"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
    }
}
