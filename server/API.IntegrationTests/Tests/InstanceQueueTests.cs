using System.Text.Json.Nodes;
using VRCX.Testing;

namespace VRCX.API.IntegrationTests.Tests;

// Upstream issue 1420: the original's instance queue (instance-queue-joined, -position, -left),
// which the port parsed and dropped.
public sealed class InstanceQueueTests
{
    private const string Queued = "wrld_queue:1~group(grp_q)~groupAccessType(public)~region(eu)";
    private const string Other = "wrld_queue:2~region(eu)";

    private static JsonObject Content(string location, int? position = null, int? size = null)
    {
        var content = new JsonObject { ["instanceLocation"] = location };
        if (position is not null) content["position"] = position;
        if (size is not null) content["queueSize"] = size;
        return content;
    }

    [Test]
    public async Task Joining_then_moving_up_reports_the_place_and_leaving_clears_it()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        await app.Pipeline.SendAsync("instance-queue-joined", Content(Queued, 5, 10));
        await app.Pipeline.SendAsync("instance-queue-position", Content(Queued, 3, 10));
        var queues = await app.GetJsonAsync(app.AccountPath("instance-queue"));
        await Assert.That(queues.GetArrayLength()).IsEqualTo(1);
        await Assert.That(queues[0].GetProperty("location").GetString()).IsEqualTo(Queued);
        await Assert.That(queues[0].GetProperty("position").GetInt32()).IsEqualTo(3);
        await Assert.That(queues[0].GetProperty("queueSize").GetInt32()).IsEqualTo(10);

        await app.Pipeline.SendAsync("instance-queue-left", Content(Queued));
        queues = await app.GetJsonAsync(app.AccountPath("instance-queue"));
        await Assert.That(queues.GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task Ready_or_moving_elsewhere_clears_the_queue()
    {
        await using var app = await VrcxScenario.StartAsync();
        await app.LoginAsync();

        await app.Pipeline.SendAsync("instance-queue-joined", Content(Queued, 2, 4));
        await app.Pipeline.SendAsync("instance-queue-ready", Content(Queued));
        await Assert.That((await app.GetJsonAsync(app.AccountPath("instance-queue"))).GetArrayLength()).IsEqualTo(0);

        await app.Pipeline.SendAsync("instance-queue-joined", Content(Queued, 2, 4));
        await app.Pipeline.UserLocationAsync(Other);
        await Assert.That((await app.GetJsonAsync(app.AccountPath("instance-queue"))).GetArrayLength()).IsEqualTo(0);
    }
}
