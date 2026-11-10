using System.Net;
using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Services;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream issue 1396: the original's status bar and login page show status.vrchat.com's incidents.
public sealed class VrchatStatusTests
{
    private sealed class StatusStub(string description, string indicator) : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var url = request.RequestUri!.ToString();
            var body = url.EndsWith("summary.json", StringComparison.Ordinal)
                ? """{"components":[{"name":"API","status":"major_outage"},{"name":"Website","status":"operational"},{"name":"Realtime","status":"degraded_performance"}]}"""
                : $$$"""{"page":{"updated_at":"2026-11-08T12:00:00.000Z"},"status":{"indicator":"{{{indicator}}}","description":"{{{description}}}"}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static async Task<(VRCX.Testing.VrcxScenario App, StatusStub Stub)> StartAsync(string description, string indicator)
    {
        var stub = new StatusStub(description, indicator);
        var app = await VRCX.Testing.VrcxScenario.StartAsync(new()
        {
            ConfigureServices = s => s.AddHttpClient(VrchatStatus.ClientName).ConfigurePrimaryHttpMessageHandler(() => stub),
        });
        return (app, stub);
    }

    [Test]
    public async Task An_outage_reports_the_description_and_affected_components()
    {
        var (app, stub) = await StartAsync("Major System Outage", "major");
        await using var _ = app;

        var status = await app.GetJsonAsync("/1/vrchat-status");

        await Assert.That(status.GetProperty("status").GetString()).IsEqualTo("Major System Outage");
        await Assert.That(status.GetProperty("indicator").GetString()).IsEqualTo("major");
        await Assert.That(status.GetProperty("summary").GetString()).IsEqualTo("API, Realtime");
        await Assert.That(status.GetProperty("hasIssue").GetBoolean()).IsTrue();

        // Within the 2 minute window the cached answer is served.
        await app.GetJsonAsync("/1/vrchat-status");
        await Assert.That(stub.Calls).IsEqualTo(2);
    }

    [Test]
    public async Task All_systems_operational_is_no_issue()
    {
        var (app, stub) = await StartAsync("All Systems Operational", "none");
        await using var _ = app;

        var status = await app.GetJsonAsync("/1/vrchat-status");

        await Assert.That(status.GetProperty("status").GetString()).IsEqualTo("");
        await Assert.That(status.GetProperty("hasIssue").GetBoolean()).IsFalse();
        // The summary is only read when something is wrong.
        await Assert.That(stub.Calls).IsEqualTo(1);
    }
}
