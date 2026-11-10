using System.Reflection;

namespace VRCX.API.IntegrationTests.Regressions;

/// <summary>The bookkeeping around the regression tests stays consistent.</summary>
public sealed class LedgerTests
{
    private static IEnumerable<(MethodInfo Method, int Number)> Tagged() =>
        typeof(LedgerTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetCustomAttributes<PropertyAttribute>()
                .Where(a => a.Name == "Upstream")
                .Select(a => (m, int.Parse(a.Value.Split('#')[1]))));

    [Test]
    public async Task Nothing_is_both_tested_and_marked_not_applicable()
    {
        var both = Tagged().Select(t => t.Number).Where(NotApplicable.Items.ContainsKey).Distinct().ToList();
        await Assert.That(both).IsEmpty();
    }

    [Test]
    public async Task Regression_test_names_start_with_the_item_they_cover()
    {
        var wrong = Tagged()
            .GroupBy(t => t.Method)
            .Where(g => !g.Any(t => g.Key.Name.StartsWith($"Issue_{t.Number}_", StringComparison.Ordinal) || g.Key.Name.StartsWith($"Pr_{t.Number}_", StringComparison.Ordinal)))
            .Select(g => g.Key.DeclaringType!.Name + "." + g.Key.Name)
            .ToList();
        await Assert.That(wrong).IsEmpty();
    }

    [Test]
    public async Task Every_not_applicable_item_has_a_reason()
    {
        await Assert.That(NotApplicable.Items.Where(i => i.Value.Trim().EndsWith(':')).Select(i => i.Key)).IsEmpty();
    }
}
