namespace AtlasLiterario.Application.Abstractions;

public interface IClock
{
    /// <summary>Current instant with offset zero.</summary>
    DateTimeOffset UtcNow { get; }
}
