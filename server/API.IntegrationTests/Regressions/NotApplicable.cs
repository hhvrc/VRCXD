namespace VRCX.API.IntegrationTests.Regressions;

/// <summary>
/// Closed upstream issues and PRs that have no server-port test, and why. Every closed item
/// ends up either with an [Issue(n)]/[Pr(n)] test or here (server/Testing/upstream-coverage.py
/// lists the ones that have neither). Keep the reason short and say which part owns it.
/// </summary>
public static class NotApplicable
{
    public const string Desktop = "the PC's agent (agent/), tested there if at all: ";
    public const string Overlay = "the agent's VR overlay (agent/src/vr), no server behaviour: ";
    public const string Packaging = "the original's Electron/CEF packaging: ";
    public const string ClientOnly = "client only, no server behaviour; a Playwright test (client/e2e) if anything: ";
    public const string Question = "a question or support request, nothing to test: ";

    public static readonly IReadOnlyDictionary<int, string> Items = new Dictionary<int, string>
    {
        [9] = Overlay + "notification time and position",
        [16] = Desktop + "starting VRChat in desktop mode is the launcher's job",
        [20] = Overlay + "SteamVR base station power control",
        [21] = Packaging + "where the html folder goes",
    };
}
