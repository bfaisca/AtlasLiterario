using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Tests.Books;

public sealed class BookAuthorIdsTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2025, 3, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDb.Create();
    private readonly FakeCurrentUser _user = new() { UserId = Guid.NewGuid() };

    public async Task InitializeAsync() => await _db.Database.EnsureCreatedAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private BookService Books() => new(_db, _user, null!, new FakeClock());

    private static CreateBookRequest Request(string[] names, Guid?[]? ids = null, string title = "Livro") =>
        new(title, null, null, null, null, null, null, null, null, null, names, null, ids);

    private async Task<Author> SeedAuthorAsync(string name)
    {
        var author = new Author(name, Now);
        _db.Authors.Add(author);
        await _db.SaveChangesAsync();
        return author;
    }

    [Fact]
    public async Task Existing_author_is_reused_by_id_even_when_the_typed_name_differs()
    {
        var garcia = await SeedAuthorAsync("Gabriel García Márquez");
        var homonym = await SeedAuthorAsync("Gabo");

        var book = await Books().CreateAsync(Request(["Gabo"], [garcia.Id]), default);

        book.Authors.ShouldHaveSingleItem().Id.ShouldBe(garcia.Id);
        (await _db.Authors.CountAsync()).ShouldBe(2);
        homonym.ShouldNotBeNull();
    }

    [Fact]
    public async Task Null_id_keeps_resolving_by_name_and_mixes_with_ids_preserving_order()
    {
        var picked = await SeedAuthorAsync("Autor Escolhido");
        var byName = await SeedAuthorAsync("Autor Por Nome");

        var book = await Books().CreateAsync(Request(["autor por nome", "x", "Novo Autor"], [null, picked.Id, null]), default);

        book.Authors.Select(a => a.Name).ShouldBe(["Autor Por Nome", "Autor Escolhido", "Novo Autor"]);
        book.Authors[0].Id.ShouldBe(byName.Id);
        (await _db.Authors.CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task Omitted_author_ids_keep_the_previous_behaviour()
    {
        await SeedAuthorAsync("Clarice Lispector");

        var book = await Books().CreateAsync(Request(["clarice lispector", " Clarice  Lispector", "Outra Pessoa", ""]), default);

        book.Authors.Select(a => a.Name).ShouldBe(["Clarice Lispector", "Outra Pessoa"]);
        (await _db.Authors.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Different_size_is_a_validation_error_on_author_ids()
    {
        var a = await SeedAuthorAsync("Autor A");

        var ex = await Should.ThrowAsync<ValidationException>(() => Books().CreateAsync(Request(["Autor A", "Autor B"], [a.Id]), default));

        ex.Code.ShouldBe("validation.failed");
        ex.Errors.ShouldContainKey("authorIds");
        (await _db.Books.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Unknown_id_is_a_validation_error_and_nothing_is_created()
    {
        var ex = await Should.ThrowAsync<ValidationException>(
            () => Books().CreateAsync(Request(["Fantasma", "Novo"], [Guid.NewGuid(), null]), default));

        ex.Code.ShouldBe("validation.failed");
        ex.Errors.ShouldContainKey("authorIds");
        (await _db.Authors.CountAsync()).ShouldBe(0);
        (await _db.Books.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Same_author_twice_is_a_validation_error()
    {
        var a = await SeedAuthorAsync("Autor A");

        var ex = await Should.ThrowAsync<ValidationException>(() => Books().CreateAsync(Request(["Autor A", "Autor A"], [a.Id, a.Id]), default));

        ex.Errors.ShouldContainKey("authorIds");
    }

    [Fact]
    public async Task Update_replaces_authors_by_id()
    {
        var first = await SeedAuthorAsync("Primeiro");
        var second = await SeedAuthorAsync("Segundo");
        var book = await Books().CreateAsync(Request(["Primeiro"], [first.Id]), default);
        _db.UserBooks.Add(new UserBook(_user.UserId!.Value, book.Id, ReadingStatus.Read, null, null, null, null, Now));
        await _db.SaveChangesAsync();

        var updated = await Books().UpdateAsync(book.Id, Request(["Segundo", "Primeiro"], [second.Id, first.Id]), default);

        updated.Authors.Select(a => a.Id).ShouldBe([second.Id, first.Id]);
        (await _db.Authors.CountAsync()).ShouldBe(2);

        var bad = await Should.ThrowAsync<ValidationException>(
            () => Books().UpdateAsync(book.Id, Request(["Segundo"], [Guid.NewGuid()]), default));
        bad.Errors.ShouldContainKey("authorIds");
    }
}
