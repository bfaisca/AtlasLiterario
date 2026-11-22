using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Library;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtlasLiterario.Application.Tests.Library;

public sealed class LibraryGeoMissingTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2025, 3, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDb.Create();
    private readonly FakeCurrentUser _user = new() { UserId = Guid.NewGuid() };
    private Dictionary<string, short> _countryIds = [];

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();
        _countryIds = await _db.Countries.ToDictionaryAsync(c => c.IsoCode, c => c.Id);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private LibraryService Library() => new(_db, _user, null!, new FakeClock(), NullLogger<LibraryService>.Instance);

    private Author AddAuthor(string name, AuthorCountryRelationship? relation = null, string iso = "BRA")
    {
        var author = new Author(name, Now);
        _db.Authors.Add(author);
        if (relation is not null)
        {
            _db.AuthorCountries.Add(new AuthorCountry(author.Id, _countryIds[iso], relation.Value, GeoSource.Wikidata, 0.9m, false, null, Now));
        }

        return author;
    }

    private UserBook AddBook(Guid userId, string title, Author[] authors, string? origin, ReadingStatus status = ReadingStatus.Read)
    {
        var book = new Book(title, Now);
        _db.Books.Add(book);
        for (short i = 0; i < authors.Length; i++)
        {
            _db.BookAuthors.Add(new BookAuthor(book.Id, authors[i].Id, i));
        }

        if (origin is not null)
        {
            _db.BookOriginCountries.Add(new BookOriginCountry(book.Id, _countryIds[origin], GeoSource.Wikidata, 0.9m, false, null, Now));
        }

        var item = new UserBook(userId, book.Id, status, null, null, null, status == ReadingStatus.Read ? new DateOnly(2025, 1, 1) : null, Now);
        _db.UserBooks.Add(item);
        return item;
    }

    private async Task SeedAsync()
    {
        var me = _user.UserId!.Value;
        var withCountry = AddAuthor("Com país", AuthorCountryRelationship.Citizenship);
        var onlyBirth = AddAuthor("Só nascimento", AuthorCountryRelationship.BirthPlace, "ARG");
        var noCountry = AddAuthor("Sem país");
        var second = AddAuthor("Segundo autor", AuthorCountryRelationship.Citizenship, "PRT");

        AddBook(me, "Completo", [withCountry], "BRA");
        AddBook(me, "Sem origem", [withCountry], null);
        AddBook(me, "Sem autor-país", [noCountry], "BRA", ReadingStatus.Reading);
        AddBook(me, "Sem nada", [noCountry], null);
        AddBook(me, "Só nascimento", [onlyBirth], "BRA");
        AddBook(me, "Sem autores", [], "BRA");
        AddBook(me, "Coautoria", [noCountry, second], "BRA");

        // Another user's book must never leak in.
        AddBook(Guid.NewGuid(), "De outro usuário", [noCountry], null);
        await _db.SaveChangesAsync();
    }

    private async Task<string[]> TitlesAsync(string? geoMissing, IReadOnlyList<ReadingStatus>? status = null)
    {
        var page = await Library().ListAsync(
            new LibraryQuery(null, status, null, null, null, null, "title", "asc", 1, 100, geoMissing), default);
        return page.Items.Select(i => i.Book.Title).ToArray();
    }

    [Fact]
    public async Task Without_the_parameter_every_book_is_listed()
    {
        await SeedAsync();
        (await TitlesAsync(null)).Length.ShouldBe(7);
        (await TitlesAsync("  ")).Length.ShouldBe(7);
    }

    [Fact]
    public async Task Origin_lists_books_without_any_origin_country()
    {
        await SeedAsync();
        (await TitlesAsync("origin")).ShouldBe(["Sem nada", "Sem origem"]);
    }

    [Fact]
    public async Task Author_lists_books_where_no_author_has_citizenship_including_books_without_authors()
    {
        await SeedAsync();
        // Birth-only relations do not count as nationality; a co-authored book with one citizenship is not missing.
        (await TitlesAsync("author")).ShouldBe(["Sem autor-país", "Sem autores", "Sem nada", "Só nascimento"]);
    }

    [Fact]
    public async Task Both_is_the_intersection_and_Any_is_the_union()
    {
        await SeedAsync();
        (await TitlesAsync("both")).ShouldBe(["Sem nada"]);
        (await TitlesAsync("any")).ShouldBe(["Sem autor-país", "Sem autores", "Sem nada", "Sem origem", "Só nascimento"]);
    }

    [Fact]
    public async Task Value_is_case_insensitive_and_combines_with_other_filters()
    {
        await SeedAsync();
        (await TitlesAsync("ANY")).Length.ShouldBe(5);
        (await TitlesAsync(" Both ")).ShouldBe(["Sem nada"]);
        (await TitlesAsync("any", [ReadingStatus.Reading])).ShouldBe(["Sem autor-país"]);
        (await TitlesAsync("origin", [ReadingStatus.Reading])).ShouldBeEmpty();

        var page = await Library().ListAsync(
            new LibraryQuery("origem", null, null, null, null, null, "title", "asc", 1, 100, "origin"), default);
        page.Items.ShouldHaveSingleItem().Book.Title.ShouldBe("Sem origem");
        page.TotalCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("1")]
    [InlineData("origin,author")]
    public async Task Invalid_value_is_a_validation_error_on_geoMissing(string value)
    {
        var ex = await Should.ThrowAsync<ValidationException>(() => TitlesAsync(value));
        ex.Code.ShouldBe("validation.failed");
        ex.Errors.ShouldContainKey("geoMissing");
    }
}
