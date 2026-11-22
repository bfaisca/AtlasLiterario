using AtlasLiterario.Application.Maps;
using AtlasLiterario.Domain.Enums;

namespace AtlasLiterario.Application.Tests.Maps;

public sealed class MapAggregatorTests
{
    private static readonly MapCountry Austria = new("AUT", "Áustria", Continent.Europe);
    private static readonly MapCountry Czechia = new("CZE", "Tchéquia", Continent.Europe);
    private static readonly MapCountry Colombia = new("COL", "Colômbia", Continent.SouthAmerica);
    private static readonly MapCountry Japan = new("JPN", "Japão", Continent.Asia);
    private static readonly MapCountry Brazil = new("BRA", "Brasil", Continent.SouthAmerica);
    private static readonly MapCountry UnitedKingdom = new("GBR", "Reino Unido", Continent.Europe);

    private sealed class InputBuilder
    {
        private readonly List<LibraryBookRow> _library = [];
        private readonly List<BookAuthorRow> _bookAuthors = [];
        private readonly List<AuthorCitizenshipRow> _citizenships = [];
        private readonly List<BookOriginRow> _origins = [];

        public Guid Author(params MapCountry[] citizenships)
        {
            var id = Guid.NewGuid();
            _citizenships.AddRange(citizenships.Select(c => new AuthorCitizenshipRow(id, c)));
            return id;
        }

        public Guid Book(ReadingStatus status, Guid[] authors, params MapCountry[] origins)
        {
            var id = Guid.NewGuid();
            _library.Add(new LibraryBookRow(id, status));
            _bookAuthors.AddRange(authors.Select(a => new BookAuthorRow(id, a)));
            _origins.AddRange(origins.Select(c => new BookOriginRow(id, c)));
            return id;
        }

        public MapAggregationInput Build() => new(_library, _bookAuthors, _citizenships, _origins);

        public MapAggregationInput BuildWithNames(Dictionary<Guid, string> names) =>
            new(_library, _bookAuthors, _citizenships, _origins,
                names.Select(n => new AuthorNameRow(n.Key, n.Value, n.Value.ToLowerInvariant())).ToList());
    }

    private static CountryMapDatum Country(MapDataDto map, string iso) => map.Countries.Single(c => c.Iso == iso);

    [Fact]
    public void Book_with_two_authors_from_the_same_country_counts_once()
    {
        var input = new InputBuilder();
        var a1 = input.Author(Brazil);
        var a2 = input.Author(Brazil);
        input.Book(ReadingStatus.Read, [a1, a2], Brazil);

        var map = MapAggregator.BuildAuthorsMap(input.Build());

        var brazil = Country(map, "BRA");
        brazil.BookCount.ShouldBe(1);
        brazil.AuthorCount.ShouldBe(2);
        map.ExploredCountries.ShouldBe(1);
        map.TotalReadBooks.ShouldBe(1);
    }

    [Fact]
    public void Author_with_two_citizenships_counts_in_both_countries()
    {
        var input = new InputBuilder();
        var author = input.Author(Japan, UnitedKingdom);
        input.Book(ReadingStatus.Read, [author]);

        var map = MapAggregator.BuildAuthorsMap(input.Build());

        Country(map, "JPN").BookCount.ShouldBe(1);
        Country(map, "GBR").BookCount.ShouldBe(1);
        map.ExploredCountries.ShouldBe(2);
        map.TotalReadBooks.ShouldBe(1); // never the sum of bookCount
        map.UnknownBookCount.ShouldBe(0);
    }

    [Fact]
    public void Book_without_known_country_goes_to_unknown_in_each_map()
    {
        var input = new InputBuilder();
        var unknownAuthor = input.Author();
        input.Book(ReadingStatus.Read, [unknownAuthor]);
        input.Book(ReadingStatus.Read, []);

        var data = input.Build();
        var authors = MapAggregator.BuildAuthorsMap(data);
        var origins = MapAggregator.BuildOriginsMap(data);

        authors.UnknownBookCount.ShouldBe(2);
        authors.Countries.ShouldBeEmpty();
        authors.ExploredCountries.ShouldBe(0);
        authors.MaxBookCount.ShouldBe(0);
        authors.TotalReadBooks.ShouldBe(2);
        origins.UnknownBookCount.ShouldBe(2);
    }

    [Fact]
    public void Book_is_unknown_only_when_no_author_has_a_known_citizenship()
    {
        var input = new InputBuilder();
        var known = input.Author(Colombia);
        var unknown = input.Author();
        input.Book(ReadingStatus.Read, [known, unknown]);

        var map = MapAggregator.BuildAuthorsMap(input.Build());

        map.UnknownBookCount.ShouldBe(0);
        Country(map, "COL").AuthorCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(ReadingStatus.WantToRead)]
    [InlineData(ReadingStatus.Reading)]
    [InlineData(ReadingStatus.Abandoned)]
    public void Only_read_books_are_counted(ReadingStatus status)
    {
        var input = new InputBuilder();
        var author = input.Author(Japan);
        input.Book(status, [author], Japan);

        var data = input.Build();

        foreach (var map in new[] { MapAggregator.BuildAuthorsMap(data), MapAggregator.BuildOriginsMap(data) })
        {
            map.TotalReadBooks.ShouldBe(0);
            map.Countries.ShouldBeEmpty();
            map.UnknownBookCount.ShouldBe(0);
        }
    }

    [Fact]
    public void Explored_countries_counts_each_country_once_and_max_uses_highest_book_count()
    {
        var input = new InputBuilder();
        var author = input.Author(Japan);
        input.Book(ReadingStatus.Read, [author], Japan);
        input.Book(ReadingStatus.Read, [author], Japan);
        input.Book(ReadingStatus.Read, [author], Japan);

        var map = MapAggregator.BuildAuthorsMap(input.Build());

        map.ExploredCountries.ShouldBe(1);
        Country(map, "JPN").BookCount.ShouldBe(3);
        Country(map, "JPN").AuthorCount.ShouldBe(1);
        map.MaxBookCount.ShouldBe(3);
    }

    [Fact]
    public void Origins_map_counts_book_once_per_origin_and_authors_of_those_books()
    {
        var input = new InputBuilder();
        var a1 = input.Author(Brazil);
        var a2 = input.Author(Japan);
        input.Book(ReadingStatus.Read, [a1, a2], Brazil, Japan);

        var map = MapAggregator.BuildOriginsMap(input.Build());

        map.MapType.ShouldBe(MapType.Origins);
        Country(map, "BRA").BookCount.ShouldBe(1);
        Country(map, "BRA").AuthorCount.ShouldBe(2); // informative: distinct authors of the books of that country
        Country(map, "JPN").BookCount.ShouldBe(1);
        map.TotalReadBooks.ShouldBe(1);
    }

    [Fact]
    public void Duplicate_rows_do_not_inflate_counts()
    {
        var bookId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var input = new MapAggregationInput(
            [new LibraryBookRow(bookId, ReadingStatus.Read), new LibraryBookRow(bookId, ReadingStatus.Read)],
            [new BookAuthorRow(bookId, authorId), new BookAuthorRow(bookId, authorId)],
            [new AuthorCitizenshipRow(authorId, Japan), new AuthorCitizenshipRow(authorId, Japan)],
            [new BookOriginRow(bookId, Japan), new BookOriginRow(bookId, Japan)]);

        var authors = MapAggregator.BuildAuthorsMap(input);
        var origins = MapAggregator.BuildOriginsMap(input);

        authors.TotalReadBooks.ShouldBe(1);
        Country(authors, "JPN").BookCount.ShouldBe(1);
        Country(authors, "JPN").AuthorCount.ShouldBe(1);
        Country(origins, "JPN").BookCount.ShouldBe(1);
    }

    [Fact]
    public void Kafka_Garcia_Marquez_and_Murakami_example()
    {
        // Kafka: Wikidata P27 = Austria-Hungary + Czechoslovakia → mapped to AUT and CZE.
        // "A Metamorfose": origin unknown in this scenario (must not be guessed from the author).
        var input = new InputBuilder();
        var kafka = input.Author(Austria, Czechia);
        var garciaMarquez = input.Author(Colombia);
        var murakami = input.Author(Japan);
        input.Book(ReadingStatus.Read, [kafka]);
        input.Book(ReadingStatus.Read, [garciaMarquez], Colombia);
        input.Book(ReadingStatus.Read, [murakami], Japan);
        input.Book(ReadingStatus.Reading, [murakami], Japan); // in progress: ignored

        var data = input.Build();
        var authors = MapAggregator.BuildAuthorsMap(data);
        var origins = MapAggregator.BuildOriginsMap(data);

        authors.TotalReadBooks.ShouldBe(3);
        authors.ExploredCountries.ShouldBe(4);
        authors.Countries.Select(c => c.Iso).ShouldBe(["AUT", "COL", "JPN", "CZE"], ignoreOrder: true);
        authors.Countries.ShouldAllBe(c => c.BookCount == 1 && c.AuthorCount == 1);
        authors.UnknownBookCount.ShouldBe(0);
        authors.MaxBookCount.ShouldBe(1);

        origins.TotalReadBooks.ShouldBe(3);
        origins.ExploredCountries.ShouldBe(2);
        origins.UnknownBookCount.ShouldBe(1);
        Country(origins, "COL").Name.ShouldBe("Colômbia");
        Country(origins, "JPN").Continent.ShouldBe(Continent.Asia);

        MapAggregator.CountDistinctCountries(authors, origins).ShouldBe(4);
        MapAggregator.CountContinents(authors, origins).ShouldBe(3);
    }

    [Fact]
    public void Countries_are_ordered_by_book_count_descending()
    {
        var input = new InputBuilder();
        var br = input.Author(Brazil);
        var jp = input.Author(Japan);
        input.Book(ReadingStatus.Read, [jp]);
        input.Book(ReadingStatus.Read, [br]);
        input.Book(ReadingStatus.Read, [br]);

        var map = MapAggregator.Build(MapType.Authors, input.Build());

        map.Countries.Select(c => c.Iso).ShouldBe(["BRA", "JPN"]);
    }

    [Fact]
    public void Status_filter_selects_the_book_set_and_defaults_to_Read()
    {
        var input = new InputBuilder();
        var kafka = input.Author(Austria, Czechia);
        input.Book(ReadingStatus.Read, [kafka]);
        input.Book(ReadingStatus.Reading, [kafka]);
        input.Book(ReadingStatus.WantToRead, [kafka]);
        input.Book(ReadingStatus.Abandoned, [input.Author()]);
        var data = input.Build();

        MapAggregator.BuildAuthorsMap(data).Status.ShouldBe("Read");
        Country(MapAggregator.BuildAuthorsMap(data), "AUT").BookCount.ShouldBe(1);

        var all = MapAggregator.Build(MapType.Authors, data, MapStatusFilter.All);
        all.TotalReadBooks.ShouldBe(4);
        Country(all, "AUT").BookCount.ShouldBe(3);
        all.ExploredCountries.ShouldBe(2);
        all.UnknownBookCount.ShouldBe(1);

        var abandoned = MapAggregator.Build(MapType.Authors, data, MapStatusFilter.Abandoned);
        abandoned.Countries.ShouldBeEmpty();
        abandoned.UnknownBookCount.ShouldBe(1);
    }

    private static CountryBookDto PanelBook(Guid bookId, string title) =>
        new(Guid.NewGuid(), bookId, title, null, [], null, null);

    [Fact]
    public void Authors_of_a_country_are_unique_sorted_and_only_from_books_of_the_status_set()
    {
        var input = new InputBuilder();
        var zed = input.Author(Brazil);
        var abe = input.Author(Brazil, Japan);
        var unread = input.Author(Brazil);
        var b1 = input.Book(ReadingStatus.Read, [zed, abe]);
        var b2 = input.Book(ReadingStatus.Read, [abe]);
        var b3 = input.Book(ReadingStatus.WantToRead, [unread]);
        var built = input.BuildWithNames(new() { [zed] = "Zed", [abe] = "Abe", [unread] = "Ulisses" });
        var readPanel = new[] { PanelBook(b1, "B1"), PanelBook(b2, "B2") };
        var wantPanel = new[] { PanelBook(b3, "B3") };

        var authors = MapAggregator.GetAuthorsForCountry(MapType.Authors, built, Brazil, readPanel);

        authors.Select(a => a.Name).ShouldBe(["Abe", "Zed"]);
        authors.Select(a => a.AuthorId).Distinct().Count().ShouldBe(authors.Count);
        authors.Count.ShouldBe(Country(MapAggregator.BuildAuthorsMap(built), "BRA").AuthorCount);
        MapAggregator.GetAuthorsForCountry(MapType.Authors, built, Japan, readPanel).ShouldHaveSingleItem().Name.ShouldBe("Abe");
        MapAggregator.GetAuthorsForCountry(MapType.Authors, built, Brazil, wantPanel, MapStatusFilter.WantToRead)
            .ShouldHaveSingleItem().Name.ShouldBe("Ulisses");
        MapAggregator.GetAuthorsForCountry(MapType.Origins, built, Brazil, readPanel).ShouldBeEmpty();
    }

    [Fact]
    public void Author_cards_carry_only_their_own_books_in_panel_order_and_never_empty()
    {
        var input = new InputBuilder();
        var abe = input.Author(Brazil);
        var zed = input.Author(Brazil);
        var shared = input.Book(ReadingStatus.Read, [abe, zed]);
        var onlyAbe = input.Book(ReadingStatus.Read, [abe]);
        var onlyZed = input.Book(ReadingStatus.Read, [zed]);
        var built = input.BuildWithNames(new() { [abe] = "Abe", [zed] = "Zed" });
        var panel = new[] { PanelBook(onlyZed, "Z"), PanelBook(shared, "S"), PanelBook(onlyAbe, "A") };

        var authors = MapAggregator.GetAuthorsForCountry(MapType.Authors, built, Brazil, panel);

        authors.Single(a => a.Name == "Abe").Books.Select(b => b.Title).ShouldBe(["S", "A"]);
        authors.Single(a => a.Name == "Zed").Books.Select(b => b.Title).ShouldBe(["Z", "S"]);
        authors.ShouldAllBe(a => a.Books.Count > 0);
    }
}
