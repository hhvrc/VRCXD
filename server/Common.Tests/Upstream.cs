namespace VRCX.Common.Tests;

/// <summary>
/// The same tags as API.IntegrationTests' Regressions/Upstream.cs, for unit tests that cover a closed
/// vrcx-team/VRCX issue or PR (server/Testing/README.md). Names start with Issue_n_ or Pr_n_.
/// </summary>
public sealed class IssueAttribute(int number) : PropertyAttribute("Upstream", $"vrcx-team/VRCX#{number}");

public sealed class PrAttribute(int number) : PropertyAttribute("Upstream", $"vrcx-team/VRCX#{number}");

/// <summary>Tagged tests here follow the ledger's naming too (LedgerTests in API.IntegrationTests).</summary>
public sealed class UpstreamTagTests
{
    [Test]
    public async Task Tagged_test_names_start_with_the_item_they_cover()
    {
        var wrong = typeof(UpstreamTagTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Select(m => (Method: m, Numbers: m.GetCustomAttributes(typeof(PropertyAttribute), false).Cast<PropertyAttribute>()
                .Where(a => a.Name == "Upstream").Select(a => int.Parse(a.Value.Split('#')[1])).ToList()))
            .Where(t => t.Numbers.Count > 0 && !t.Numbers.Any(n => t.Method.Name.StartsWith($"Issue_{n}_", StringComparison.Ordinal) || t.Method.Name.StartsWith($"Pr_{n}_", StringComparison.Ordinal)))
            .Select(t => t.Method.DeclaringType!.Name + "." + t.Method.Name)
            .ToList();
        await Assert.That(wrong).IsEmpty();
    }
}
