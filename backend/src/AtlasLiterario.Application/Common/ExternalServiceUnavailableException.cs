namespace AtlasLiterario.Application.Common;

/// <summary>An external bibliographic source failed (mapped to HTTP 502, code <c>external.unavailable</c>).</summary>
public sealed class ExternalServiceUnavailableException(string sourceName, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public const string Code = "external.unavailable";

    public string SourceName { get; } = sourceName;
}
