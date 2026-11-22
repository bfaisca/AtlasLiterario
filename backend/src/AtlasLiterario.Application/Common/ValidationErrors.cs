using AtlasLiterario.Domain.Exceptions;

namespace AtlasLiterario.Application.Common;

/// <summary>Collects field errors and throws a single <see cref="ValidationException"/>.</summary>
public sealed class ValidationErrors
{
    public const string DefaultCode = "validation.failed";

    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public ValidationErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
        return this;
    }

    public void ThrowIfAny(string code = DefaultCode, string message = "Um ou mais campos são inválidos.")
    {
        if (HasErrors)
        {
            throw new ValidationException(code, message, _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }
}
