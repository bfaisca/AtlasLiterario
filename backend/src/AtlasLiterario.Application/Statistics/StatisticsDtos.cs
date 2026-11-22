using AtlasLiterario.Application.Maps;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Statistics;

public sealed record DashboardDto(
    int TotalReadBooks,
    int TotalAuthors,
    int CountriesByAuthors,
    int CountriesByOrigins,
    int DistinctCountries,
    int ContinentsExplored,
    int ReadingNow,
    int WantToRead,
    IReadOnlyList<CountryBookDto> RecentlyFinished,
    IReadOnlyList<TopCountryDto> TopCountries,
    PassportProgressDto PassportProgress);

public sealed record TopCountryDto(string Iso, string Name, int BookCount, MapType MapType);

/// <param name="Percentage">0–100, one decimal place.</param>
public sealed record PassportProgressDto(int ExploredCountries, int TotalCountries, double Percentage);

public sealed record PassportsDto(
    PassportMapStatsDto Authors,
    PassportMapStatsDto Origins,
    int DistinctCountries,
    IReadOnlyList<ContinentProgressDto> Continents,
    IReadOnlyList<TimelinePointDto> Timeline,
    PassportProgressDto Progress);

public sealed record PassportMapStatsDto(
    int ExploredCountries,
    TopCountryDto? TopCountry,
    IReadOnlyList<CountryMapDatum> BooksByCountry,
    int UnknownBookCount);

public sealed record ContinentProgressDto(Continent Continent, int ExploredCountries, int TotalCountries);

/// <param name="Month">"YYYY-MM".</param>
public sealed record TimelinePointDto(
    string Month,
    int BooksFinished,
    int CumulativeBooks,
    int CumulativeCountriesAuthors,
    int CumulativeCountriesOrigins,
    int CumulativeDistinctCountries);
