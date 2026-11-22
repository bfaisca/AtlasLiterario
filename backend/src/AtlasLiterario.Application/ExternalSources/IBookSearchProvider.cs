using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.ExternalSources;

/// <summary>Bibliographic search source (Google Books, Open Library).</summary>
public interface IBookSearchProvider
{
    ExternalProvider Provider { get; }

    Task<IReadOnlyList<ExternalBookCandidate>> SearchAsync(BookSearchQuery query, CancellationToken ct);

    Task<ExternalBookCandidate?> GetAsync(string externalId, CancellationToken ct);
}
