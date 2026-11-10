using VRCX.Common.Services;

namespace VRCX.Common.Tests;

// The original's base/string.js cleanups: replaceBioSymbols and removeEmojis.
public class VrchatTextTests
{
    [Test]
    public async Task Look_alike_symbols_turn_back_into_ascii() =>
        await Assert.That(VrchatText.ReplaceBioSymbols("a＠b  c（d）  ")).IsEqualTo("a@b c(d)");

    [Test]
    [Arguments("Hello 🌍 World", "Hello World")]
    [Arguments("A  🎮  B", "A B")]
    [Arguments("Hello World", "Hello World")]
    [Arguments("  Hello  ", "Hello")]
    [Arguments("", "")]
    [Arguments(null, "")]
    public async Task Emojis_are_removed_and_whitespace_collapsed(string? text, string expected) =>
        await Assert.That(VrchatText.RemoveEmojis(text)).IsEqualTo(expected);

    [Test]
    public async Task A_status_description_gets_both_cleanups() =>
        await Assert.That(VrchatText.StatusDescription("hello？ 🎉 world")).IsEqualTo("hello? world");
}
