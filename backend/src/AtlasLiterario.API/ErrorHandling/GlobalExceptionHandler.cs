using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AtlasLiterario.API.ErrorHandling;

/// <summary>Maps exceptions to RFC 7807 ProblemDetails with a stable <c>code</c>.</summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            ValidationException e => (StatusCodes.Status400BadRequest, "Dados inválidos.", e.Code),
            UnauthorizedException e => (StatusCodes.Status401Unauthorized, "Não autenticado.", e.Code),
            ForbiddenException e => (StatusCodes.Status403Forbidden, "Acesso negado.", e.Code),
            NotFoundException e => (StatusCodes.Status404NotFound, "Recurso não encontrado.", e.Code),
            PayloadTooLargeException e => (StatusCodes.Status413PayloadTooLarge, "Conteúdo grande demais.", e.Code),
            ConflictException e => (StatusCodes.Status409Conflict, "Conflito.", e.Code),
            ExternalServiceUnavailableException => (
                StatusCodes.Status502BadGateway, "Fonte externa indisponível.", ExternalServiceUnavailableException.Code),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } e
                when httpContext.Request.Path.Value?.EndsWith("/cover", StringComparison.OrdinalIgnoreCase) == true
                => (e.StatusCode, "Conteúdo grande demais.", "cover.too_large"),
            BadHttpRequestException e => (e.StatusCode, "Requisição inválida.", ProblemDetailsDefaults.DefaultCode(e.StatusCode)),
            _ => (StatusCodes.Status500InternalServerError, "Erro inesperado.", "server.error"),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        ProblemDetails problem = exception is ValidationException { Errors.Count: > 0 } validation
            ? new HttpValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        // Business messages are safe to show; unexpected errors never expose internals.
        problem.Detail = exception is DomainException or ExternalServiceUnavailableException ? exception.Message : null;
        problem.Extensions[ProblemDetailsDefaults.CodeKey] = code;
        if (exception is ConflictException conflict)
        {
            foreach (var (key, value) in conflict.Extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);
}
