using VRCX.Common.Problems;

namespace VRCX.Common;

/// <summary>
/// What a feature hands back when it can fail: the value, or the problem to answer with. It is a
/// union, so there is no "both" and no "neither", and a caller cannot reach the value without
/// saying what happens to the problem. A feature returns either one as it is
/// (<c>return AccountError.NoLiveSession;</c>, <c>return avatar;</c>); a controller answers with
/// <see cref="VrcxControllerBase.Answer{T}(Result{T})"/> or switches over the cases itself.
/// A read that may find nothing says so in its type, <c>Result&lt;T?&gt;</c>, and the switch over it
/// then has a null case. What can only fail or not, with nothing to hand back, stays a nullable
/// <see cref="VrcxProblem"/>.
/// </summary>
public union Result<T>(T, VrcxProblem);
