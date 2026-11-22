using AtlasLiterario.Application.Statistics;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Tests.Statistics;

public sealed class StatisticsServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2025, 3, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDb.Create();
    private readonly FakeCurrentUser _user = new() { UserId = Guid.NewGuid() };
    private readonly FakeClock _clock = new();
    private Dictionary<string, short> _countryIds = [];

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();
        _countryIds = await _db.Countries.ToDictionaryAsync(c => c.IsoCode, c => c.Id);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private StatisticsService Stats() => new(_db, _user, _clock);

    private Author AddAuthor(string name, params string[] citizenships)
    {
        var author = new Author(name, Now);
        _db.Authors.Add(author);
        foreach (var iso in citizenships)
        {
            _db.AuthorCountries.Add(new AuthorCountry(
                author.Id, _countryIds[iso], AuthorCountryRelationship.Citizenship, GeoSource.Wikidata, 0.9m, false, null, Now));
        }

        return author;
    }

    private Book AddBook(string title, Author[] authors, params string[] origins)
    {
        var book = new Book(title, Now);
        _db.Books.Add(book);
        for (short i = 0; i < authors.Length; i++)
        {
            _db.BookAuthors.Add(new BookAuthor(book.Id, authors[i].Id, i));
        }

        foreach (var iso in origins)
        {
            _db.BookOriginCountries.Add(new BookOriginCountry(book.Id, _countryIds[iso], GeoSource.Wikidata, 0.9m, false, null, Now));
        }

        return book;
    }

    private void Shelve(Book book, ReadingStatus status, DateOnly? finished = null, Guid? userId = null)
    {
        var item = new UserBook(userId ?? _user.UserId!.Value, book.Id, status, null, null, null, finished, Now);
        _db.UserBooks.Add(item);
        if (status == ReadingStatus.Read && finished is null)
        {
            // The domain fills "today" for Read; simulate legacy data without a finished date.
            typeof(UserBook).GetProperty(nameof(UserBook.FinishedDate))!.SetValue(item, null);
        }
    }

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public async Task User_without_books_gets_zeros_empty_timeline_and_zero_percentage()
    {
        var dashboard = await Stats().GetDashboardAsync(default);
        var passports = await Stats().GetPassportsAsync(default);

        dashboard.TotalReadBooks.ShouldBe(0);
        dashboard.TotalAuthors.ShouldBe(0);
        dashboard.DistinctCountries.ShouldBe(0);
        dashboard.ContinentsExplored.ShouldBe(0);
        dashboard.RecentlyFinished.ShouldBeEmpty();
        dashboard.TopCountries.ShouldBeEmpty();
        dashboard.PassportProgress.Percentage.ShouldBe(0);
        dashboard.PassportProgress.TotalCountries.ShouldBe(250);
        passports.Timeline.ShouldBeEmpty();
        passports.Authors.TopCountry.ShouldBeNull();
        passports.Authors.BooksByCountry.ShouldBeEmpty();
        passports.Progress.Percentage.ShouldBe(0);
        passports.Continents.Select(c => c.Continent).ShouldNotContain(Continent.Antarctica);
        passports.Continents.Count.ShouldBe(6);
        passports.Continents.ShouldAllBe(c => c.ExploredCountries == 0 && c.TotalCountries > 0);
    }

    [Fact]
    public async Task Only_read_books_of_the_logged_user_count_and_other_statuses_are_counted_separately()
    {
        var other = Guid.NewGuid();
        var machado = AddAuthor("Machado", "BRA");
        var kafka = AddAuthor("Kafka", "AUT");
        Shelve(AddBook("Dom Casmurro", [machado], "BRA"), ReadingStatus.Read, D(2025, 1, 5));
        Shelve(AddBook("Querido", [kafka], "AUT"), ReadingStatus.Reading);
        Shelve(AddBook("Desejado", [kafka], "AUT"), ReadingStatus.WantToRead);
        Shelve(AddBook("Desejado 2", [kafka], "AUT"), ReadingStatus.WantToRead);
        Shelve(AddBook("Largado", [kafka], "AUT"), ReadingStatus.Abandoned);
        var foreign = AddAuthor("Murakami", "JPN");
        Shelve(AddBook("Alheio", [foreign], "JPN"), ReadingStatus.Read, D(2025, 2, 1), other);
        await _db.SaveChangesAsync();

        var dashboard = await Stats().GetDashboardAsync(default);

        dashboard.TotalReadBooks.ShouldBe(1);
        dashboard.TotalAuthors.ShouldBe(1);
        dashboard.CountriesByAuthors.ShouldBe(1);
        dashboard.DistinctCountries.ShouldBe(1);
        dashboard.ReadingNow.ShouldBe(1);
        dashboard.WantToRead.ShouldBe(2);
        dashboard.RecentlyFinished.ShouldHaveSingleItem().Title.ShouldBe("Dom Casmurro");
        dashboard.TopCountries.ShouldAllBe(t => t.Iso == "BRA");

        _user.UserId = other;
        var theirs = await Stats().GetDashboardAsync(default);
        theirs.TotalReadBooks.ShouldBe(1);
        theirs.TopCountries.ShouldAllBe(t => t.Iso == "JPN");
        theirs.ReadingNow.ShouldBe(0);
    }

    [Fact]
    public async Task Author_with_two_nationalities_and_work_with_two_origins_count_in_each_country_and_a_country_counts_once()
    {
        var kafka = AddAuthor("Kafka", "AUT", "CZE");
        var coauthored = AddAuthor("Coautor", "AUT");
        // Two authors from AUT on the same book: the book counts once in AUT.
        Shelve(AddBook("O Processo", [kafka, coauthored], "DEU", "AUT"), ReadingStatus.Read, D(2025, 1, 1));
        Shelve(AddBook("A Metamorfose", [kafka], "DEU"), ReadingStatus.Read, D(2025, 2, 1));
        await _db.SaveChangesAsync();

        var p = await Stats().GetPassportsAsync(default);
        var d = await Stats().GetDashboardAsync(default);

        p.Authors.ExploredCountries.ShouldBe(2);
        p.Authors.BooksByCountry.Single(c => c.Iso == "AUT").BookCount.ShouldBe(2);
        p.Authors.BooksByCountry.Single(c => c.Iso == "AUT").AuthorCount.ShouldBe(2);
        p.Authors.BooksByCountry.Single(c => c.Iso == "CZE").BookCount.ShouldBe(2);
        p.Origins.ExploredCountries.ShouldBe(2);
        p.Origins.BooksByCountry.Single(c => c.Iso == "DEU").BookCount.ShouldBe(2);
        p.Origins.BooksByCountry.Single(c => c.Iso == "AUT").BookCount.ShouldBe(1);
        // Union AUT, CZE, DEU: AUT is in both maps but counted once.
        p.DistinctCountries.ShouldBe(3);
        d.DistinctCountries.ShouldBe(3);
        d.TotalAuthors.ShouldBe(2);
        d.TotalReadBooks.ShouldBe(2);
        p.Progress.ExploredCountries.ShouldBe(3);
    }

    [Fact]
    public async Task Books_without_known_countries_are_unknown_and_never_reduce_counts()
    {
        var nobody = AddAuthor("Sem país");
        var machado = AddAuthor("Machado", "BRA");
        Shelve(AddBook("Desconhecido", [nobody]), ReadingStatus.Read, D(2025, 1, 1));
        Shelve(AddBook("Sem autor", []), ReadingStatus.Read, D(2025, 1, 2));
        Shelve(AddBook("Conhecido", [machado], "BRA"), ReadingStatus.Read, D(2025, 1, 3));
        await _db.SaveChangesAsync();

        var p = await Stats().GetPassportsAsync(default);

        p.Authors.UnknownBookCount.ShouldBe(2);
        p.Origins.UnknownBookCount.ShouldBe(2);
        p.Authors.ExploredCountries.ShouldBe(1);
        (await Stats().GetDashboardAsync(default)).TotalReadBooks.ShouldBe(3);
    }

    [Fact]
    public async Task Continents_list_excludes_antarctica_keeps_zeros_and_uses_the_union()
    {
        var machado = AddAuthor("Machado", "BRA");
        Shelve(AddBook("A", [machado], "FRA", "BRA"), ReadingStatus.Read, D(2025, 1, 1));
        await _db.SaveChangesAsync();

        var p = await Stats().GetPassportsAsync(default);
        var d = await Stats().GetDashboardAsync(default);

        p.Continents.Select(c => c.Continent).ShouldBe(
            [Continent.Africa, Continent.Asia, Continent.Europe, Continent.NorthAmerica, Continent.SouthAmerica, Continent.Oceania]);
        p.Continents.Single(c => c.Continent == Continent.SouthAmerica).ExploredCountries.ShouldBe(1);
        p.Continents.Single(c => c.Continent == Continent.Europe).ExploredCountries.ShouldBe(1);
        p.Continents.Single(c => c.Continent == Continent.Africa).ExploredCountries.ShouldBe(0);
        p.Continents.Sum(c => c.TotalCountries).ShouldBe(245); // 250 minus the 5 Antarctica-continent entries
        d.ContinentsExplored.ShouldBe(2);
    }

    [Fact]
    public async Task Timeline_has_no_gaps_repeats_cumulatives_and_runs_until_the_current_month()
    {
        var kafka = AddAuthor("Kafka", "AUT");
        var machado = AddAuthor("Machado", "BRA");
        Shelve(AddBook("A", [kafka], "DEU"), ReadingStatus.Read, D(2024, 11, 30));
        Shelve(AddBook("B", [kafka], "DEU"), ReadingStatus.Read, D(2024, 11, 2));
        Shelve(AddBook("C", [machado], "BRA"), ReadingStatus.Read, D(2025, 1, 15));
        await _db.SaveChangesAsync();

        var t = (await Stats().GetPassportsAsync(default)).Timeline;

        t.Select(x => x.Month).ShouldBe(["2024-11", "2024-12", "2025-01", "2025-02", "2025-03"]);
        t.Select(x => x.BooksFinished).ShouldBe([2, 0, 1, 0, 0]);
        t.Select(x => x.CumulativeBooks).ShouldBe([2, 2, 3, 3, 3]);
        t.Select(x => x.CumulativeCountriesAuthors).ShouldBe([1, 1, 2, 2, 2]);
        t.Select(x => x.CumulativeCountriesOrigins).ShouldBe([1, 1, 2, 2, 2]);
        // AUT, DEU, then BRA (author and origin) -> 3 distinct.
        t.Select(x => x.CumulativeDistinctCountries).ShouldBe([2, 2, 3, 3, 3]);
    }

    [Fact]
    public async Task Read_book_without_finished_date_counts_in_totals_but_not_in_the_timeline()
    {
        var machado = AddAuthor("Machado", "BRA");
        var kafka = AddAuthor("Kafka", "AUT");
        Shelve(AddBook("Datado", [kafka], "AUT"), ReadingStatus.Read, D(2025, 2, 10));
        Shelve(AddBook("Sem data", [machado], "BRA"), ReadingStatus.Read);
        await _db.SaveChangesAsync();

        var d = await Stats().GetDashboardAsync(default);
        var p = await Stats().GetPassportsAsync(default);

        d.TotalReadBooks.ShouldBe(2);
        p.DistinctCountries.ShouldBe(2);
        p.Timeline.Select(x => x.Month).ShouldBe(["2025-02", "2025-03"]);
        p.Timeline.Last().CumulativeBooks.ShouldBe(1);
        p.Timeline.Last().CumulativeDistinctCountries.ShouldBe(1);
        d.RecentlyFinished.Select(b => b.Title).ShouldBe(["Datado", "Sem data"]); // undated last
    }

    [Fact]
    public async Task Recently_finished_is_limited_to_six_newest_and_top_countries_to_five_per_map_ordered()
    {
        var authors = new[] { "BRA", "ARG", "CHL", "COL", "PER", "URY", "VEN" }.Select(iso => AddAuthor($"A-{iso}", iso)).ToArray();
        for (var i = 0; i < 7; i++)
        {
            // Country i has (7 - i) books so the counts are strictly decreasing.
            for (var n = 0; n < 7 - i; n++)
            {
                Shelve(AddBook($"L{i}-{n}", [authors[i]], authors[i].Name[2..]), ReadingStatus.Read, D(2025, 1, 1).AddDays(i * 3 + n));
            }
        }

        await _db.SaveChangesAsync();

        var d = await Stats().GetDashboardAsync(default);

        d.RecentlyFinished.Count.ShouldBe(6);
        d.RecentlyFinished.Select(b => b.FinishedDate).ShouldBe(
            d.RecentlyFinished.Select(b => b.FinishedDate).OrderByDescending(x => x));
        var topAuthors = d.TopCountries.Where(t => t.MapType == MapType.Authors).ToList();
        topAuthors.Select(t => t.Iso).ShouldBe(["BRA", "ARG", "CHL", "COL", "PER"]);
        topAuthors.Select(t => t.BookCount).ShouldBe([7, 6, 5, 4, 3]);
        d.TopCountries.Count(t => t.MapType == MapType.Origins).ShouldBe(5);
        var passports = await Stats().GetPassportsAsync(default);
        passports.Authors.BooksByCountry.Select(c => c.BookCount).ShouldBe(passports.Authors.BooksByCountry.Select(c => c.BookCount).OrderByDescending(x => x));
        passports.Authors.TopCountry!.Iso.ShouldBe("BRA");
        passports.Authors.BooksByCountry.Count.ShouldBe(7);
    }

    [Theory]
    [InlineData(0, 250, 0.0)]
    [InlineData(1, 250, 0.4)]
    [InlineData(1, 16, 6.3)]   // 6.25 -> half away from zero
    [InlineData(1, 3, 33.3)]
    [InlineData(2, 3, 66.7)]
    [InlineData(250, 250, 100.0)]
    [InlineData(5, 0, 0.0)]    // no division by zero
    public void Percentage_has_one_decimal_with_commercial_rounding(int explored, int total, double expected) =>
        StatisticsService.Percentage(explored, total).ShouldBe(expected);
}
