using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using VRCX.Common.Problems;

namespace VRCX.Common.ExceptionHandling;

/// <summary>
/// What an exception no endpoint caught answers: it is logged with the request's method and path,
/// and the client gets a 500 problem that says nothing about what went wrong. The request's headers
/// and body are not logged: they carry the server token and passwords.
/// </summary>
public sealed class VrcxExceptionHandler(ILogger<VrcxExceptionHandler> logger, JsonSerializerOptions jsonSerializerOptions) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        var problem = new VrcxProblem("Exception", "An unknown exception occurred", HttpStatusCode.InternalServerError,
            "An unknown error occurred. Please try again later. If the issue persists, look at the server's log.");
        await problem.WriteAsJsonAsync(context, jsonSerializerOptions, cancellationToken);
        return context.Response.HasStarted;
    }
}
