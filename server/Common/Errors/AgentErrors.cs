using System.Net;
using OpenShock.Internal.Common.Problems;

namespace VRCX.Common.Errors;

/// <summary>What the PC's agent answered when it could not do a relayed action.</summary>
public static class AgentError
{
    public static OpenShockProblem Unsupported => new("Agent.Unsupported", "The agent on that PC does not do this; it may need updating", HttpStatusCode.Conflict);
    public static OpenShockProblem NotConfirmed => new("Agent.NotConfirmed", "That needs confirming first", HttpStatusCode.BadRequest);
    public static OpenShockProblem NotFound(string? detail) => new("Agent.NotFound", "That is not on the PC", HttpStatusCode.NotFound, detail);
}
