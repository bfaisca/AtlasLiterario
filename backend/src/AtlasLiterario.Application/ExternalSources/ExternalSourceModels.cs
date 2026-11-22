using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.ExternalSources;

public enum BookSearchType
{
    All = 0,
    Title = 1,
    Author = 2,
    Isbn = 3,
}

public sealed record BookSearchQuery(string Term, BookSearchType Type, int Limit = 20);

/// <summary>Author as reported by a bibliographic source (identity not resolved yet).</summary>
public sealed record ExternalAuthorRef(string Name, string? OpenLibraryKey);

/// <summary>Book candidate returned by a search provider. Nothing is persisted at this stage.</summary>
public sealed record ExternalBookCandidate(
    ExternalProvider Provider,
    string ExternalId,
    string Title,
    string? Subtitle,
    IReadOnlyList<ExternalAuthorRef> Authors,
    string? Publisher,
    int? PublicationYear,
    string? Isbn13,
    string? Isbn10,
    string? CoverUrl,
    string? Description,
    IReadOnlyList<string> Categories,
    // Language of the edition (never use it as the original language).
    string? EditionLanguage,
    string? OpenLibraryWorkId);

/// <summary>Author with identifiers resolved across sources.</summary>
public sealed record ResolvedAuthor(
    string Name,
    string? OpenLibraryKey,
    string? WikidataQid,
    string? ViafId,
    DateOnly? BirthDate);

/// <summary>Country resolved from a source, with provenance.</summary>
public sealed record ResolvedCountry(
    string IsoCode,
    GeoSource Source,
    decimal Confidence,
    string? SourceEntityQid,
    bool ViaHistoricalMapping);
