using AtlasLiterario.Application.Common;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Authors;

public sealed record AuthorDetailsDto(
    Guid Id,
    string Name,
    DateOnly? BirthDate,
    IReadOnlyList<AuthorCountryDto> Countries,
    IReadOnlyList<ExternalSourceRef> ExternalIds);

/// <summary>GeoRelationDto + relationship (flattened).</summary>
public sealed record AuthorCountryDto(
    string CountryIso,
    string CountryName,
    GeoSource Source,
    decimal Confidence,
    bool IsVerified,
    AuthorCountryRelationship Relationship);

/// <summary>Replaces the Citizenship relations of the author (Source = Manual).</summary>
public sealed record UpdateAuthorCountriesRequest(IReadOnlyList<string> CitizenshipIsos);

/// <summary>Autocomplete suggestion of the book form.</summary>
/// <param name="NationalityIsos">Citizenship only (ISO 3166-1 alpha-3); empty = unknown.</param>
/// <param name="NationalityNames">Portuguese names, same order as <paramref name="NationalityIsos"/>.</param>
public sealed record AuthorSuggestionDto(
    Guid Id,
    string Name,
    IReadOnlyList<string> NationalityIsos,
    IReadOnlyList<string> NationalityNames);

/// <summary>An author record inside a group of probable duplicates (moderator tool).</summary>
/// <param name="BookCount">Catalog books linked to the author (all users).</param>
/// <param name="NationalityIsos">Citizenship only (ISO 3166-1 alpha-3).</param>
public sealed record AuthorDuplicateDto(
    Guid Id,
    string Name,
    int BookCount,
    IReadOnlyList<string> NationalityIsos,
    DateTimeOffset CreatedAt);

/// <summary>Authors sharing the same normalized name (at least two), most books first, then oldest.</summary>
public sealed record AuthorDuplicateGroupDto(string NormalizedName, IReadOnlyList<AuthorDuplicateDto> Authors);

/// <summary>Merges <paramref name="SourceIds"/> (1–50 distinct ids, never the target) into <paramref name="TargetId"/>.</summary>
public sealed record MergeAuthorsRequest(Guid TargetId, IReadOnlyList<Guid> SourceIds);

/// <param name="MergedAuthors">Removed records.</param>
/// <param name="BooksRelinked">Book links moved to the target.</param>
/// <param name="BooksAlreadyLinked">Links discarded because the target already had the book.</param>
/// <param name="CountriesAdded">Country relations brought to the target.</param>
public sealed record AuthorMergeResultDto(
    Guid TargetId, int MergedAuthors, int BooksRelinked, int BooksAlreadyLinked, int CountriesAdded);
