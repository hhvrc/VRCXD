using Microsoft.AspNetCore.TestHost;
using TUnit.Core.Interfaces;
using VRCX.Testing;
using VRCX.Testing.FakeVrchat;

namespace VRCX.API.IntegrationTests;

/// <summary>
/// The older tests' fixture, now one <see cref="VrcxScenario"/> per test (use with
/// <c>[ClassDataSource&lt;IsolatedHost&gt;(Shared = SharedType.None)]</c>). It used to be a single
/// host shared by the whole session, so a stub one test set on the fake VRChat could be read by
/// another and leftovers from one test reached the next (the intermittent InvitePhotoTests and
/// UserActionsTests failures). Each test now has its own database, Redis database and fake.
/// New tests should use <see cref="VrcxScenario"/> directly.
/// </summary>
public sealed class IsolatedHost : IAsyncInitializer, IAsyncDisposable
{
    public const string ApiToken = VrcxScenario.ApiToken;

    public VrcxScenario Scenario { get; private set; } = null!;

    public async Task InitializeAsync() => Scenario = await VrcxScenario.StartAsync();

    public FakeVrchatApi Vrchat => Scenario.Vrchat;
    public IServiceProvider Services => Scenario.Services;
    public TestServer Server => Scenario.Server;

    public HttpClient CreateAuthenticatedClient() => Scenario.CreateClient();
    public HttpClient CreateClient() => Scenario.CreateClient(authenticated: false);

    public async ValueTask DisposeAsync()
    {
        if (Scenario is not null) await Scenario.DisposeAsync();
    }
}
