using System.Net;
using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Domain.Exceptions;
using AtlasLiterario.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AtlasLiterario.Infrastructure.Tests;

/// <summary>Application services + real clients against recorded responses (search → import → enrichment).</summary>
public sealed class ImportPipelineTests : IDisposable
{
    private readonly TestEnvironment _env = new();
    private readonly FakeCurrentUser _user = new() { UserId = Guid.NewGuid() };

    public void Dispose() => _env.Dispose();

    private BookImportService Importer()
    {
        var openLibrary = _env.OpenLibrary();
        return new BookImportService(
            _env.Db, [_env.GoogleBooks(), openLibrary], openLibrary, _env.Wikidata(), _env.Clock,
            NullLogger<BookImportService>.Instance);
    }

    private BookSearchService Searcher() =>
        new(_env.Db, _user, [_env.GoogleBooks(), _env.OpenLibrary()], NullLogger<BookSearchService>.Instance);

    private Task<Book> LoadAsync(Guid id) =>
        _env.Db.Books.AsNoTracking()
            .Include(b => b.Authors).ThenInclude(ba => ba.Author!).ThenInclude(a => a.Countries).ThenInclude(c => c.Country)
            .Include(b => b.Authors).ThenInclude(ba => ba.Author!).ThenInclude(a => a.ExternalIdentifiers)
            .Include(b => b.OriginCountries).ThenInclude(o => o.Country)
            .Include(b => b.ExternalIdentifiers)
            .SingleAsync(b => b.Id == id);

    [Fact]
    public async Task Importing_Kafka_resolves_author_identity_citizenship_origin_and_language()
    {
        _env.WithRecordedSources();

        var id = await Importer().ImportAsync(new ExternalSourceRef(ExternalProvider.OpenLibrary, "OL498463W"), default);

        var book = await LoadAsync(id);
        book.Title.ShouldBe("Der Proceß");
        book.OriginalLanguage.ShouldBe("de");
        book.GeoEnrichmentStatus.ShouldBe(GeoEnrichmentStatus.Completed);
        book.ExternalIdentifiers.Select(i => (i.Provider, i.ExternalId)).ShouldBe(
            [(BookIdentifierProvider.OpenLibraryWork, "OL498463W"), (BookIdentifierProvider.Wikidata, "Q36097")], ignoreOrder: true);
        book.OriginCountries.Select(o => o.Country!.IsoCode).ShouldBe(["AUT", "DEU"], ignoreOrder: true);
        book.OriginCountries.ShouldAllBe(o => o.Source == GeoSource.Wikidata && o.Confidence == 0.9m && !o.IsVerified);

        var kafka = book.Authors.ShouldHaveSingleItem().Author!;
        kafka.Name.ShouldBe("Franz Kafka");
        kafka.BirthDate.ShouldBe(new DateOnly(1883, 7, 3));
        kafka.ExternalIdentifiers.Select(i => i.Provider).ShouldBe(
            [AuthorIdentifierProvider.Wikidata, AuthorIdentifierProvider.OpenLibrary, AuthorIdentifierProvider.Viaf], ignoreOrder: true);
        kafka.Countries.Select(c => c.Country!.IsoCode).ShouldBe(["CZE", "AUT"], ignoreOrder: true);
        kafka.Countries.ShouldAllBe(c => c.RelationshipType == AuthorCountryRelationship.Citizenship && c.Confidence == 0.6m);
    }

    [Fact]
    public async Task Importing_twice_does_not_duplicate_book_or_author()
    {
        _env.WithRecordedSources();
        var importer = Importer();

        var first = await importer.ImportAsync(new ExternalSourceRef(ExternalProvider.OpenLibrary, "OL274505W"), default);
        var second = await importer.ImportAsync(new ExternalSourceRef(ExternalProvider.OpenLibrary, "OL274505W"), default);

        second.ShouldBe(first);
        (await _env.Db.Books.CountAsync()).ShouldBe(1);
        (await _env.Db.Authors.CountAsync()).ShouldBe(1);
        var book = await LoadAsync(first);
        book.Authors.Single().Author!.Countries.Single().Country!.IsoCode.ShouldBe("COL");
        // Wikidata P495 of "Cien años de soledad" is Argentina (first edition, Buenos Aires): stored as reported.
        book.OriginCountries.Single().Country!.IsoCode.ShouldBe("ARG");
    }

    [Fact]
    public async Task Same_isbn_from_another_source_reuses_the_book_and_author_is_reused_by_identifier()
    {
        _env.Http.On("/books/v1/volumes/-gfjwAEACAAJ", "googlebooks-volume--gfjwAEACAAJ.json");
        _env.WithRecordedSources();
        var importer = Importer();

        var fromOpenLibrary = await importer.ImportAsync(new ExternalSourceRef(ExternalProvider.OpenLibrary, "OL26449397M"), default);
        var fromGoogle = await importer.ImportAsync(new ExternalSourceRef(ExternalProvider.GoogleBooks, "-gfjwAEACAAJ"), default);

        fromGoogle.ShouldBe(fromOpenLibrary);
        var book = await LoadAsync(fromOpenLibrary);
        book.Isbn13.ShouldBe("9788535914849");
        book.ExternalIdentifiers.Select(i => i.Provider).ShouldBe(
            [BookIdentifierProvider.OpenLibraryEdition, BookIdentifierProvider.OpenLibraryWork, BookIdentifierProvider.Wikidata, BookIdentifierProvider.GoogleBooks],
            ignoreOrder: true);
        (await _env.Db.Authors.CountAsync()).ShouldBe(1);

        // Orwell's Open Library record has no Wikidata id: nationality unknown, origin known → Partial.
        book.Authors.Single().Author!.Countries.ShouldBeEmpty();
        book.OriginCountries.Single().Country!.IsoCode.ShouldBe("GBR");
        book.GeoEnrichmentStatus.ShouldBe(GeoEnrichmentStatus.Partial);
    }

    [Fact]
    public async Task Google_edition_unknown_to_open_library_gets_identity_by_name_and_work_by_title()
    {
        // Real case: Brazilian edition of "O processo" (Google Books) has no Open Library record for its ISBN.
        _env.Http
            .On("/books/v1/volumes/xSv3DwAAQBAJ", "googlebooks-volume-xSv3DwAAQBAJ.json")
            .OnStatus("/isbn/9788567097855.json", HttpStatusCode.NotFound);
        _env.WithRecordedSources();

        var id = await Importer().ImportAsync(new ExternalSourceRef(ExternalProvider.GoogleBooks, "xSv3DwAAQBAJ"), default);

        var book = await LoadAsync(id);
        book.Title.ShouldBe("O processo");
        book.Isbn13.ShouldBe("9788567097855");
        book.Genres.ShouldContain("Fiction / Classics");
        var kafka = book.Authors.Single().Author!;
        kafka.ExternalIdentifiers.ShouldContain(i => i.Provider == AuthorIdentifierProvider.Wikidata && i.ExternalId == "Q905");
        kafka.Countries.Select(c => c.Country!.IsoCode).ShouldBe(["CZE", "AUT"], ignoreOrder: true);
        book.ExternalIdentifiers.ShouldContain(i => i.Provider == BookIdentifierProvider.Wikidata && i.ExternalId == "Q36097");
        book.OriginCountries.Select(o => o.Country!.IsoCode).ShouldBe(["AUT", "DEU"], ignoreOrder: true);
        book.GeoEnrichmentStatus.ShouldBe(GeoEnrichmentStatus.Completed);
    }

    [Fact]
    public async Task Work_without_open_library_link_is_found_by_title_and_author()
    {
        _env.WithRecordedSources();

        var id = await Importer().ImportAsync(new ExternalSourceRef(ExternalProvider.OpenLibrary, "OL2625457W"), default);

        var book = await LoadAsync(id);
        book.OriginCountries.Single().Country!.IsoCode.ShouldBe("JPN");
        book.OriginalLanguage.ShouldBe("ja");
        book.Authors.Single().Author!.Countries.Single().Country!.IsoCode.ShouldBe("JPN");
        book.GeoEnrichmentStatus.ShouldBe(GeoEnrichmentStatus.Completed);
    }

    [Fact]
    public async Task Manual_relations_survive_reprocessing()
    {
        _env.WithRecordedSources();
        var importer = Importer();
        var id = await importer.ImportAsync(new ExternalSourceRef(ExternalProvider.OpenLibrary, "OL1003040W"), default);
        var userId = Guid.NewGuid();
        var portugal = await _env.Db.Countries.SingleAsync(c => c.IsoCode == "PRT");

        var book = await _env.Db.Books.Include(b => b.OriginCountries).Include(b => b.Authors).ThenInclude(ba => ba.Author!).ThenInclude(a => a.Countries)
            .SingleAsync(b => b.Id == id);
        foreach (var origin in book.OriginCountries.ToList())
        {
            book.OriginCountries.Remove(origin);
        }

        book.OriginCountries.Add(BookOriginCountry.CreateManual(id, portugal.Id, userId, _env.Clock.UtcNow));
        var machado = book.Authors.Single().Author!;
        machado.Countries.Clear();
        machado.Countries.Add(AuthorCountry.CreateManual(machado.Id, portugal.Id, AuthorCountryRelationship.Citizenship, userId, _env.Clock.UtcNow));
        await _env.Db.SaveChangesAsync();
        _env.Db.ChangeTracker.Clear();

        await importer.EnrichAsync(id, default);

        var reloaded = await LoadAsync(id);
        var origin2 = reloaded.OriginCountries.ShouldHaveSingleItem();
        origin2.Country!.IsoCode.ShouldBe("PRT");
        origin2.Source.ShouldBe(GeoSource.Manual);
        origin2.UpdatedByUserId.ShouldBe(userId);
        var citizenship = reloaded.Authors.Single().Author!.Countries.ShouldHaveSingleItem();
        citizenship.Country!.IsoCode.ShouldBe("PRT");
        citizenship.IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task Wikidata_outage_keeps_the_book_with_failed_enrichment()
    {
        _env.Http.OnStatus("sparql", HttpStatusCode.ServiceUnavailable);
        _env.WithRecordedSources();

        var id = await Importer().ImportAsync(new ExternalSourceRef(ExternalProvider.OpenLibrary, "OL498463W"), default);

        var book = await LoadAsync(id);
        book.Title.ShouldBe("Der Proceß");
        book.GeoEnrichmentStatus.ShouldBe(GeoEnrichmentStatus.Failed);
        book.OriginCountries.ShouldBeEmpty();
        book.Authors.Single().Author!.Countries.ShouldBeEmpty(); // unknown, not "no country"
    }

    [Fact]
    public async Task Unknown_source_id_is_not_found_and_unsupported_provider_is_invalid()
    {
        _env.Http.OnStatus("/works/OL1W.json", HttpStatusCode.NotFound);

        (await Should.ThrowAsync<NotFoundException>(() =>
            Importer().ImportAsync(new ExternalSourceRef(ExternalProvider.OpenLibrary, "OL1W"), default))).Code.ShouldBe("book.source_not_found");
        (await Should.ThrowAsync<ValidationException>(() =>
            Importer().ImportAsync(new ExternalSourceRef(ExternalProvider.Wikidata, "Q36097"), default))).Code.ShouldBe("book.invalid_source");
    }

    [Fact]
    public async Task Search_degrades_to_open_library_when_google_books_quota_is_exceeded()
    {
        _env.Http.On("/books/v1/volumes", "googlebooks-429.json", HttpStatusCode.TooManyRequests);
        _env.WithRecordedSources();

        var results = await Searcher().SearchAsync("o processo kafka", null, 10, default);

        results.ShouldNotBeEmpty();
        results.ShouldAllBe(r => r.Source.Provider == ExternalProvider.OpenLibrary);
        results.ShouldContain(r => r.ResultKey == "OpenLibrary:OL498463W");
    }

    [Fact]
    public async Task Search_merges_sources_and_removes_duplicates_by_isbn_and_title_author()
    {
        _env.Http.On("/books/v1/volumes?q=o processo kafka", "googlebooks-search-kafka-o-processo.json");
        _env.WithRecordedSources();

        var results = await Searcher().SearchAsync("o processo kafka", "all", 40, default);

        results.Select(r => r.ResultKey).ShouldBeUnique();
        results.Where(r => r.Isbn13 != null).Select(r => r.Isbn13).ShouldBeUnique();
        var titleAuthor = results.Where(r => r.Authors.Count > 0)
            .Select(r => Domain.Common.TextNormalizer.NormalizeForComparison(r.Title) + "|" + Domain.Common.TextNormalizer.NormalizeForComparison(r.Authors[0]))
            .ToList();
        titleAuthor.ShouldBeUnique();
        // Google Books has metadata priority: "O processo"/Franz Kafka comes from Google, the OL duplicate is dropped.
        results.First(r => r.Title == "O processo" && r.Authors[0] == "Franz Kafka").Source.Provider.ShouldBe(ExternalProvider.GoogleBooks);
        results.ShouldContain(r => r.ResultKey == "OpenLibrary:OL498463W"); // "Der Proceß" is a different title
    }

    [Fact]
    public async Task Search_fails_with_502_only_when_every_source_fails_and_nothing_is_local()
    {
        _env.Http.On("/books/v1/volumes", "googlebooks-429.json", HttpStatusCode.TooManyRequests);
        _env.Http.OnStatus("/search.json", HttpStatusCode.ServiceUnavailable);

        await Should.ThrowAsync<ExternalServiceUnavailableException>(() => Searcher().SearchAsync("dom casmurro", null, 10, default));

        var book = new Book("Dom Casmurro", _env.Clock.UtcNow);
        _env.Db.Books.Add(book);
        await _env.Db.SaveChangesAsync();

        var local = (await Searcher().SearchAsync("dom casmurro", null, 10, default)).ShouldHaveSingleItem();
        local.ResultKey.ShouldBe($"local:{book.Id}");
        local.Source.ShouldBe(new ExternalSourceRef(ExternalProvider.Local, book.Id.ToString()));
    }

    [Fact]
    public async Task Imported_books_appear_as_local_results_with_in_library_flag()
    {
        _env.Http.On("/books/v1/volumes", "googlebooks-429.json", HttpStatusCode.TooManyRequests);
        _env.WithRecordedSources();
        var id = await Importer().ImportAsync(new ExternalSourceRef(ExternalProvider.OpenLibrary, "OL1003040W"), default);
        _env.Db.UserBooks.Add(new UserBook(_user.UserId!.Value, id, ReadingStatus.Read, 5, null, null, null, _env.Clock.UtcNow));
        await _env.Db.SaveChangesAsync();

        var results = await Searcher().SearchAsync("dom casmurro", "all", 10, default);

        var first = results[0];
        first.BookId.ShouldBe(id);
        first.InLibrary.ShouldBeTrue();
        first.LibraryItemId.ShouldNotBeNull();
        first.ResultKey.ShouldBe($"local:{id}");
        results.ShouldNotContain(r => r.ResultKey == "OpenLibrary:OL1003040W");
    }

    [Theory]
    [InlineData("a", null, "validation.failed")]
    [InlineData("dom casmurro", "publisher", "validation.failed")]
    [InlineData("9780306406158", "isbn", "book.invalid_isbn13")]
    public async Task Invalid_search_input_is_rejected(string q, string? type, string code)
    {
        (await Should.ThrowAsync<ValidationException>(() => Searcher().SearchAsync(q, type, null, default))).Code.ShouldBe(code);
    }

    [Fact]
    public void Isbn_10_is_converted_and_isbn_in_general_search_is_searched_as_isbn()
    {
        BookSearchService.ParseQuery("0-306-40615-2", "isbn", null).Term.ShouldBe("9780306406157");
        BookSearchService.ParseQuery("978-85-359-1484-9", null, null).Type.ShouldBe(BookSearchType.Isbn);
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; init; }

        public bool IsAuthenticated => UserId.HasValue;

        public Guid GetRequiredUserId() => UserId ?? throw new UnauthorizedException("auth.unauthorized", "anonymous");
    }
}
