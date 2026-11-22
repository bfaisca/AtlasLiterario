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
