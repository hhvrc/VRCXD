using VRCX.Common.Live;

namespace VRCX.Common.Tests;

// The live socket's counts, durations and times are unsigned on the wire. A value that came out
// negative (a clock behind, a setting stored wrong) is sent as zero, not wrapped to four billion.
public sealed class UnsignedTests
{
    [Test]
    public async Task A_negative_value_is_sent_as_zero_and_the_rest_as_they_are()
    {
        await Assert.That(Unsigned.Of(-1)).IsEqualTo(0u);
        await Assert.That(Unsigned.Of(int.MinValue)).IsEqualTo(0u);
        await Assert.That(Unsigned.Of(0)).IsEqualTo(0u);
        await Assert.That(Unsigned.Of(int.MaxValue)).IsEqualTo((uint)int.MaxValue);
        await Assert.That(Unsigned.Of(-5L)).IsEqualTo(0ul);
        await Assert.That(Unsigned.Of(long.MaxValue)).IsEqualTo((ulong)long.MaxValue);
    }
}
