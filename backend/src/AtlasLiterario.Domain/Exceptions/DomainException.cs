namespace AtlasLiterario.Domain.Exceptions;

/// <summary>
/// Base class for expected business errors. <see cref="Code"/> is a stable identifier
/// returned to clients in the ProblemDetails <c>code</c> field (e.g. "auth.email_taken").
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Resource does not exist or is not visible to the current user (HTTP 404).</summary>
public sealed class NotFoundException(string code, string message) : DomainException(code, message)
{
    public NotFoundException(string message) : this("resource.not_found", message) { }
}

/// <summary>Operation conflicts with the current state (HTTP 409).</summary>
public sealed class ConflictException(string code, string message, IReadOnlyDictionary<string, object?>? extensions = null)
    : DomainException(code, message)
{
    /// <summary>Extra ProblemDetails members (e.g. <c>libraryItemId</c>).</summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
}

/// <summary>Request payload exceeds the allowed size (HTTP 413).</summary>
public sealed class PayloadTooLargeException(string code, string message) : DomainException(code, message);

/// <summary>Authenticated user is not allowed to perform the operation (HTTP 403).</summary>
public sealed class ForbiddenException(string code, string message) : DomainException(code, message);

/// <summary>Missing or invalid credentials (HTTP 401).</summary>
public sealed class UnauthorizedException(string code, string message) : DomainException(code, message);

/// <summary>Invalid input or violated invariant (HTTP 400).</summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string code, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(code, message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    /// <summary>Field-level errors (camelCase field name → messages). May be empty.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static ValidationException ForField(string code, string field, string message) =>
        new(code, message, new Dictionary<string, string[]> { [field] = [message] });
}
