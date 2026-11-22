using AtlasLiterario.Application.Authors;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Application.Tests.Fakes;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AtlasLiterario.Application.Tests.Authors;

/// <remarks>
/// Runs on the EF InMemory provider, which ignores transactions. Atomicity is guaranteed by design: the whole merge is
/// staged in the change tracker and persisted by ONE SaveChangesAsync (a single database transaction on PostgreSQL);
/// the tests verify the final state and that validation/404 failures change nothing.
/// </remarks>
public sealed class AuthorDeduplicationTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2025, 3, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = TestDb.Create();
    private readonly FakeCurrentUser _user = new();
    private readonly FakeClock _clock = new();
    private Dictionary<string, short> _countryIds = [];
    private Guid _readerId;

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();
        _countryIds = await _db.Countries.ToDictionaryAsync(c => c.IsoCode, c => c.Id);
        _readerId = await AddUserAsync(UserRole.User);
        _user.UserId = await AddUserAsync(UserRole.Moderator);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private AuthorDeduplicationService Service() => new(_db, _user, _clock);

    private async Task<Guid> AddUserAsync(UserRole role)
    {
        var user = new User("Pessoa", $"u{Guid.NewGuid():N}@example.com", "hash", Now);
        user.SetRole(role, Now);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private Author AddAuthor(string name, DateTimeOffset? createdAt = null)
    {
        var author = new Author(name, createdAt ?? Now);
        _db.Authors.Add(author);
        return author;
    }

    private void Cit(Author a, string iso, GeoSource source = GeoSource.Wikidata, decimal confidence = 0.9m, bool verified = false,
        AuthorCountryRelationship relation = AuthorCountryRelationship.Citizenship) =>
        _db.AuthorCountries.Add(new AuthorCountry(a.Id, _countryIds[iso], relation, source, confidence, verified, null, Now));

    private Book AddBook(string title, params (Author Author, short Position)[] authors)
    {
        var book = new Book(title, Now);
        _db.Books.Add(book);
        foreach (var (author, position) in authors)
        {
            _db.BookAuthors.Add(new BookAuthor(book.Id, author.Id, position));
        }

        return book;
    }

    private UserBook Shelve(Book book, ReadingStatus status = ReadingStatus.Read, DateOnly? finished = null)
    {
        var item = new UserBook(_readerId, book.Id, status, null, null, null, status == ReadingStatus.Read ? finished : null, Now);
        _db.UserBooks.Add(item);
        return item;
    }

    private async Task SaveAsync()
    {
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private async Task<T> AsAsync<T>(Guid userId, Func<Task<T>> action)
    {
        var previous = _user.UserId;
        _user.UserId = userId;
        try
        {
            return await action();
        }
        finally
        {
            _user.UserId = previous;
        }
    }

    // ------------------------------------------------------------------ merge

    [Fact]
    public async Task Merges_three_duplicates_with_distinct_books_without_losing_any_book()
    {
        var target = AddAuthor("Elena Ferrante");
        var d1 = AddAuthor("Elena Ferrante");
        var d2 = AddAuthor("Elena Ferrante");
        var b0 = AddBook("A amiga genial", (target, 0));
        var b1 = AddBook("Dias do abandono", (d1, 0));
        var b2 = AddBook("A filha perdida", (d2, 0));
        var items = new[] { Shelve(b0), Shelve(b1, ReadingStatus.Reading), Shelve(b2, ReadingStatus.WantToRead) };
        await SaveAsync();

        var result = await Service().MergeAsync(new MergeAuthorsRequest(target.Id, [d1.Id, d2.Id]), default);

        result.ShouldBe(new AuthorMergeResultDto(target.Id, 2, 2, 0, 0));
        (await _db.Authors.Select(a => a.Id).ToListAsync()).ShouldBe([target.Id]);
        (await _db.BookAuthors.Where(ba => ba.AuthorId == target.Id).Select(ba => ba.BookId).ToListAsync())
            .ShouldBe([b0.Id, b1.Id, b2.Id], ignoreOrder: true);
        (await _db.BookAuthors.CountAsync()).ShouldBe(3);
        // books and the readers' library are untouched
        (await _db.Books.CountAsync()).ShouldBe(3);
        var after = await _db.UserBooks.AsNoTracking().OrderBy(u => u.Id).ToListAsync();
        after.Count.ShouldBe(3);
        after.Select(u => (u.Id, u.BookId, u.Status)).ShouldBe(
            items.OrderBy(u => u.Id).Select(u => (u.Id, u.BookId, u.Status)).ToList());
    }

    [Fact]
    public async Task Book_with_both_authors_keeps_a_single_link_and_loses_nothing()
    {
        var target = AddAuthor("Elena Ferrante");
        var dup = AddAuthor("Elena Ferrante");
        var shared = AddBook("Compartilhado", (target, 0), (dup, 1));
        var onlyDup = AddBook("Só do duplicado", (dup, 0));
        await SaveAsync();

        var result = await Service().MergeAsync(new MergeAuthorsRequest(target.Id, [dup.Id]), default);

        result.BooksRelinked.ShouldBe(1);
        result.BooksAlreadyLinked.ShouldBe(1);
        var links = await _db.BookAuthors.AsNoTracking().ToListAsync();
        links.Count.ShouldBe(2);
        links.ShouldAllBe(l => l.AuthorId == target.Id);
        links.Select(l => l.BookId).ShouldBe([shared.Id, onlyDup.Id], ignoreOrder: true);
        links.Single(l => l.BookId == shared.Id).Position.ShouldBe((short)0); // the target's own link is kept
    }

    [Fact]
    public async Task Two_sources_on_the_same_book_collapse_into_one_link()
    {
        var target = AddAuthor("Elena Ferrante");
        var d1 = AddAuthor("Elena Ferrante");
        var d2 = AddAuthor("Elena Ferrante");
        var book = AddBook("Antologia", (d1, 0), (d2, 1));
        await SaveAsync();

        var result = await Service().MergeAsync(new MergeAuthorsRequest(target.Id, [d1.Id, d2.Id]), default);

        result.BooksRelinked.ShouldBe(1);
        result.BooksAlreadyLinked.ShouldBe(1);
        (await _db.BookAuthors.AsNoTracking().SingleAsync()).BookId.ShouldBe(book.Id);
    }

    [Fact]
    public async Task Moved_link_keeps_its_position_among_the_other_authors()
    {
        var first = AddAuthor("Primeiro Autor");
        var third = AddAuthor("Terceiro Autor");
        var target = AddAuthor("Elena Ferrante");
        var dup = AddAuthor("Elena Ferrante");
        var book = AddBook("Coletânea", (first, 0), (dup, 1), (third, 2));
        await SaveAsync();

        await Service().MergeAsync(new MergeAuthorsRequest(target.Id, [dup.Id]), default);

        var ordered = await _db.BookAuthors.AsNoTracking().Where(ba => ba.BookId == book.Id).OrderBy(ba => ba.Position).ToListAsync();
        ordered.Select(ba => (ba.AuthorId, ba.Position)).ShouldBe([(first.Id, (short)0), (target.Id, (short)1), (third.Id, (short)2)]);
    }

    [Fact]
    public async Task Countries_are_united_and_conflicts_prefer_verified_then_higher_confidence()
    {
        var target = AddAuthor("Elena Ferrante");
        var dup = AddAuthor("Elena Ferrante");
        Cit(target, "ITA", GeoSource.Wikidata, 0.9m);               // conflict: source is verified -> wins
        Cit(dup, "ITA", GeoSource.Manual, 1m, verified: true);
        Cit(target, "FRA", GeoSource.Wikidata, 0.6m);               // conflict: both unverified, source more confident -> wins
        Cit(dup, "FRA", GeoSource.OpenLibrary, 0.8m);
        Cit(target, "ESP", GeoSource.Manual, 1m, verified: true);   // conflict: target verified, source better confidence only -> target stays
        Cit(dup, "ESP", GeoSource.Wikidata, 1m);
        Cit(dup, "PRT");                                            // only in the source -> added
        Cit(dup, "BRA", relation: AuthorCountryRelationship.BirthPlace); // same country other relation -> added
        Cit(target, "BRA");                                         // different relation -> not a conflict
        await SaveAsync();

        var result = await Service().MergeAsync(new MergeAuthorsRequest(target.Id, [dup.Id]), default);

        result.CountriesAdded.ShouldBe(2);
        var rows = await _db.AuthorCountries.AsNoTracking().Where(ac => ac.AuthorId == target.Id)
            .Select(ac => new { Iso = ac.Country!.IsoCode, ac.RelationshipType, ac.Source, ac.Confidence, ac.IsVerified })
            .ToListAsync();
        rows.Count.ShouldBe(6);
        var ita = rows.Single(r => r.Iso == "ITA");
        (ita.Source, ita.IsVerified, ita.Confidence).ShouldBe((GeoSource.Manual, true, 1m));
        var fra = rows.Single(r => r.Iso == "FRA");
        (fra.Source, fra.Confidence).ShouldBe((GeoSource.OpenLibrary, 0.8m));
        var esp = rows.Single(r => r.Iso == "ESP");
        (esp.Source, esp.IsVerified).ShouldBe((GeoSource.Manual, true));
        rows.ShouldContain(r => r.Iso == "PRT" && r.RelationshipType == AuthorCountryRelationship.Citizenship);
        rows.ShouldContain(r => r.Iso == "BRA" && r.RelationshipType == AuthorCountryRelationship.BirthPlace);
        rows.ShouldContain(r => r.Iso == "BRA" && r.RelationshipType == AuthorCountryRelationship.Citizenship);
        (await _db.AuthorCountries.CountAsync()).ShouldBe(6); // nothing left on the removed author
    }

    [Fact]
    public async Task Same_country_in_two_sources_is_added_once()
    {
        var target = AddAuthor("Elena Ferrante");
        var d1 = AddAuthor("Elena Ferrante");
        var d2 = AddAuthor("Elena Ferrante");
        Cit(d1, "ITA", GeoSource.Wikidata, 0.7m);
        Cit(d2, "ITA", GeoSource.Manual, 1m, verified: true);
        await SaveAsync();

        var result = await Service().MergeAsync(new MergeAuthorsRequest(target.Id, [d1.Id, d2.Id]), default);

        result.CountriesAdded.ShouldBe(1);
        var row = await _db.AuthorCountries.AsNoTracking().SingleAsync();
        (row.AuthorId, row.IsVerified, row.Source).ShouldBe((target.Id, true, GeoSource.Manual));
    }

    [Fact]
    public async Task External_ids_move_when_the_target_lacks_the_provider_and_are_dropped_otherwise()
    {
        var target = AddAuthor("Elena Ferrante");
        var dup = AddAuthor("Elena Ferrante");
        _db.ExternalAuthorIdentifiers.Add(new ExternalAuthorIdentifier(target.Id, AuthorIdentifierProvider.Wikidata, "Q-TARGET"));
        _db.ExternalAuthorIdentifiers.Add(new ExternalAuthorIdentifier(dup.Id, AuthorIdentifierProvider.Wikidata, "Q-DUP"));     // conflict -> dropped
        _db.ExternalAuthorIdentifiers.Add(new ExternalAuthorIdentifier(dup.Id, AuthorIdentifierProvider.OpenLibrary, "OL1A"));   // moved
        await SaveAsync();

        await Service().MergeAsync(new MergeAuthorsRequest(target.Id, [dup.Id]), default);

        var ids = await _db.ExternalAuthorIdentifiers.AsNoTracking().ToListAsync();
        ids.ShouldAllBe(i => i.AuthorId == target.Id);
        ids.Select(i => (i.Provider, i.ExternalId)).ShouldBe(
            [(AuthorIdentifierProvider.Wikidata, "Q-TARGET"), (AuthorIdentifierProvider.OpenLibrary, "OL1A")], ignoreOrder: true);
    }

    [Fact]
    public async Task Birth_date_only_fills_a_gap_and_the_target_name_is_kept()
    {
        var withDate = AddAuthor("Elena Ferrante");
        withDate.BirthDate = new DateOnly(1943, 1, 1);
        var noDate = AddAuthor("Elena  Ferrante ");
        var dupDate = AddAuthor("elena ferrante");
        dupDate.BirthDate = new DateOnly(1950, 5, 5);
        await SaveAsync();

        await Service().MergeAsync(new MergeAuthorsRequest(withDate.Id, [dupDate.Id]), default);
        (await _db.Authors.AsNoTracking().SingleAsync(a => a.Id == withDate.Id)).BirthDate.ShouldBe(new DateOnly(1943, 1, 1));

        await Service().MergeAsync(new MergeAuthorsRequest(noDate.Id, [withDate.Id]), default);
        var kept = await _db.Authors.AsNoTracking().SingleAsync();
        kept.Id.ShouldBe(noDate.Id);
        kept.BirthDate.ShouldBe(new DateOnly(1943, 1, 1));
        kept.Name.ShouldBe("Elena  Ferrante");
    }

    [Fact]
    public async Task Authors_map_shows_one_card_with_all_the_books_after_the_merge()
    {
        var target = AddAuthor("Elena Ferrante");
        var d1 = AddAuthor("Elena Ferrante");
        var d2 = AddAuthor("Elena Ferrante");
        Cit(target, "ITA");
        Cit(d1, "ITA");
        Cit(d2, "ITA");
        var books = new[]
        {
            AddBook("L1", (target, 0)), AddBook("L2", (d1, 0)), AddBook("L3", (d2, 0)), AddBook("L4", (d1, 0), (d2, 1)),
        };
        for (var i = 0; i < books.Length; i++)
        {
            Shelve(books[i], ReadingStatus.Read, new DateOnly(2025, 1, 1 + i));
        }

        await SaveAsync();
        var map = new MapService(_db, new FakeCurrentUser { UserId = _readerId });
        var before = await map.GetCountryDetailAsync(MapType.Authors, "ITA", null, default);
        before.Authors.Count.ShouldBe(3);

        await Service().MergeAsync(new MergeAuthorsRequest(target.Id, [d1.Id, d2.Id]), default);
        _db.ChangeTracker.Clear();

        var after = await map.GetCountryDetailAsync(MapType.Authors, "ITA", null, default);
        after.AuthorCount.ShouldBe(1);
        var card = after.Authors.ShouldHaveSingleItem();
        card.AuthorId.ShouldBe(target.Id);
        card.Books.Select(b => b.Title).ShouldBe(["L4", "L3", "L2", "L1"]);
        after.BookCount.ShouldBe(4);
    }

    [Fact]
    public async Task Merging_again_with_removed_sources_is_not_found()
    {
        var target = AddAuthor("Elena Ferrante");
        var dup = AddAuthor("Elena Ferrante");
        await SaveAsync();
        var request = new MergeAuthorsRequest(target.Id, [dup.Id]);
        await Service().MergeAsync(request, default);

        var ex = await Should.ThrowAsync<NotFoundException>(() => Service().MergeAsync(request, default));
        ex.Code.ShouldBe("author.not_found");
    }

    [Fact]
    public async Task Missing_target_or_source_is_not_found_and_changes_nothing()
    {
        var target = AddAuthor("Elena Ferrante");
        var dup = AddAuthor("Elena Ferrante");
        AddBook("Livro", (dup, 0));
        await SaveAsync();

        (await Should.ThrowAsync<NotFoundException>(() =>
            Service().MergeAsync(new MergeAuthorsRequest(Guid.NewGuid(), [dup.Id]), default))).Code.ShouldBe("author.not_found");
        (await Should.ThrowAsync<NotFoundException>(() =>
            Service().MergeAsync(new MergeAuthorsRequest(target.Id, [dup.Id, Guid.NewGuid()]), default))).Code.ShouldBe("author.not_found");

        (await _db.Authors.CountAsync()).ShouldBe(2);
        (await _db.BookAuthors.AsNoTracking().SingleAsync()).AuthorId.ShouldBe(dup.Id);
    }

    [Fact]
    public async Task Invalid_requests_are_400_on_source_ids()
    {
        var target = AddAuthor("Elena Ferrante");
        var dup = AddAuthor("Elena Ferrante");
        await SaveAsync();

        var cases = new Dictionary<string, MergeAuthorsRequest>
        {
            ["empty"] = new(target.Id, []),
            ["repeated"] = new(target.Id, [dup.Id, dup.Id]),
            ["contains target"] = new(target.Id, [dup.Id, target.Id]),
            ["too many"] = new(target.Id, Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList()),
        };
        foreach (var (name, request) in cases)
        {
            var ex = await Should.ThrowAsync<ValidationException>(() => Service().MergeAsync(request, default), name);
            ex.Code.ShouldBe("validation.failed", name);
            ex.Errors.ShouldContainKey("sourceIds", name);
        }

        (await _db.Authors.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Fifty_sources_are_accepted()
    {
        var target = AddAuthor("Elena Ferrante");
        var sources = Enumerable.Range(0, 50).Select(_ => AddAuthor("Elena Ferrante")).ToList();
        await SaveAsync();

        var result = await Service().MergeAsync(new MergeAuthorsRequest(target.Id, sources.Select(s => s.Id).ToList()), default);

        result.MergedAuthors.ShouldBe(50);
        (await _db.Authors.CountAsync()).ShouldBe(1);
    }

    // ------------------------------------------------------------------ permissions

    [Fact]
    public async Task Common_user_gets_403_on_both_operations_and_nothing_changes()
    {
        var target = AddAuthor("Elena Ferrante");
        var dup = AddAuthor("Elena Ferrante");
        await SaveAsync();
        _user.UserId = _readerId;

        var merge = await Should.ThrowAsync<ForbiddenException>(() =>
            Service().MergeAsync(new MergeAuthorsRequest(target.Id, [dup.Id]), default));
        merge.Code.ShouldBe("author.moderator_required");
        var list = await Should.ThrowAsync<ForbiddenException>(() => Service().GetDuplicatesAsync(default));
        list.Code.ShouldBe("author.moderator_required");

        (await _db.Authors.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Role_is_read_from_the_database_so_a_demotion_is_immediate()
    {
        var target = AddAuthor("Elena Ferrante");
        var dup = AddAuthor("Elena Ferrante");
        await SaveAsync();
        var moderator = await _db.Users.SingleAsync(u => u.Id == _user.UserId);
        moderator.SetRole(UserRole.User, Now);
        await SaveAsync();

        await Should.ThrowAsync<ForbiddenException>(() =>
            Service().MergeAsync(new MergeAuthorsRequest(target.Id, [dup.Id]), default));
    }

    // ------------------------------------------------------------------ duplicates

    [Fact]
    public async Task Duplicates_lists_groups_by_normalized_name_ordered_with_book_count_and_citizenships_only()
    {
        var old = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var zolaA = AddAuthor("Émile Zola", old.AddDays(5));
        var zolaB = AddAuthor("emile zola", old);
        var ferrante1 = AddAuthor("Elena Ferrante", old.AddDays(1));
        var ferrante2 = AddAuthor("Elena Ferrante", old.AddDays(2));
        var ferrante3 = AddAuthor("ELENA FERRANTE", old.AddDays(3));
        AddAuthor("Autor Único");
        Cit(ferrante1, "ITA");
        Cit(ferrante1, "FRA", relation: AuthorCountryRelationship.BirthPlace);
        Cit(ferrante1, "ESP");
        Cit(ferrante3, "ITA");
        AddBook("A", (ferrante2, 0));
        AddBook("B", (ferrante2, 0));
        AddBook("C", (ferrante3, 0), (zolaA, 1));
        await SaveAsync();

        var groups = await Service().GetDuplicatesAsync(default);

        groups.Select(g => g.NormalizedName).ShouldBe(["elena ferrante", "emile zola"]);
        var ferrante = groups[0].Authors;
        ferrante.Select(a => a.Id).ShouldBe([ferrante2.Id, ferrante3.Id, ferrante1.Id]); // 2 books, 1 book, 0 books
        ferrante.Select(a => a.BookCount).ShouldBe([2, 1, 0]);
        ferrante.Single(a => a.Id == ferrante1.Id).NationalityIsos.ShouldBe(["ESP", "ITA"]); // Birth/FRA excluded
        ferrante.Single(a => a.Id == ferrante2.Id).NationalityIsos.ShouldBeEmpty();
        groups[1].Authors.Select(a => a.Id).ShouldBe([zolaA.Id, zolaB.Id]); // 1 book first, then 0
    }

    [Fact]
    public async Task Duplicates_is_empty_when_every_name_is_unique()
    {
        AddAuthor("Um");
        AddAuthor("Outro");
        await SaveAsync();

        (await Service().GetDuplicatesAsync(default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Same_book_ordering_ties_break_by_age()
    {
        var newer = AddAuthor("Elena Ferrante", Now);
        var older = AddAuthor("Elena Ferrante", Now.AddYears(-1));
        await SaveAsync();

        var group = (await Service().GetDuplicatesAsync(default)).ShouldHaveSingleItem();

        group.Authors.Select(a => a.Id).ShouldBe([older.Id, newer.Id]);
    }
}
