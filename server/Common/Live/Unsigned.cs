namespace VRCX.Common.Live;

/// <summary>
/// A count, a duration or a time on the live socket as its unsigned wire type. A plain cast would
/// turn a value that came out negative (a clock that went back, a setting stored wrong) into one
/// near four billion; the wire says "never negative", so it is held at zero instead.
/// </summary>
public static class Unsigned
{
    public static uint Of(int value) => value > 0 ? (uint)value : 0;

    public static ulong Of(long value) => value > 0 ? (ulong)value : 0;
}
