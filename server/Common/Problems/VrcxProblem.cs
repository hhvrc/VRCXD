using System.Net;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace VRCX.Common.Problems;

/// <summary>
/// An API error as RFC 9457 problem details: a stable <c>Domain.Reason</c> type the clients switch
/// on, a title, the HTTP status and an optional detail. The ones the server answers with are
/// declared in <c>Common/Errors/Errors.cs</c>.
/// </summary>
public class VrcxProblem : ProblemDetails
{
    public VrcxProblem(string type, string title, HttpStatusCode status = HttpStatusCode.BadRequest, string? detail = null)
    {
        Type = type;
        Title = title;
        Detail = detail;
        Status = (int)status;
    }

    /// <summary>The request's trace identifier, to find the request in the server's log.</summary>
    public string? RequestId { get; set; }

    /// <summary>The problem as a controller's response, with its status and the request's id.</summary>
    public ObjectResult ToObjectResult(HttpContext context)
    {
        RequestId = context.TraceIdentifier;
        return new ObjectResult(this) { StatusCode = Status };
    }

    /// <summary>Writes the problem as the response where there is no controller to return it from (authentication, the exception handler).</summary>
    public Task WriteAsJsonAsync(HttpContext context, JsonSerializerOptions jsonSerializerOptions, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = Status ?? StatusCodes.Status400BadRequest;
        RequestId = context.TraceIdentifier;
        return context.Response.WriteAsJsonAsync(this, jsonSerializerOptions, MediaTypeNames.Application.ProblemJson, cancellationToken);
    }
}
