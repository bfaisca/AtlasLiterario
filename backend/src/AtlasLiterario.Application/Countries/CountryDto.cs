using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Countries;

/// <param name="Iso">ISO 3166-1 alpha-3.</param>
/// <param name="Iso2">ISO 3166-1 alpha-2.</param>
/// <param name="Name">English name.</param>
/// <param name="NamePt">Portuguese name.</param>
public sealed record CountryDto(string Iso, string Iso2, string Name, string NamePt, Continent Continent, string GeoJsonId);
