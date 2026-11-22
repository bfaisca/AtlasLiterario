using AtlasLiterario.Application.Authors;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Tests.Maps;

public sealed class MapServiceTests : IAsyncLifetime
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

    /// <summary>Changing existing countries is a moderator action (role read from the database).</summary>
    private async Task AsModeratorAsync(Func<Task> action)
    {
        var reader = _user.UserId;
        var user = new User("Moderadora", $"mod{Guid.NewGuid():N}@example.com", "hash", Now);
        user.SetRole(UserRole.Moderator, Now);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _user.UserId = user.Id;
        try
        {
            await action();
        }
        finally
        {
            _user.UserId = reader;
        }
    }

    private MapService Maps() => new(_db, _user);

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

    private Book AddBook(string title, Author[] authors, string[]? origins = null)
    {
        var book = new Book(title, Now);
        _db.Books.Add(book);
        for (short i = 0; i < authors.Length; i++)
        {
            _db.BookAuthors.Add(new BookAuthor(book.Id, authors[i].Id, i));
        }

        foreach (var iso in origins ?? [])
        {
            _db.BookOriginCountries.Add(new BookOriginCountry(book.Id, _countryIds[iso], GeoSource.Wikidata, 0.9m, false, null, Now));
        }

        return book;
    }

    private UserBook Shelve(Guid userId, Book book, ReadingStatus status, DateOnly? finished = null)
    {
        var item = new UserBook(userId, book.Id, status, null, null, null, status == ReadingStatus.Read ? finished : null, Now);
        _db.UserBooks.Add(item);
        return item;
    }

    private (Book Cien, Book Processo, Book Metamorfose, Book Wood) SeedPromptExample()
    {
        var marquez = AddAuthor("Gabriel García Márquez", "COL");
        var kafka = AddAuthor("Franz Kafka", "AUT", "CZE");
        var murakami = AddAuthor("Haruki Murakami", "JPN");
        var cien = AddBook("Cien años de soledad", [marquez], ["ARG"]);
        var processo = AddBook("O Processo", [kafka], ["DEU"]);
        var meta = AddBook("A Metamorfose", [kafka], ["DEU"]);
        var wood = AddBook("Norwegian Wood", [murakami], ["JPN"]);
        var id = _user.UserId!.Value;
        Shelve(id, cien, ReadingStatus.Read, new DateOnly(2025, 1, 10));
        Shelve(id, processo, ReadingStatus.Read, new DateOnly(2025, 2, 1));
        Shelve(id, meta, ReadingStatus.Read, new DateOnly(2025, 3, 1));
        Shelve(id, wood, ReadingStatus.Read, new DateOnly(2025, 2, 20));
        return (cien, processo, meta, wood);
    }

    [Fact]
    public async Task Authors_map_for_the_prompt_example()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        var map = await Maps().GetMapAsync(MapType.Authors, null, default);

        map.MapType.ShouldBe(MapType.Authors);
        map.TotalReadBooks.ShouldBe(4);
        map.ExploredCountries.ShouldBe(4);
        map.MaxBookCount.ShouldBe(2);
        map.UnknownBookCount.ShouldBe(0);
        map.Countries.ToDictionary(c => c.Iso, c => c.BookCount).ShouldBe(
            new Dictionary<string, int> { ["COL"] = 1, ["AUT"] = 2, ["CZE"] = 2, ["JPN"] = 1 }, ignoreOrder: true);
        var aut = map.Countries.Single(c => c.Iso == "AUT");
        aut.AuthorCount.ShouldBe(1);
        aut.Name.ShouldBe("Áustria");
        aut.Continent.ShouldBe(Continent.Europe);
    }

    [Fact]
    public async Task Origins_map_counts_only_origin_relations()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        var map = await Maps().GetMapAsync(MapType.Origins, null, default);

        map.TotalReadBooks.ShouldBe(4);
        map.ExploredCountries.ShouldBe(3);
        map.Countries.ToDictionary(c => c.Iso, c => c.BookCount).ShouldBe(
            new Dictionary<string, int> { ["ARG"] = 1, ["DEU"] = 2, ["JPN"] = 1 }, ignoreOrder: true);
        map.MaxBookCount.ShouldBe(2);
    }

    [Fact]
    public async Task Missing_data_is_unknown_and_never_becomes_a_country()
    {
        var anonymous = AddAuthor("Autor sem país");
        var birthOnly = AddAuthor("Só nascimento");
        _db.AuthorCountries.Add(new AuthorCountry(
            birthOnly.Id, _countryIds["FRA"], AuthorCountryRelationship.BirthPlace, GeoSource.Wikidata, 0.9m, false, null, Now));
        var b1 = AddBook("Sem nada", [anonymous]);
        var b2 = AddBook("Só nascimento", [birthOnly]);
        Shelve(_user.UserId!.Value, b1, ReadingStatus.Read);
        Shelve(_user.UserId!.Value, b2, ReadingStatus.Read);
        await _db.SaveChangesAsync();

        var authors = await Maps().GetMapAsync(MapType.Authors, null, default);
        var origins = await Maps().GetMapAsync(MapType.Origins, null, default);

        authors.Countries.ShouldBeEmpty();
        authors.UnknownBookCount.ShouldBe(2);
        authors.ExploredCountries.ShouldBe(0);
        authors.MaxBookCount.ShouldBe(0);
        authors.TotalReadBooks.ShouldBe(2);
        origins.Countries.ShouldBeEmpty();
        origins.UnknownBookCount.ShouldBe(2);
    }

    [Fact]
    public async Task Book_counts_once_per_country_even_with_two_authors_of_the_same_country()
    {
        var a = AddAuthor("Autor A", "FRA");
        var b = AddAuthor("Autor B", "FRA", "BEL");
        var book = AddBook("Duplo", [a, b]);
        Shelve(_user.UserId!.Value, book, ReadingStatus.Read);
        await _db.SaveChangesAsync();

        var map = await Maps().GetMapAsync(MapType.Authors, null, default);

        map.TotalReadBooks.ShouldBe(1);
        var fra = map.Countries.Single(c => c.Iso == "FRA");
        fra.BookCount.ShouldBe(1);
        fra.AuthorCount.ShouldBe(2);
        map.Countries.Single(c => c.Iso == "BEL").AuthorCount.ShouldBe(1);
        map.ExploredCountries.ShouldBe(2);
    }

    [Fact]
    public async Task Only_read_books_count_and_leaving_read_removes_the_country()
    {
        var (_, processo, meta, _) = SeedPromptExample();
        Shelve(_user.UserId!.Value, AddBook("Quero ler", [AddAuthor("Futuro", "BRA")]), ReadingStatus.WantToRead);
        Shelve(_user.UserId!.Value, AddBook("Lendo", [AddAuthor("Agora", "ITA")]), ReadingStatus.Reading);
        Shelve(_user.UserId!.Value, AddBook("Abandonado", [AddAuthor("Largado", "PRT")]), ReadingStatus.Abandoned);
        await _db.SaveChangesAsync();

        var before = await Maps().GetMapAsync(MapType.Authors, null, default);
        before.TotalReadBooks.ShouldBe(4);
        before.Countries.Select(c => c.Iso).ShouldNotContain("BRA");
        before.Countries.Select(c => c.Iso).ShouldNotContain("ITA");
        before.Countries.Select(c => c.Iso).ShouldNotContain("PRT");

        // Read -> Reading: both Kafka books leave AUT/CZE
        foreach (var bookId in new[] { processo.Id, meta.Id })
        {
            var item = await _db.UserBooks.SingleAsync(ub => ub.BookId == bookId);
            item.ChangeStatus(ReadingStatus.Reading, Now);
        }

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var after = await Maps().GetMapAsync(MapType.Authors, null, default);
        after.TotalReadBooks.ShouldBe(2);
        after.Countries.Select(c => c.Iso).ShouldBe(["COL", "JPN"], ignoreOrder: true);
        after.ExploredCountries.ShouldBe(2);

        // Only one of the two Kafka books returns: AUT/CZE come back with count 1
        var back = await _db.UserBooks.SingleAsync(ub => ub.BookId == processo.Id);
        back.ChangeStatus(ReadingStatus.Read, Now);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var again = await Maps().GetMapAsync(MapType.Authors, null, default);
        again.Countries.Single(c => c.Iso == "AUT").BookCount.ShouldBe(1);
    }

    [Fact]
    public async Task Manual_origin_correction_changes_the_origins_map()
    {
        var (cien, _, _, _) = SeedPromptExample();
        await _db.SaveChangesAsync();
        (await Maps().GetMapAsync(MapType.Origins, null, default)).Countries.Select(c => c.Iso).ShouldContain("ARG");

        var books = new BookService(_db, _user, null!, _clock);
        await AsModeratorAsync(() => books.UpdateOriginCountriesAsync(cien.Id, new UpdateOriginCountriesRequest(["COL"]), default));
        _db.ChangeTracker.Clear();

        var map = await Maps().GetMapAsync(MapType.Origins, null, default);
        map.Countries.Select(c => c.Iso).ShouldNotContain("ARG");
        map.Countries.Single(c => c.Iso == "COL").BookCount.ShouldBe(1);
    }

    [Fact]
    public async Task Manual_citizenship_correction_changes_the_authors_map()
    {
        var (cien, _, _, _) = SeedPromptExample();
        await _db.SaveChangesAsync();
        var marquezId = (await _db.BookAuthors.SingleAsync(ba => ba.BookId == cien.Id)).AuthorId;

        var authors = new AuthorService(_db, _user, _clock);
        await AsModeratorAsync(() => authors.UpdateCountriesAsync(marquezId, new UpdateAuthorCountriesRequest(["COL", "MEX"]), default));
        _db.ChangeTracker.Clear();

        var map = await Maps().GetMapAsync(MapType.Authors, null, default);
        map.Countries.Single(c => c.Iso == "MEX").BookCount.ShouldBe(1);
        map.ExploredCountries.ShouldBe(5);
    }

    [Fact]
    public async Task Users_only_see_their_own_data()
    {
        SeedPromptExample();
        var other = Guid.NewGuid();
        var foreign = AddBook("Do outro", [AddAuthor("Estrangeiro", "NOR")], ["NOR"]);
        Shelve(other, foreign, ReadingStatus.Read);
        await _db.SaveChangesAsync();

        var mine = await Maps().GetMapAsync(MapType.Authors, null, default);
        mine.Countries.Select(c => c.Iso).ShouldNotContain("NOR");
        mine.TotalReadBooks.ShouldBe(4);

        _user.UserId = other;
        var theirs = await Maps().GetMapAsync(MapType.Authors, null, default);
        theirs.TotalReadBooks.ShouldBe(1);
        theirs.Countries.ShouldHaveSingleItem().Iso.ShouldBe("NOR");

        // the other user's detail of AUT is empty even though the catalog books are shared
        var detail = await Maps().GetCountryDetailAsync(MapType.Authors, "AUT", null, default);
        detail.BookCount.ShouldBe(0);
        detail.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task Country_detail_lists_read_books_by_finished_date_desc_with_authors()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        var detail = await Maps().GetCountryDetailAsync(MapType.Authors, "aut", null, default);

        detail.Iso.ShouldBe("AUT");
        detail.Name.ShouldBe("Áustria");
        detail.Continent.ShouldBe(Continent.Europe);
        detail.BookCount.ShouldBe(2);
        detail.AuthorCount.ShouldBe(1);
        detail.Books.Select(b => b.Title).ShouldBe(["A Metamorfose", "O Processo"]);
        detail.Books[0].Authors.ShouldBe(["Franz Kafka"]);
        detail.Books[0].FinishedDate.ShouldBe(new DateOnly(2025, 3, 1));
        detail.Books[0].LibraryItemId.ShouldNotBe(Guid.Empty);

        var origin = await Maps().GetCountryDetailAsync(MapType.Origins, "DEU", null, default);
        origin.BookCount.ShouldBe(2);
        origin.Books.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Country_detail_of_valid_country_without_books_is_empty_and_unknown_iso_is_a_validation_error()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        var empty = await Maps().GetCountryDetailAsync(MapType.Authors, "BRA", null, default);
        empty.BookCount.ShouldBe(0);
        empty.AuthorCount.ShouldBe(0);
        empty.Books.ShouldBeEmpty();
        empty.Name.ShouldBe("Brasil");

        var ex = await Should.ThrowAsync<ValidationException>(() => Maps().GetCountryDetailAsync(MapType.Authors, "ZZZ", null, default));
        ex.Code.ShouldBe("country.unknown_iso");
        await Should.ThrowAsync<ValidationException>(() => Maps().GetCountryDetailAsync(MapType.Authors, "BR", null, default));
    }

    [Fact]
    public async Task Detail_counts_match_the_map_counts_for_every_country()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        foreach (var type in new[] { MapType.Authors, MapType.Origins })
        {
            var map = await Maps().GetMapAsync(type, null, default);
            foreach (var country in map.Countries)
            {
                var detail = await Maps().GetCountryDetailAsync(type, country.Iso, null, default);
                detail.BookCount.ShouldBe(country.BookCount);
                detail.AuthorCount.ShouldBe(country.AuthorCount);
                detail.Books.Count.ShouldBe(country.BookCount);
            }
        }
    }

    /// <summary>Marquez(COL)/Kafka(AUT,CZE)/Murakami(JPN): Read, Reading, WantToRead, Abandoned + an extra Read by Kafka.</summary>
    private async Task SeedAllStatusesAsync()
    {
        var marquez = AddAuthor("Gabriel García Márquez", "COL");
        var kafka = AddAuthor("Franz Kafka", "AUT", "CZE");
        var murakami = AddAuthor("Haruki Murakami", "JPN");
        var id = _user.UserId!.Value;
        Shelve(id, AddBook("Cien años", [marquez], ["ARG"]), ReadingStatus.Read, new DateOnly(2025, 1, 10));
        Shelve(id, AddBook("O Processo", [kafka], ["DEU"]), ReadingStatus.Reading);
        Shelve(id, AddBook("A Metamorfose", [kafka], ["DEU"]), ReadingStatus.WantToRead);
        Shelve(id, AddBook("Norwegian Wood", [murakami]), ReadingStatus.Abandoned);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Map_status_defaults_to_Read_and_echoes_the_filter()
    {
        await SeedAllStatusesAsync();

        var omitted = await Maps().GetMapAsync(MapType.Authors, null, default);
        var explicitRead = await Maps().GetMapAsync(MapType.Authors, "read", default);

        omitted.Status.ShouldBe("Read");
        explicitRead.Status.ShouldBe("Read");
        omitted.TotalReadBooks.ShouldBe(1);
        omitted.Countries.Select(c => c.Iso).ShouldBe(["COL"]);
        explicitRead.ShouldBeEquivalentTo(omitted);
    }

    [Theory]
    [InlineData("Reading", 1, new[] { "AUT", "CZE" }, 0)]
    [InlineData("WantToRead", 1, new[] { "AUT", "CZE" }, 0)]
    [InlineData("Abandoned", 1, new[] { "JPN" }, 0)]
    public async Task Authors_map_by_a_single_status(string status, int total, string[] isos, int unknown)
    {
        await SeedAllStatusesAsync();

        var map = await Maps().GetMapAsync(MapType.Authors, status, default);

        map.Status.ShouldBe(status);
        map.TotalReadBooks.ShouldBe(total);
        map.Countries.Select(c => c.Iso).ShouldBe(isos, ignoreOrder: true);
        map.UnknownBookCount.ShouldBe(unknown);
    }

    [Fact]
    public async Task Authors_map_with_All_counts_each_book_once_per_country_and_each_country_once()
    {
        await SeedAllStatusesAsync();

        var map = await Maps().GetMapAsync(MapType.Authors, "ALL", default);

        map.Status.ShouldBe("All");
        map.TotalReadBooks.ShouldBe(4);
        map.ExploredCountries.ShouldBe(4);
        map.UnknownBookCount.ShouldBe(0);
        map.MaxBookCount.ShouldBe(2); // both Kafka books (Reading + WantToRead) count in AUT and in CZE
        map.Countries.ToDictionary(c => c.Iso, c => c.BookCount).ShouldBe(
            new Dictionary<string, int> { ["COL"] = 1, ["AUT"] = 2, ["CZE"] = 2, ["JPN"] = 1 }, ignoreOrder: true);
        map.Countries.Single(c => c.Iso == "AUT").AuthorCount.ShouldBe(1);
    }

    [Fact]
    public async Task Origins_map_by_status_keeps_unknown_book_count_within_the_filtered_set()
    {
        await SeedAllStatusesAsync();

        var abandoned = await Maps().GetMapAsync(MapType.Origins, "Abandoned", default);
        abandoned.TotalReadBooks.ShouldBe(1);
        abandoned.Countries.ShouldBeEmpty();
        abandoned.UnknownBookCount.ShouldBe(1);
        abandoned.ExploredCountries.ShouldBe(0);
        abandoned.MaxBookCount.ShouldBe(0);

        var all = await Maps().GetMapAsync(MapType.Origins, "All", default);
        all.TotalReadBooks.ShouldBe(4);
        all.UnknownBookCount.ShouldBe(1);
        all.Countries.ToDictionary(c => c.Iso, c => c.BookCount).ShouldBe(
            new Dictionary<string, int> { ["ARG"] = 1, ["DEU"] = 2 }, ignoreOrder: true);
        all.MaxBookCount.ShouldBe(2);
        all.ExploredCountries.ShouldBe(2);
    }

    [Fact]
    public async Task Country_detail_follows_the_status_filter_and_only_lists_the_users_books()
    {
        await SeedAllStatusesAsync();

        (await Maps().GetCountryDetailAsync(MapType.Origins, "DEU", null, default)).BookCount.ShouldBe(0);

        var all = await Maps().GetCountryDetailAsync(MapType.Origins, "DEU", "All", default);
        all.Status.ShouldBe("All");
        all.BookCount.ShouldBe(2);
        all.Books.Select(b => b.Title).ShouldBe(["A Metamorfose", "O Processo"], ignoreOrder: true);

        var reading = await Maps().GetCountryDetailAsync(MapType.Origins, "deu", "Reading", default);
        reading.Status.ShouldBe("Reading");
        reading.Books.ShouldHaveSingleItem().Title.ShouldBe("O Processo");

        var wantToRead = await Maps().GetCountryDetailAsync(MapType.Authors, "CZE", "WantToRead", default);
        wantToRead.BookCount.ShouldBe(1);
        wantToRead.AuthorCount.ShouldBe(1);
        wantToRead.Books.ShouldHaveSingleItem().Title.ShouldBe("A Metamorfose");

        (await Maps().GetCountryDetailAsync(MapType.Authors, "CZE", null, default)).Status.ShouldBe("Read");
    }

    [Theory]
    [InlineData("Finished")]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("Read,Reading")]
    public async Task Invalid_status_is_a_validation_error_on_the_status_field(string status)
    {
        var map = await Should.ThrowAsync<ValidationException>(() => Maps().GetMapAsync(MapType.Authors, status, default));
        map.Code.ShouldBe("validation.failed");
        map.Errors.ShouldContainKey("status");

        var detail = await Should.ThrowAsync<ValidationException>(() => Maps().GetCountryDetailAsync(MapType.Origins, "BRA", status, default));
        detail.Errors.ShouldContainKey("status");
    }

    [Fact]
    public async Task Country_detail_authors_lists_citizens_of_the_panel_country_alphabetically_and_matches_author_count()
    {
        var kafka = AddAuthor("Franz Kafka", "AUT", "CZE");
        var zweig = AddAuthor("Stefan Zweig", "AUT");
        var angel = AddAuthor("Ágnes Ángel", "AUT");
        var unread = AddAuthor("Autor Desejado", "AUT");
        var other = AddAuthor("Haruki Murakami", "JPN");
        var id = _user.UserId!.Value;
        Shelve(id, AddBook("O Processo", [kafka]), ReadingStatus.Read);
        Shelve(id, AddBook("Xadrez", [zweig, other]), ReadingStatus.Read);
        Shelve(id, AddBook("Livro do Ágnes", [angel]), ReadingStatus.Read);
        Shelve(id, AddBook("Quero ler", [unread]), ReadingStatus.WantToRead);
        await _db.SaveChangesAsync();

        var aut = await Maps().GetCountryDetailAsync(MapType.Authors, "AUT", null, default);
        aut.Authors.Select(a => a.Name).ShouldBe(["Ágnes Ángel", "Franz Kafka", "Stefan Zweig"]);
        aut.Authors.ShouldAllBe(a => a.CountryIso == "AUT" && a.CountryName == "Áustria");
        aut.Authors.Count.ShouldBe(aut.AuthorCount);
        aut.Authors.Select(a => a.AuthorId).ShouldContain(kafka.Id);
        aut.Authors.Select(a => a.AuthorId).ShouldNotContain(unread.Id);

        // an author with two citizenships shows up in both panels
        var cze = await Maps().GetCountryDetailAsync(MapType.Authors, "CZE", null, default);
        cze.Authors.ShouldHaveSingleItem().AuthorId.ShouldBe(kafka.Id);
        cze.Authors.Count.ShouldBe(cze.AuthorCount);

        // status filter: same set as books/authorCount
        var wanted = await Maps().GetCountryDetailAsync(MapType.Authors, "AUT", "WantToRead", default);
        wanted.Authors.ShouldHaveSingleItem().AuthorId.ShouldBe(unread.Id);
        wanted.Authors.Count.ShouldBe(wanted.AuthorCount);

        var empty = await Maps().GetCountryDetailAsync(MapType.Authors, "BRA", null, default);
        empty.Authors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Country_detail_authors_is_empty_on_the_origins_map()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        var origin = await Maps().GetCountryDetailAsync(MapType.Origins, "DEU", null, default);

        origin.BookCount.ShouldBe(2);
        origin.Authors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Author_card_books_follow_the_status_filter_order_and_belong_to_the_author()
    {
        var kafka = AddAuthor("Franz Kafka", "AUT");
        var brod = AddAuthor("Max Brod", "AUT");
        var id = _user.UserId!.Value;
        var processo = AddBook("O Processo", [kafka]);
        var meta = AddBook("A Metamorfose", [kafka, brod]);
        var castelo = AddBook("O Castelo", [kafka]);
        var wantToRead = AddBook("Carta ao Pai", [kafka]);
        var brodOnly = AddBook("Tycho Brahe", [brod]);
        Shelve(id, processo, ReadingStatus.Read, new DateOnly(2025, 1, 10));
        Shelve(id, meta, ReadingStatus.Read, new DateOnly(2025, 3, 1));
        Shelve(id, castelo, ReadingStatus.Read, new DateOnly(2024, 12, 1));
        Shelve(id, wantToRead, ReadingStatus.WantToRead);
        Shelve(id, brodOnly, ReadingStatus.Read, new DateOnly(2025, 2, 1));
        // somebody else's reading of a Kafka book must never leak
        Shelve(Guid.NewGuid(), AddBook("Do outro", [kafka]), ReadingStatus.Read, new DateOnly(2025, 4, 1));
        await _db.SaveChangesAsync();

        var read = await Maps().GetCountryDetailAsync(MapType.Authors, "AUT", null, default);

        read.Authors.Select(a => a.Name).ShouldBe(["Franz Kafka", "Max Brod"]);
        read.Authors.ShouldAllBe(a => a.Books.Count > 0);
        var kafkaCard = read.Authors[0];
        kafkaCard.Books.Select(b => b.Title).ShouldBe(["A Metamorfose", "O Processo", "O Castelo"]);
        read.Authors[1].Books.Select(b => b.Title).ShouldBe(["A Metamorfose", "Tycho Brahe"]);
        read.Authors.Select(a => a.AuthorId).Distinct().Count().ShouldBe(read.Authors.Count);
        // same DTOs / same order as the panel's own list
        var panel = read.Books.ToDictionary(b => b.LibraryItemId);
        foreach (var book in kafkaCard.Books)
        {
            book.ShouldBe(panel[book.LibraryItemId]);
        }

        kafkaCard.Books.Select(b => b.LibraryItemId)
            .ShouldBe(read.Books.Where(b => kafkaCard.Books.Any(k => k.LibraryItemId == b.LibraryItemId)).Select(b => b.LibraryItemId));

        var want = await Maps().GetCountryDetailAsync(MapType.Authors, "AUT", "WantToRead", default);
        want.Authors.ShouldHaveSingleItem().Books.ShouldHaveSingleItem().Title.ShouldBe("Carta ao Pai");

        var all = await Maps().GetCountryDetailAsync(MapType.Authors, "AUT", "All", default);
        all.Authors[0].Books.Select(b => b.Title).ShouldBe(
            ["A Metamorfose", "O Processo", "O Castelo", "Carta ao Pai"]);

        _user.UserId = Guid.NewGuid();
        (await Maps().GetCountryDetailAsync(MapType.Authors, "AUT", null, default)).Authors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Origins_panel_has_no_author_cards()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        (await Maps().GetCountryDetailAsync(MapType.Origins, "DEU", null, default)).Authors.ShouldBeEmpty();
    }
}
