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

        var map = await Maps().GetMapAsync(MapType.Authors, default);

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

        var map = await Maps().GetMapAsync(MapType.Origins, default);

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

        var authors = await Maps().GetMapAsync(MapType.Authors, default);
        var origins = await Maps().GetMapAsync(MapType.Origins, default);

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

        var map = await Maps().GetMapAsync(MapType.Authors, default);

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

        var before = await Maps().GetMapAsync(MapType.Authors, default);
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

        var after = await Maps().GetMapAsync(MapType.Authors, default);
        after.TotalReadBooks.ShouldBe(2);
        after.Countries.Select(c => c.Iso).ShouldBe(["COL", "JPN"], ignoreOrder: true);
        after.ExploredCountries.ShouldBe(2);

        // Only one of the two Kafka books returns: AUT/CZE come back with count 1
        var back = await _db.UserBooks.SingleAsync(ub => ub.BookId == processo.Id);
        back.ChangeStatus(ReadingStatus.Read, Now);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var again = await Maps().GetMapAsync(MapType.Authors, default);
        again.Countries.Single(c => c.Iso == "AUT").BookCount.ShouldBe(1);
    }

    [Fact]
    public async Task Manual_origin_correction_changes_the_origins_map()
    {
        var (cien, _, _, _) = SeedPromptExample();
        await _db.SaveChangesAsync();
        (await Maps().GetMapAsync(MapType.Origins, default)).Countries.Select(c => c.Iso).ShouldContain("ARG");

        var books = new BookService(_db, _user, null!, _clock);
        await AsModeratorAsync(() => books.UpdateOriginCountriesAsync(cien.Id, new UpdateOriginCountriesRequest(["COL"]), default));
        _db.ChangeTracker.Clear();

        var map = await Maps().GetMapAsync(MapType.Origins, default);
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

        var map = await Maps().GetMapAsync(MapType.Authors, default);
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

        var mine = await Maps().GetMapAsync(MapType.Authors, default);
        mine.Countries.Select(c => c.Iso).ShouldNotContain("NOR");
        mine.TotalReadBooks.ShouldBe(4);

        _user.UserId = other;
        var theirs = await Maps().GetMapAsync(MapType.Authors, default);
        theirs.TotalReadBooks.ShouldBe(1);
        theirs.Countries.ShouldHaveSingleItem().Iso.ShouldBe("NOR");

        // the other user's detail of AUT is empty even though the catalog books are shared
        var detail = await Maps().GetCountryDetailAsync(MapType.Authors, "AUT", default);
        detail.BookCount.ShouldBe(0);
        detail.Books.ShouldBeEmpty();
    }

    [Fact]
    public async Task Country_detail_lists_read_books_by_finished_date_desc_with_authors()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        var detail = await Maps().GetCountryDetailAsync(MapType.Authors, "aut", default);

        detail.Iso.ShouldBe("AUT");
        detail.Name.ShouldBe("Áustria");
        detail.Continent.ShouldBe(Continent.Europe);
        detail.BookCount.ShouldBe(2);
        detail.AuthorCount.ShouldBe(1);
        detail.Books.Select(b => b.Title).ShouldBe(["A Metamorfose", "O Processo"]);
        detail.Books[0].Authors.ShouldBe(["Franz Kafka"]);
        detail.Books[0].FinishedDate.ShouldBe(new DateOnly(2025, 3, 1));
        detail.Books[0].LibraryItemId.ShouldNotBe(Guid.Empty);

        var origin = await Maps().GetCountryDetailAsync(MapType.Origins, "DEU", default);
        origin.BookCount.ShouldBe(2);
        origin.Books.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Country_detail_of_valid_country_without_books_is_empty_and_unknown_iso_is_a_validation_error()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        var empty = await Maps().GetCountryDetailAsync(MapType.Authors, "BRA", default);
        empty.BookCount.ShouldBe(0);
        empty.AuthorCount.ShouldBe(0);
        empty.Books.ShouldBeEmpty();
        empty.Name.ShouldBe("Brasil");

        var ex = await Should.ThrowAsync<ValidationException>(() => Maps().GetCountryDetailAsync(MapType.Authors, "ZZZ", default));
        ex.Code.ShouldBe("country.unknown_iso");
        await Should.ThrowAsync<ValidationException>(() => Maps().GetCountryDetailAsync(MapType.Authors, "BR", default));
    }

    [Fact]
    public async Task Detail_counts_match_the_map_counts_for_every_country()
    {
        SeedPromptExample();
        await _db.SaveChangesAsync();

        foreach (var type in new[] { MapType.Authors, MapType.Origins })
        {
            var map = await Maps().GetMapAsync(type, default);
            foreach (var country in map.Countries)
            {
                var detail = await Maps().GetCountryDetailAsync(type, country.Iso, default);
                detail.BookCount.ShouldBe(country.BookCount);
                detail.AuthorCount.ShouldBe(country.AuthorCount);
                detail.Books.Count.ShouldBe(country.BookCount);
            }
        }
    }
}
