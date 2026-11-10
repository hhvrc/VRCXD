using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's getBundleDateSize: per platform, the analysis of the newest bundle the SDK can open.
public sealed class FileAnalysisTests
{
    [Test]
    public async Task Each_platform_gets_the_newest_supported_bundles_analysis()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Json("config", """{"sdkUnityVersion":"2022.3.22f1"}""");
        app.Vrchat.World("wrld_sized", "Sized", w => w.Set("unityPackages", System.Text.Json.Nodes.JsonNode.Parse("""
            [
              {"id":"unp_1","platform":"standalonewindows","unitySortNumber":20220322000,"assetUrl":"https://api.vrchat.cloud/api/1/file/file_pc-old/3/file","variant":"standard"},
              {"id":"unp_2","platform":"standalonewindows","unitySortNumber":20220322000,"assetUrl":"https://api.vrchat.cloud/api/1/file/file_pc-new/7/file","variant":"standard"},
              {"id":"unp_3","platform":"standalonewindows","unitySortNumber":20230100000,"assetUrl":"https://api.vrchat.cloud/api/1/file/file_pc-future/1/file"},
              {"id":"unp_4","platform":"android","unitySortNumber":20220322000,"assetUrl":"https://api.vrchat.cloud/api/1/file/file_quest/2/file","variant":"security"},
              {"id":"unp_5","platform":"android","unitySortNumber":20220322000,"assetUrl":"https://api.vrchat.cloud/api/1/file/file_imp/9/file","variant":"impostor"},
              {"id":"unp_6","platform":"ios","unitySortNumber":20220322000,"assetUrl":"https://api.vrchat.cloud/api/1/file/file_ios/4/file"}
            ]
            """)));
        app.Vrchat.Json("analysis/file_pc-new/7/security", """{"success":true,"fileSize":1048576,"uncompressedSize":4194304}""");
        app.Vrchat.Json("analysis/file_quest/2/security", """{"success":true,"fileSize":2048}""");
        app.Vrchat.Json("analysis/file_ios/4/security", """{"success":false}""");

        var analysis = await app.GetJsonAsync(app.AccountPath("worlds/wrld_sized/file-analysis"));

        await Assert.That(analysis.GetProperty("standalonewindows").GetProperty("fileSize").GetInt64()).IsEqualTo(1048576);
        await Assert.That(analysis.GetProperty("android").GetProperty("fileSize").GetInt64()).IsEqualTo(2048);
        await Assert.That(analysis.TryGetProperty("ios", out _)).IsFalse();
    }

    // The original's extractFileId and extractFileVersion: a URL without both is skipped, and a
    // version of several digits is read whole.
    [Test]
    public async Task Bundles_need_a_file_id_and_version_in_their_url_and_versions_keep_every_digit()
    {
        static Vrchat.Models.UnityPackage Package(string platform, string? url) =>
            new() { Id = "unp_" + platform, Platform = platform, AssetUrl = url, UnitySortNumber = 20220322000 };

        var picked = Controller.Accounts.AccountsController.PickAnalysedBundles(
        [
            Package("standalonewindows", "https://api.vrchat.cloud/api/1/file/file_pc-big/123/file"),
            Package("android", "file_plain-string"),
            Package("ios", "https://example.com/no/match/here"),
            Package("linux", null),
        ], "2022.3.22f1").ToList();

        await Assert.That(picked).IsEquivalentTo(new[] { ("standalonewindows", "file_pc-big", 123, "security") });
    }

    // The original's cachedConfig.sdkUnityVersion, which the pop-ups' Platform line is held against.
    [Test]
    public async Task The_sdk_unity_version_comes_from_the_config()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat.Json("config", """{"sdkUnityVersion":"2022.3.22f1"}""");

        var found = await app.GetJsonAsync(app.AccountPath("sdk-unity-version"));
        await Assert.That(found.GetProperty("sdkUnityVersion").GetString()).IsEqualTo("2022.3.22f1");
    }
}
