using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Maps;

public sealed record MapDataDto(
    MapType MapType,
    int TotalReadBooks,
    int ExploredCountries,
    int MaxBookCount,
    int UnknownBookCount,
    IReadOnlyList<CountryMapDatum> Countries);

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
    IReadOnlyList<CountryBookDto> Books);

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
