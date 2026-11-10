using Microsoft.Extensions.DependencyInjection;
using VRCX.Common.Options;

namespace VRCX.API.IntegrationTests.Tests;

// Error reporting goes only to the address this server is configured with; without one it is off.
public sealed class ErrorReportingTests
{
    [Test]
    public async Task Without_a_configured_address_error_reporting_is_off()
    {
        await using var app = await VRCX.Testing.VrcxScenario.StartAsync();

        var body = await app.GetJsonAsync("/1/error-reporting");

        // Nulls are left out of responses, so no address means no dsn field or a null one.
        var hasDsn = body.TryGetProperty("dsn", out var dsn) && dsn.ValueKind != System.Text.Json.JsonValueKind.Null;
        await Assert.That(hasDsn).IsFalse();
    }

    [Test]
    public async Task The_configured_address_is_given_to_clients()
    {
        await using var app = await VRCX.Testing.VrcxScenario.StartAsync(new()
        {
            ConfigureServices = s => s.AddSingleton(new ErrorReportingOptions { Dsn = " https://key@sentry.example/1 " }),
        });

        var body = await app.GetJsonAsync("/1/error-reporting");

        await Assert.That(body.GetProperty("dsn").GetString()).IsEqualTo("https://key@sentry.example/1");
    }
}
