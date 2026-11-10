using System.Net;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OpenShock.Internal.Common.Problems;

namespace VRCX.Common.Problems;

public sealed class ValidationProblem : OpenShockProblem
{
    public ValidationProblem(ModelStateDictionary state) : base("Validation.Error",
        "One or more validation errors occurred", HttpStatusCode.BadRequest, null)
    {
        Errors = CreateErrorDictionary(state);
    }

    public IDictionary<string, string[]> Errors { get; set; }

    private static Dictionary<string, string[]> CreateErrorDictionary(ModelStateDictionary modelState)
    {
        var errorDictionary = new Dictionary<string, string[]>(modelState.Count, StringComparer.Ordinal);
        foreach (var (key, value) in modelState)
        {
            var errors = value.Errors;
            if (errors is not { Count: > 0 }) continue;
            errorDictionary.Add(key, errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "The input was not valid" : e.ErrorMessage).ToArray());
        }
        return errorDictionary;
    }
}
