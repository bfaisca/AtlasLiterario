using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.API.IntegrationTests;

/// <summary>Deterministic stand-ins for Google Books / Open Library / Wikidata (no network in integration tests).</summary>
public static class FakeExternalSources
{
    public static readonly ExternalBookCandidate DomCasmurro = new(
        ExternalProvider.OpenLibrary, "OL1003040W", "Dom Casmurro", null,
        [new ExternalAuthorRef("Machado de Assis", "OL93286A")],
        null, 1899, null, null, "https://covers.openlibrary.org/b/id/647501-L.jpg", "Bentinho e Capitu.", [], null, "OL1003040W");

    public static readonly ExternalBookCandidate NorwegianWood = new(
        ExternalProvider.OpenLibrary, "OL2625457W", "Norwegian Wood", null,
        [new ExternalAuthorRef("村上春樹", "OL382524A")],
        null, 1987, null, null, null, null, [], null, "OL2625457W");

    public static IBookSearchProvider OpenLibrary { get; } = new FakeProvider(ExternalProvider.OpenLibrary, [DomCasmurro, NorwegianWood]);

    /// <summary>Google Books always over quota (exercises the degradation path).</summary>
    public static IBookSearchProvider GoogleBooks { get; } = new FakeProvider(ExternalProvider.GoogleBooks, null);

    public static IAuthorIdentityResolver AuthorResolver { get; } = new FakeAuthorResolver();

    public static IGeoResolver Geo { get; } = new FakeGeo();

    private sealed class FakeProvider(ExternalProvider provider, IReadOnlyList<ExternalBookCandidate>? catalog) : IBookSearchProvider
    {
        public ExternalProvider Provider => provider;

        public Task<IReadOnlyList<ExternalBookCandidate>> SearchAsync(BookSearchQuery query, CancellationToken ct) =>
            catalog is null
                ? throw new ExternalServiceUnavailableException("Google Books", "HTTP 429")
                : Task.FromResult<IReadOnlyList<ExternalBookCandidate>>(catalog
                    .Where(c => c.Title.Contains(query.Term, StringComparison.OrdinalIgnoreCase)
                                || c.Authors.Any(a => a.Name.Contains(query.Term, StringComparison.OrdinalIgnoreCase)))
                    .ToList());

        public Task<ExternalBookCandidate?> GetAsync(string externalId, CancellationToken ct) =>
            catalog is null
                ? throw new ExternalServiceUnavailableException("Google Books", "HTTP 429")
                : Task.FromResult(catalog.FirstOrDefault(c => c.ExternalId == externalId));
    }

    private sealed class FakeAuthorResolver : IAuthorIdentityResolver
    {
        private static readonly Dictionary<string, ResolvedAuthor> Authors = new()
        {
            ["OL93286A"] = new ResolvedAuthor("Machado de Assis", "OL93286A", "Q311145", null, new DateOnly(1839, 6, 21)),
            ["OL382524A"] = new ResolvedAuthor("村上春樹", "OL382524A", "Q134798", "108238901", new DateOnly(1949, 1, 12)),
        };

        public Task<IReadOnlyList<ResolvedAuthor>> ResolveAuthorsAsync(ExternalBookCandidate book, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ResolvedAuthor>>(book.Authors
                .Select(a => a.OpenLibraryKey is not null && Authors.TryGetValue(a.OpenLibraryKey, out var r)
                    ? r
                    : new ResolvedAuthor(a.Name, a.OpenLibraryKey, null, null, null))
                .ToList());
    }

    private sealed class FakeGeo : IGeoResolver
    {
        public Task<IReadOnlyList<ResolvedCountry>> GetAuthorCitizenshipsAsync(string authorQid, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ResolvedCountry>>(authorQid switch
            {
                "Q311145" => [new ResolvedCountry("BRA", GeoSource.Wikidata, 0.9m, "Q155", false)],
                "Q134798" => [new ResolvedCountry("JPN", GeoSource.Wikidata, 0.9m, "Q17", false)],
                _ => [],
            });

        public Task<IReadOnlyList<ResolvedCountry>> GetWorkOriginsAsync(string workQid, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ResolvedCountry>>(workQid == "Q1236531"
                ? [new ResolvedCountry("BRA", GeoSource.Wikidata, 0.9m, "Q155", false)]
                : []);

        public Task<string?> GetWorkOriginalLanguageAsync(string workQid, CancellationToken ct) =>
            Task.FromResult(workQid == "Q1236531" ? "pt" : null);

        public Task<string?> FindWorkQidAsync(string? isbn13, string? openLibraryWorkId, string title, string? authorQid, CancellationToken ct) =>
            Task.FromResult(openLibraryWorkId == "OL1003040W" ? "Q1236531" : null);
    }
}
