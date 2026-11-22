namespace AtlasLiterario.Application.ExternalSources;

/// <summary>Geographic resolution through Wikidata (P27 citizenship, P495 country of origin, P407/P364 language).</summary>
/// <remarks>Implementations throw <c>ExternalServiceUnavailableException</c> when the source cannot be reached.</remarks>
public interface IGeoResolver
{
    /// <summary>Countries of citizenship (P27). Deprecated statements are ignored; preferred ones win when present.</summary>
    Task<IReadOnlyList<ResolvedCountry>> GetAuthorCitizenshipsAsync(string authorQid, CancellationToken ct);

    /// <summary>Countries of origin of the work (P495), same rank rules.</summary>
    Task<IReadOnlyList<ResolvedCountry>> GetWorkOriginsAsync(string workQid, CancellationToken ct);

    /// <summary>ISO 639-1 code of the language of the work (P407, then P364), or null when unknown/ambiguous.</summary>
    Task<string?> GetWorkOriginalLanguageAsync(string workQid, CancellationToken ct);

    /// <summary>Finds the work QID (P648 Open Library work id; then exact label + P50 author). Null when not found or ambiguous.</summary>
    Task<string?> FindWorkQidAsync(
        string? isbn13, string? openLibraryWorkId, string title, string? authorQid, CancellationToken ct);
}
