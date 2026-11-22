using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Maps;

/// <summary>
/// Pure aggregation of the literary maps (docs/business-rules.md §3). No I/O.
/// <list type="bullet">
/// <item>R = distinct books of the user with status Read.</item>
/// <item>A book counts once per country, regardless of how many of its authors share that country.</item>
/// <item>An author with several citizenships makes the book count in each of those countries.</item>
/// <item>Books without any known country go to <c>unknownBookCount</c> and never reduce other counts.</item>
/// </list>
/// </summary>
public static class MapAggregator
{
    public static MapDataDto Build(MapType mapType, MapAggregationInput input) => mapType switch
    {
        MapType.Authors => BuildAuthorsMap(input),
        MapType.Origins => BuildOriginsMap(input),
        _ => throw new ArgumentOutOfRangeException(nameof(mapType), mapType, "Unknown map type."),
    };

    /// <summary>Authors map: countries of citizenship of the authors of each read book.</summary>
    public static MapDataDto BuildAuthorsMap(MapAggregationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var readBooks = GetReadBookIds(input);
        var authorsByBook = GetAuthorsByBook(input, readBooks);
        var citizenshipsByAuthor = input.AuthorCitizenships
            .GroupBy(r => r.AuthorId)
            .ToDictionary(g => g.Key, g => DistinctByIso(g.Select(r => r.Country)));

        var accumulator = new Accumulator();
        var unknown = 0;

        foreach (var bookId in readBooks)
        {
            var bookHasCountry = false;
            foreach (var authorId in authorsByBook.GetValueOrDefault(bookId, []))
            {
                if (!citizenshipsByAuthor.TryGetValue(authorId, out var countries))
                {
                    continue;
                }

                foreach (var country in countries)
                {
                    accumulator.Add(country, bookId, authorId);
                    bookHasCountry = true;
                }
            }

            if (!bookHasCountry)
            {
                unknown++;
            }
        }

        return accumulator.ToMapData(MapType.Authors, readBooks.Count, unknown);
    }

    /// <summary>Origins map: literary countries of origin of each read book.</summary>
    public static MapDataDto BuildOriginsMap(MapAggregationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var readBooks = GetReadBookIds(input);
        var authorsByBook = GetAuthorsByBook(input, readBooks);
        var originsByBook = input.BookOrigins
            .Where(r => readBooks.Contains(r.BookId))
            .GroupBy(r => r.BookId)
            .ToDictionary(g => g.Key, g => DistinctByIso(g.Select(r => r.Country)));

        var accumulator = new Accumulator();
        var unknown = 0;

        foreach (var bookId in readBooks)
        {
            if (!originsByBook.TryGetValue(bookId, out var countries) || countries.Count == 0)
            {
                unknown++;
                continue;
            }

            var authors = authorsByBook.GetValueOrDefault(bookId, []);
            foreach (var country in countries)
            {
                accumulator.Add(country, bookId, authors);
            }
        }

        return accumulator.ToMapData(MapType.Origins, readBooks.Count, unknown);
    }

    /// <summary>
    /// Ids of the read books that count for <paramref name="iso"/> on the given map (same rules as <see cref="Build"/>).
    /// </summary>
    public static IReadOnlyCollection<Guid> GetBookIdsForCountry(MapType mapType, MapAggregationInput input, string iso)
    {
        ArgumentNullException.ThrowIfNull(input);
        var readBooks = GetReadBookIds(input);
        switch (mapType)
        {
            case MapType.Authors:
                var authorsByBook = GetAuthorsByBook(input, readBooks);
                var authorsInCountry = input.AuthorCitizenships
                    .Where(r => string.Equals(r.Country.Iso, iso, StringComparison.Ordinal))
                    .Select(r => r.AuthorId)
                    .ToHashSet();
                return readBooks.Where(b => authorsByBook.GetValueOrDefault(b, []).Overlaps(authorsInCountry)).ToList();
            case MapType.Origins:
                return input.BookOrigins
                    .Where(r => readBooks.Contains(r.BookId) && string.Equals(r.Country.Iso, iso, StringComparison.Ordinal))
                    .Select(r => r.BookId)
                    .Distinct()
                    .ToList();
            default:
                throw new ArgumentOutOfRangeException(nameof(mapType), mapType, "Unknown map type.");
        }
    }

    /// <summary>|countries(authors) ∪ countries(origins)| — each country counted once.</summary>
    public static int CountDistinctCountries(MapDataDto authorsMap, MapDataDto originsMap) =>
        authorsMap.Countries.Select(c => c.Iso)
            .Union(originsMap.Countries.Select(c => c.Iso), StringComparer.Ordinal)
            .Count();

    /// <summary>Distinct continents of the countries present in the given maps.</summary>
    public static int CountContinents(params MapDataDto[] maps) =>
        maps.SelectMany(m => m.Countries).Select(c => c.Continent).Distinct().Count();

    private static HashSet<Guid> GetReadBookIds(MapAggregationInput input) =>
        input.LibraryBooks
            .Where(b => b.Status == ReadingStatus.Read)
            .Select(b => b.BookId)
            .ToHashSet();

    private static Dictionary<Guid, HashSet<Guid>> GetAuthorsByBook(MapAggregationInput input, HashSet<Guid> readBooks) =>
        input.BookAuthors
            .Where(r => readBooks.Contains(r.BookId))
            .GroupBy(r => r.BookId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.AuthorId).ToHashSet());

    private static List<MapCountry> DistinctByIso(IEnumerable<MapCountry> countries) =>
        countries.DistinctBy(c => c.Iso, StringComparer.Ordinal).ToList();

    private sealed class Accumulator
    {
        private readonly Dictionary<string, (MapCountry Country, HashSet<Guid> Books, HashSet<Guid> Authors)> _byIso =
            new(StringComparer.Ordinal);

        public void Add(MapCountry country, Guid bookId, Guid authorId) => Add(country, bookId, [authorId]);

        public void Add(MapCountry country, Guid bookId, IEnumerable<Guid> authorIds)
        {
            if (!_byIso.TryGetValue(country.Iso, out var entry))
            {
                entry = (country, [], []);
                _byIso[country.Iso] = entry;
            }

            entry.Books.Add(bookId);
            entry.Authors.UnionWith(authorIds);
        }

        public MapDataDto ToMapData(MapType mapType, int totalReadBooks, int unknownBookCount)
        {
            var countries = _byIso.Values
                .Select(e => new CountryMapDatum(
                    e.Country.Iso, e.Country.Name, e.Country.Continent, e.Books.Count, e.Authors.Count))
                .OrderByDescending(c => c.BookCount)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();

            return new MapDataDto(
                mapType,
                totalReadBooks,
                countries.Count,
                countries.Count == 0 ? 0 : countries.Max(c => c.BookCount),
                unknownBookCount,
                countries);
        }
    }
}
