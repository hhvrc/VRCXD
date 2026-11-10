namespace VRCX.API.IntegrationTests.Regressions;

/// <summary>
/// Tags for regression tests taken from closed vrcx-team/VRCX issues and PRs (see
/// Server/Testing/README.md). Filter with --treenode-filter "/*/*/*/*[Category=Regression]" or by
/// [Upstream=vrcx-team/VRCX#1234].
/// </summary>
public sealed class IssueAttribute(int number) : PropertyAttribute("Upstream", $"vrcx-team/VRCX#{number}");

public sealed class PrAttribute(int number) : PropertyAttribute("Upstream", $"vrcx-team/VRCX#{number}");
