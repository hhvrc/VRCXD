using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// VRChat serves names of thousands of characters: one world is named after the Bee Movie script,
// cut off at 4,999. A btree index on the name could not hold an entry that long and refused the
// row, which failed a VRCX import (and would have failed storing the world). Names are stored whole.
public sealed class LongNameTests
{
    /// <summary>Text of <paramref name="length"/> characters that does not compress: the repeats of "AAAA" fit an index, prose does not.</summary>
    public static string Prose(int length)
    {
        var random = new Random(1);
        string[] words = ["according", "to", "all", "known", "laws", "of", "aviation", "there", "is", "no", "way", "a", "bee", "should", "be", "able", "fly"];
        var text = new System.Text.StringBuilder();
        while (text.Length < length) text.Append(words[random.Next(words.Length)]).Append(random.Next(10) == 0 ? ". " : " ").Append(random.Next(1000));
        return text.ToString(0, length);
    }

    [Test]
    public async Task A_world_an_avatar_and_a_group_with_a_5000_character_name_are_stored_with_all_of_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();
        var name = Prose(5_000);
        app.Vrchat.World("wrld_bee", name);
        app.Vrchat.Avatar("avtr_bee", name);
        app.Vrchat.Group("grp_bee", name);

        foreach (var path in new[] { "/1/worlds/wrld_bee", "/1/avatars/avtr_bee", "/1/groups/grp_bee" })
        {
            var stored = await app.GetJsonAsync($"{path}?refresh=true&account={app.AccountId}");
            await Assert.That(stored.GetProperty("name").GetString()).IsEqualTo(name).Because(path);
        }
    }
}
