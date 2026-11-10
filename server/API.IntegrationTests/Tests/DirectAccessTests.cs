using System.Net.Http.Json;
using System.Text.Json;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// The original's Direct Access lookups: a vrch.at short name to its instance, and a group short code.
public sealed class DirectAccessTests
{
    [Test]
    public async Task Short_names_and_group_short_codes_are_looked_up()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        app.Vrchat
            .Json("instances/s/abcd1234", """{"location":"wrld_x:1~region(eu)","shortName":"abcd1234"}""")
            .Json("groups/strictsearch?query=ABC.1234", """[{"id":"grp_x","shortCode":"ABC","discriminator":"1234"}]""");

        var instance = await app.Api.GetFromJsonAsync<JsonElement>($"/1/accounts/{app.AccountId}/search/short-name/abcd1234");
        await Assert.That(instance.GetProperty("location").GetString()).IsEqualTo("wrld_x:1~region(eu)");

        var groups = await app.Api.GetFromJsonAsync<JsonElement>($"/1/accounts/{app.AccountId}/search/groups/strict?query=ABC.1234");
        await Assert.That(groups[0].GetProperty("id").GetString()).IsEqualTo("grp_x");
    }
}
