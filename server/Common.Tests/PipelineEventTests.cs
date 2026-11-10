using VRCX.Vrchat.Pipeline;

namespace VRCX.Common.Tests;

// Issue 1670: VRChat's {"err":...} frame must not look like an ordinary event, or the pipeline loop
// treats it as a healthy connection and reconnects every second.
public sealed class PipelineEventTests
{
    [Test]
    public async Task An_err_frame_is_an_error_event()
    {
        var e = PipelineEvent.Parse("""{"err":"authToken doesn't correspond with an active session","authToken":"authcookie_x"}""");
        await Assert.That(e).IsTypeOf<PipelineErrorEvent>();
        var error = (PipelineErrorEvent)e;
        await Assert.That(error.IsAuthError).IsTrue();
        await Assert.That(error.Message).Contains("active session");
    }

    [Test]
    public async Task An_ordinary_frame_is_not()
    {
        var e = PipelineEvent.Parse("""{"type":"see-notification","content":"not_1"}""");
        await Assert.That(e is PipelineErrorEvent).IsFalse();
    }
}
