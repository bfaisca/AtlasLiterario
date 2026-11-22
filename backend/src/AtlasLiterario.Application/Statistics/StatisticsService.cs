using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Statistics;

public interface IStatisticsService
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct);

    Task<PassportsDto> GetPassportsAsync(CancellationToken ct);
}

/// <summary>
/// Dashboard and Literary Passports of the authenticated user (docs/business-rules.md §3, §9). Always Read books only
/// (the maps' status filter does not apply). Country counts come from <see cref="MapAggregator"/>, so a book counts once
/// per country, an author with several citizenships counts in each, and a country counts once per map.
/// Missing data is "unknown" (<c>unknownBookCount</c>) and never reduces any count.
/// </summary>
public sealed class StatisticsService(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IStatisticsService
{
    internal const int RecentLimit = 6;
    internal const int TopLimit = 5;

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        var (authorsMap, originsMap, input) = await LoadMapsAsync(userId, ct);
        var totalCountries = await db.Countries.AsNoTracking().CountAsync(ct);

        var byStatus = await db.UserBooks.AsNoTracking()
            .Where(ub => ub.UserId == userId && (ub.Status == ReadingStatus.Reading || ub.Status == ReadingStatus.WantToRead))
            .GroupBy(ub => ub.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var recent = CountryBookQuery.Order(await CountryBookQuery.ToDtosAsync(
            db.UserBooks.AsNoTracking()
                .Where(ub => ub.UserId == userId && ub.Status == ReadingStatus.Read)
                .OrderBy(ub => ub.FinishedDate == null)
                .ThenByDescending(ub => ub.FinishedDate)
                .ThenBy(ub => ub.Book!.Title)
                .ThenBy(ub => ub.Id)
                .Take(RecentLimit),
            ct));

        var distinct = MapAggregator.CountDistinctCountries(authorsMap, originsMap);
        return new DashboardDto(
            TotalReadBooks: input.LibraryBooks.Count,
            TotalAuthors: input.BookAuthors.Select(r => r.AuthorId).Distinct().Count(),
            CountriesByAuthors: authorsMap.ExploredCountries,
            CountriesByOrigins: originsMap.ExploredCountries,
            DistinctCountries: distinct,
            ContinentsExplored: MapAggregator.CountContinents(authorsMap, originsMap),
            ReadingNow: byStatus.FirstOrDefault(x => x.Status == ReadingStatus.Reading)?.Count ?? 0,
            WantToRead: byStatus.FirstOrDefault(x => x.Status == ReadingStatus.WantToRead)?.Count ?? 0,
            RecentlyFinished: recent,
            TopCountries: [.. Top(authorsMap, MapType.Authors), .. Top(originsMap, MapType.Origins)],
            PassportProgress: Progress(distinct, totalCountries));
    }

    public async Task<PassportsDto> GetPassportsAsync(CancellationToken ct)
    {
        var userId = currentUser.GetRequiredUserId();
        var (authorsMap, originsMap, input) = await LoadMapsAsync(userId, ct);

        var countriesByContinent = await db.Countries.AsNoTracking()
            .GroupBy(c => c.Continent)
            .Select(g => new { Continent = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Continent, x => x.Count, ct);

        var union = authorsMap.Countries.Concat(originsMap.Countries)
            .DistinctBy(c => c.Iso, StringComparer.Ordinal)
            .ToList();
        var exploredByContinent = union.GroupBy(c => c.Continent).ToDictionary(g => g.Key, g => g.Count());

        var continents = Enum.GetValues<Continent>()
            .Where(c => c != Continent.Antarctica)
            .Select(c => new ContinentProgressDto(c, exploredByContinent.GetValueOrDefault(c), countriesByContinent.GetValueOrDefault(c)))
            .ToList();

        return new PassportsDto(
            Authors: MapStats(authorsMap, MapType.Authors),
            Origins: MapStats(originsMap, MapType.Origins),
            DistinctCountries: union.Count,
            Continents: continents,
            Timeline: BuildTimeline(input, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)),
            Progress: Progress(union.Count, countriesByContinent.Values.Sum()));
    }

    /// <summary>
    /// 0–100 with one decimal (commercial rounding, half away from zero). Never divides by zero.
    /// </summary>
    public static double Percentage(int explored, int total) =>
        total <= 0 ? 0 : (double)Math.Round(explored * 100m / total, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Monthly series from the month of the first finished book to the current month (or the last finished month, if later),
    /// with no gaps. Cumulative figures are computed with the maps' own rules over the books finished up to each month.
    /// Read books without <c>finishedDate</c> are not placed on the timeline (they still count in every total).
    /// </summary>
    internal static IReadOnlyList<TimelinePointDto> BuildTimeline(MapAggregationInput input, DateOnly today)
    {
        var dated = input.LibraryBooks.Where(b => b.FinishedDate is not null).ToList();
        if (dated.Count == 0)
        {
            return [];
        }

        var perMonth = dated
            .GroupBy(b => new DateOnly(b.FinishedDate!.Value.Year, b.FinishedDate.Value.Month, 1))
            .ToDictionary(g => g.Key, g => g.Count());
        var first = perMonth.Keys.Min();
        var last = new DateOnly(today.Year, today.Month, 1);
        if (perMonth.Keys.Max() > last)
        {
            last = perMonth.Keys.Max();
        }

        var points = new List<TimelinePointDto>();
        var cumulativeBooks = 0;
        (int Authors, int Origins, int Distinct) countries = (0, 0, 0);
        for (var month = first; month <= last; month = month.AddMonths(1))
        {
            var finished = perMonth.GetValueOrDefault(month);
            if (finished > 0)
            {
                // Countries only change in months where a book was finished; empty months repeat the last values.
                cumulativeBooks += finished;
                var upToMonth = dated.Where(b => b.FinishedDate!.Value <= month.AddMonths(1).AddDays(-1)).ToList();
                var snapshot = input with { LibraryBooks = upToMonth };
                var authors = MapAggregator.BuildAuthorsMap(snapshot);
                var origins = MapAggregator.BuildOriginsMap(snapshot);
                countries = (authors.ExploredCountries, origins.ExploredCountries, MapAggregator.CountDistinctCountries(authors, origins));
            }

            points.Add(new TimelinePointDto(
                $"{month.Year:D4}-{month.Month:D2}", finished, cumulativeBooks, countries.Authors, countries.Origins, countries.Distinct));
        }

        return points;
    }

    private Task<(MapDataDto Authors, MapDataDto Origins, MapAggregationInput Input)> LoadMapsAsync(Guid userId, CancellationToken ct) =>
        LoadMapsAsync(db, userId, ct);

    /// <summary>Read-only maps of any owner (also used by the public profiles, which pass the profile owner's id).</summary>
    internal static async Task<(MapDataDto Authors, MapDataDto Origins, MapAggregationInput Input)> LoadMapsAsync(
        IAppDbContext db, Guid userId, CancellationToken ct)
    {
        var input = await MapInputLoader.LoadAsync(
            db, userId, MapStatusFilter.Read, includeAuthors: true, includeOrigins: true, includeAuthorNames: false, ct);
        return (MapAggregator.BuildAuthorsMap(input), MapAggregator.BuildOriginsMap(input), input);
    }

    private static PassportProgressDto Progress(int explored, int totalCountries) =>
        new(explored, totalCountries, Percentage(explored, totalCountries));

    private static IEnumerable<TopCountryDto> Top(MapDataDto map, MapType type) =>
        map.Countries.Take(TopLimit).Select(c => new TopCountryDto(c.Iso, c.Name, c.BookCount, type));

    private static PassportMapStatsDto MapStats(MapDataDto map, MapType type) =>
        new(map.ExploredCountries, Top(map, type).FirstOrDefault(), map.Countries, map.UnknownBookCount);
}
