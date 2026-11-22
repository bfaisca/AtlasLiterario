using System.Diagnostics;

namespace AtlasLiterario.API.ErrorHandling;

/// <summary>Adds a stable <c>code</c> (and <c>traceId</c>) to every ProblemDetails produced by the API.</summary>
public static class ProblemDetailsDefaults
{
    public const string CodeKey = "code";

    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Instance ??= context.HttpContext.Request.Path;
        problem.Extensions.TryAdd(
            "traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

        if (!problem.Extensions.ContainsKey(CodeKey))
        {
            problem.Extensions[CodeKey] = DefaultCode(problem.Status ?? context.HttpContext.Response.StatusCode);
        }
    }

    public static string DefaultCode(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "validation.failed",
        StatusCodes.Status401Unauthorized => "auth.unauthorized",
        StatusCodes.Status403Forbidden => "auth.forbidden",
        StatusCodes.Status404NotFound => "resource.not_found",
        StatusCodes.Status405MethodNotAllowed => "http.method_not_allowed",
        StatusCodes.Status409Conflict => "resource.conflict",
        StatusCodes.Status415UnsupportedMediaType => "http.unsupported_media_type",
        StatusCodes.Status501NotImplemented => "feature.not_implemented",
        StatusCodes.Status502BadGateway => "external.unavailable",
        >= 500 => "server.error",
        _ => $"http.{status}",
    };
}
