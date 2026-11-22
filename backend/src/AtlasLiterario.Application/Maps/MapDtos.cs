using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Maps;

/// <remarks><c>TotalReadBooks</c> counts the books of the applied <c>Status</c> filter (the name is kept for compatibility; with the default it is the Read total).</remarks>
public sealed record MapDataDto(
    MapType MapType,
    int TotalReadBooks,
    int ExploredCountries,
    int MaxBookCount,
    int UnknownBookCount,
    IReadOnlyList<CountryMapDatum> Countries,
    string Status);

/// <summary>Map-layer input, independent of the visual library.</summary>
/// <param name="Iso">= geoJsonId (ISO_A3).</param>
/// <param name="Name">Portuguese name.</param>
public sealed record CountryMapDatum(string Iso, string Name, Continent Continent, int BookCount, int AuthorCount);

public sealed record CountryDetailDto(
    MapType MapType,
    string Iso,
    string Name,
    Continent Continent,
    int BookCount,
    int AuthorCount,
    IReadOnlyList<CountryBookDto> Books,
    string Status,
    IReadOnlyList<CountryAuthorDto> Authors);

/// <summary>
/// Author card of the Authors-map country panel. <paramref name="Books"/> are the author's books inside the same
/// status-filtered set as the panel's <c>books</c> (same order, same user), never empty.
/// </summary>
public sealed record CountryAuthorDto(
    Guid AuthorId, string Name, string CountryIso, string CountryName, IReadOnlyList<CountryBookDto> Books);

public sealed record CountryBookDto(
    Guid LibraryItemId,
    Guid BookId,
    string Title,
    string? CoverUrl,
    IReadOnlyList<string> Authors,
    DateOnly? FinishedDate,
    int? Rating);

public sealed record CreateMapShareRequest(MapType MapType);

public sealed record MapShareDto(string Token, MapType MapType, string Url, DateTimeOffset CreatedAt);

public sealed record PublicMapDto(
    string DisplayName,
    MapType MapType,
    int TotalReadBooks,
    int ExploredCountries,
    int MaxBookCount,
    IReadOnlyList<PublicCountryMapDatum> Countries);

/// <summary>Pick&lt;CountryMapDatum, "iso" | "name" | "continent" | "bookCount"&gt;.</summary>
public sealed record PublicCountryMapDatum(string Iso, string Name, Continent Continent, int BookCount);
