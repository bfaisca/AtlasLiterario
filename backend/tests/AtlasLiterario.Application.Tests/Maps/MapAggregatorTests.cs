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
}
