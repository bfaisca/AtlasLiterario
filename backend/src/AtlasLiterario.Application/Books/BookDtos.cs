using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Books;

public sealed record BookSearchResultDto(
    string ResultKey,
    Guid? BookId,
    bool InLibrary,
    Guid? LibraryItemId,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    string? Publisher,
    int? PublicationYear,
    string? Isbn13,
    string? Isbn10,
    string? CoverUrl,
    string? Description,
    ExternalSourceRef Source);

public sealed record BookDetailsDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string? Isbn13,
    string? Isbn10,
    string? Publisher,
    int? PublicationYear,
    string? Description,
    string? CoverUrl,
    string? OriginalLanguage,
    IReadOnlyList<string> Genres,
    IReadOnlyList<AuthorSummaryDto> Authors,
    IReadOnlyList<GeoRelationDto> OriginCountries,
    GeoEnrichmentStatus GeoEnrichmentStatus,
    IReadOnlyList<ExternalSourceRef> ExternalIds);

/// <param name="Nationalities">Citizenship only; empty = unknown.</param>
public sealed record AuthorSummaryDto(Guid Id, string Name, IReadOnlyList<GeoRelationDto> Nationalities);

/// <param name="CountryIso">ISO 3166-1 alpha-3.</param>
/// <param name="CountryName">Portuguese name.</param>
public sealed record GeoRelationDto(
    string CountryIso,
    string CountryName,
    GeoSource Source,
    decimal Confidence,
    bool IsVerified);

/// <summary>Manual creation of a book (also used by PUT /books/{id}).</summary>
public sealed record CreateBookRequest(
    string Title,
    string? Subtitle,
    string? Isbn13,
    string? Isbn10,
    string? Publisher,
    int? PublicationYear,
    string? Description,
    string? CoverUrl,
    string? OriginalLanguage,
    IReadOnlyList<string>? Genres,
    IReadOnlyList<string> AuthorNames,
    IReadOnlyList<string>? OriginCountryIsos);

/// <summary>Replaces the origins of a work (Source = Manual).</summary>
public sealed record UpdateOriginCountriesRequest(IReadOnlyList<string> CountryIsos);
