using System.Net;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.ExternalSources;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Infrastructure.ExternalSources;
using AtlasLiterario.Infrastructure.Tests.Support;

namespace AtlasLiterario.Infrastructure.Tests;

public sealed class GoogleBooksClientTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public async Task Parses_real_search_with_isbns_https_covers_and_categories()
    {
        _env.Http.On("/books/v1/volumes?q=o processo kafka", "googlebooks-search-kafka-o-processo.json");

        var results = await _env.GoogleBooks().SearchAsync(new BookSearchQuery("o processo kafka", BookSearchType.All, 10), default);

        results.Count.ShouldBe(10);
        var first = results[0];
        first.Provider.ShouldBe(ExternalProvider.GoogleBooks);
        first.ExternalId.ShouldBe("xSv3DwAAQBAJ");
        first.Title.ShouldBe("O processo");
        first.Authors.Select(a => a.Name).ShouldBe(["Franz Kafka"]);
        first.Isbn13.ShouldBe("9788567097855");
        first.Isbn10.ShouldBe("8567097851");
        first.PublicationYear.ShouldBe(2020);
        first.EditionLanguage.ShouldBe("pt-BR");
        first.CoverUrl!.ShouldStartWith("https://");
        first.CoverUrl.ShouldNotContain("edge=curl");
        results.Select(r => r.ExternalId).ShouldBeUnique();
    }

    [Fact]
    public async Task Volume_description_html_is_converted_to_text()
    {
        _env.Http.On("/books/v1/volumes/xSv3DwAAQBAJ", "googlebooks-volume-xSv3DwAAQBAJ.json");

        var volume = await _env.GoogleBooks().GetAsync("xSv3DwAAQBAJ", default);

        volume.ShouldNotBeNull();
        volume.Description!.ShouldNotContain("<");
        volume.Description.ShouldStartWith("Um dos principais romances de Franz Kafka");
        volume.Categories.ShouldContain("Fiction / Classics");
    }

    [Fact]
    public async Task Quota_exceeded_429_becomes_external_unavailable()
    {
        _env.Http.On("/books/v1/volumes", "googlebooks-429.json", HttpStatusCode.TooManyRequests);

        var ex = await Should.ThrowAsync<ExternalServiceUnavailableException>(() =>
            _env.GoogleBooks().SearchAsync(new BookSearchQuery("dom casmurro", BookSearchType.Title, 5), default));

        ex.Message.ShouldContain("429");
    }

    [Fact]
    public async Task Api_key_is_sent_as_header_and_never_in_the_url()
    {
        _env.Http.On("/books/v1/volumes?q=isbn:9788535914849", "googlebooks-search-isbn-9788535914849.json");

        var results = await _env.GoogleBooks(apiKey: "test-key-123")
            .SearchAsync(new BookSearchQuery("9788535914849", BookSearchType.Isbn, 5), default);

        results.ShouldHaveSingleItem().Isbn13.ShouldBe("9788535914849");
        var request = _env.Http.Requests.ShouldHaveSingleItem();
        request.RequestUri!.ToString().ShouldNotContain("test-key-123");
        request.RequestUri.Query.ShouldNotContain("key=");
        request.RequestUri.Query.ShouldContain("country=BR");
        request.Headers.GetValues(GoogleBooksClient.ApiKeyHeader).ShouldBe(["test-key-123"]);
    }

    [Theory]
    [InlineData("<p>Um <b>romance</b>.</p><p>Outro &amp; mais</p>", "Um romance.\n\nOutro & mais")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Strip_html(string? html, string? expected) => GoogleBooksClient.StripHtml(html).ShouldBe(expected);
}

public sealed class OpenLibraryClientTests : IDisposable
{
    private readonly TestEnvironment _env = new TestEnvironment().WithRecordedSources();

    public void Dispose() => _env.Dispose();

    [Theory]
    [InlineData("\u0098O \u009cprocesso", "O processo")]
    [InlineData("  Dom Casmurro ", "Dom Casmurro")]
    [InlineData("\u0098\u009c", null)]
    public void Open_library_non_sorting_control_characters_are_removed(string raw, string? expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { title = raw });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.Str("title").ShouldBe(expected);
    }

    [Fact]
    public async Task Search_returns_works_with_author_keys_and_no_edition_specific_isbn()
    {
        var results = await _env.OpenLibrary().SearchAsync(new BookSearchQuery("o processo kafka", BookSearchType.All, 5), default);

        results.Count.ShouldBeGreaterThan(0);
        var trial = results.First(r => r.ExternalId == "OL498463W");
        trial.Title.ShouldBe("Der Proceß");
        trial.Authors.ShouldHaveSingleItem().ShouldBe(new ExternalAuthorRef("Franz Kafka", "OL33146A"));
        trial.OpenLibraryWorkId.ShouldBe("OL498463W");
        trial.Isbn13.ShouldBeNull();
        trial.CoverUrl.ShouldBe("https://covers.openlibrary.org/b/id/997423-L.jpg");
    }

    [Fact]
    public async Task Work_combines_work_record_and_search_index()
    {
        var work = await _env.OpenLibrary().GetAsync("OL274505W", default);

        work.ShouldNotBeNull();
        work.Title.ShouldBe("Cien años de soledad");
        work.Authors.ShouldHaveSingleItem().OpenLibraryKey.ShouldBe("OL27363A");
        work.PublicationYear.ShouldBe(1967);
        work.OpenLibraryWorkId.ShouldBe("OL274505W");
    }

    [Fact]
    public async Task Isbn_search_returns_the_edition_with_authors_from_the_work()
    {
        var result = (await _env.OpenLibrary().SearchAsync(new BookSearchQuery("9788535914849", BookSearchType.Isbn, 5), default))
            .ShouldHaveSingleItem();

        result.ExternalId.ShouldBe("OL26449397M");
        result.Title.ShouldBe("1984");
        result.Isbn13.ShouldBe("9788535914849");
        result.Publisher.ShouldBe("Companhia das Letras");
        result.OpenLibraryWorkId.ShouldBe("OL1168083W");
        result.Authors.ShouldHaveSingleItem().Name.ShouldBe("George Orwell");
    }

    [Fact]
    public async Task Unknown_isbn_returns_no_result()
    {
        _env.Http.OnStatus("/isbn/9780306406157.json", HttpStatusCode.NotFound);

        (await _env.OpenLibrary().SearchAsync(new BookSearchQuery("9780306406157", BookSearchType.Isbn, 5), default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Author_resolution_reads_wikidata_viaf_and_birth_date_and_keeps_unknowns_unknown()
    {
        _env.Http.OnStatus("/search/authors.json", HttpStatusCode.NotFound); // name lookup without a match
        var candidate = new ExternalBookCandidate(ExternalProvider.OpenLibrary, "x", "x", null,
            [new ExternalAuthorRef("Franz Kafka", "OL33146A"), new ExternalAuthorRef("George Orwell", "OL15318546A"), new ExternalAuthorRef("Sem Chave", null)],
            null, null, null, null, null, null, [], null, null);

        var authors = await _env.OpenLibrary().ResolveAuthorsAsync(candidate, default);

        authors[0].ShouldBe(new ResolvedAuthor("Franz Kafka", "OL33146A", "Q905", "56611857", new DateOnly(1883, 7, 3)));
        authors[1].WikidataQid.ShouldBeNull(); // the Open Library record has no Wikidata id: never guessed
        authors[2].ShouldBe(new ResolvedAuthor("Sem Chave", null, null, null, null));
    }

    [Fact]
    public async Task Responses_are_cached()
    {
        var client = _env.OpenLibrary();
        await client.GetAsync("OL1003040W", default);
        await client.GetAsync("OL1003040W", default);

        _env.Http.CountRequests("/works/OL1003040W.json").ShouldBe(1);
    }
}

public sealed class WikidataClientTests : IDisposable
{
    private readonly TestEnvironment _env = new TestEnvironment().WithRecordedSources();

    public void Dispose() => _env.Dispose();

    [Fact]
    public async Task Kafka_citizenship_uses_historical_mappings_with_reduced_confidence()
    {
        // Real P27: Czechoslovakia (P298 "CSK", not in the country table) and Cisleithania (no P298).
        var countries = await _env.Wikidata().GetAuthorCitizenshipsAsync("Q905", default);

        countries.Select(c => c.IsoCode).ShouldBe(["CZE", "AUT"], ignoreOrder: true);
        countries.ShouldAllBe(c => c.Confidence == WikidataClient.MappedConfidence && c.ViaHistoricalMapping && c.Source == GeoSource.Wikidata);
    }

    [Fact]
    public async Task Direct_citizenship_has_high_confidence()
    {
        var murakami = (await _env.Wikidata().GetAuthorCitizenshipsAsync("Q134798", default)).ShouldHaveSingleItem();

        murakami.IsoCode.ShouldBe("JPN");
        murakami.Confidence.ShouldBe(WikidataClient.DirectConfidence);
        murakami.ViaHistoricalMapping.ShouldBeFalse();
    }

    [Fact]
    public async Task Deprecated_statements_are_ignored()
    {
        // Picasso: Spain (normal) + France (deprecated). Dickens: UK (deprecated) + UK of GB and Ireland (normal → mapped).
        var picasso = await _env.Wikidata().GetAuthorCitizenshipsAsync("Q5593", default);
        var dickens = await _env.Wikidata().GetAuthorCitizenshipsAsync("Q5686", default);

        picasso.Select(c => c.IsoCode).ShouldBe(["ESP"]);
        var gbr = dickens.ShouldHaveSingleItem();
        gbr.IsoCode.ShouldBe("GBR");
        gbr.ViaHistoricalMapping.ShouldBeTrue(); // only the normal-rank historical state counted
    }

    [Fact]
    public void Preferred_rank_wins_over_normal()
    {
        WikidataClient.Statement S(string value, string rank) => new("country", value, rank, null, null);

        var selected = WikidataClient.SelectByRank([S("Q1", "NormalRank"), S("Q2", "PreferredRank"), S("Q3", "DeprecatedRank")]);

        selected.Select(s => s.Value).ShouldBe(["Q2"]);
    }

    [Fact]
    public async Task Work_origins_and_original_language()
    {
        var client = _env.Wikidata();

        (await client.GetWorkOriginsAsync("Q36097", default)).Select(c => c.IsoCode).ShouldBe(["AUT", "DEU"], ignoreOrder: true);
        (await client.GetWorkOriginalLanguageAsync("Q36097", default)).ShouldBe("de");
        // 1984: English + a language item without ISO 639-1 code → "en".
        (await client.GetWorkOriginalLanguageAsync("Q208460", default)).ShouldBe("en");
        // Only one SPARQL request per work (origins and language share the cached statements).
        _env.Http.CountRequests("wd:Q36097 p:P495").ShouldBe(1);
    }

    [Fact]
    public async Task Work_is_found_by_open_library_id_then_by_title_and_author()
    {
        var client = _env.Wikidata();

        (await client.FindWorkQidAsync(null, "OL498463W", "Der Proceß", "Q905", default)).ShouldBe("Q36097");
        // Norwegian Wood has no P648 link: falls back to exact label + P50.
        (await client.FindWorkQidAsync(null, "OL2625457W", "Norwegian Wood", "Q134798", default)).ShouldBe("Q751348");
    }

    [Fact]
    public async Task Work_without_any_link_and_without_author_qid_stays_unknown()
    {
        (await _env.Wikidata().FindWorkQidAsync(null, "OL2625457W", "Norwegian Wood", null, default)).ShouldBeNull();
    }

    [Fact]
    public async Task Sparql_failure_becomes_external_unavailable()
    {
        var env = new TestEnvironment();
        env.Http.OnStatus("sparql", HttpStatusCode.ServiceUnavailable);

        await Should.ThrowAsync<ExternalServiceUnavailableException>(() => env.Wikidata().GetAuthorCitizenshipsAsync("Q905", default));
    }

    [Fact]
    public void Sparql_literals_are_escaped()
    {
        WikidataClient.FindByTitleQuery("Q1", "A \"quoted\" \\ title").ShouldContain("\"a \\\"quoted\\\" \\\\ title\"");
    }
}

public sealed class OpenLibraryAuthorIdentityTests : IDisposable
{
    private readonly TestEnvironment _env = new TestEnvironment().WithRecordedSources();

    public void Dispose() => _env.Dispose();

    private static ExternalBookCandidate WithAuthors(params string[] names) =>
        new(ExternalProvider.GoogleBooks, "g", "t", null, names.Select(n => new ExternalAuthorRef(n, null)).ToList(),
            null, null, null, null, null, null, [], null, null);

    [Fact]
    public async Task Name_only_authors_are_identified_when_one_exact_match_dominates()
    {
        var authors = await _env.OpenLibrary().ResolveAuthorsAsync(
            WithAuthors("Franz Kafka", "Gabriel García Márquez", "Haruki Murakami"), default);

        authors[0].WikidataQid.ShouldBe("Q905");
        authors[1].WikidataQid.ShouldBe("Q5878");
        // Matched through Open Library's alternate names; the Latin-script source name is kept for display.
        authors[2].OpenLibraryKey.ShouldBe("OL382524A");
        authors[2].WikidataQid.ShouldBe("Q134798");
        authors[2].Name.ShouldBe("Haruki Murakami");
    }

    [Fact]
    public async Task Surname_only_names_are_not_guessed()
    {
        var author = (await _env.OpenLibrary().ResolveAuthorsAsync(WithAuthors("Kafka"), default)).ShouldHaveSingleItem();

        author.ShouldBe(new ResolvedAuthor("Kafka", null, null, null, null));
        _env.Http.CountRequests("/search/authors.json").ShouldBe(0);
    }

    [Fact]
    public void Weak_or_ambiguous_matches_are_rejected()
    {
        static System.Text.Json.JsonElement Docs(params (string Key, string Name, int Works)[] docs) =>
            System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new
            {
                docs = docs.Select(d => new { key = d.Key, name = d.Name, work_count = d.Works }),
            })).RootElement;

        OpenLibraryClient.SelectDominantAuthor(Docs(("OL1A", "Ana Souza", 10)), "ana souza").ShouldBeNull(); // too few works
        OpenLibraryClient.SelectDominantAuthor(Docs(("OL1A", "Ana Souza", 100), ("OL2A", "Ana Souza", 40)), "ana souza").ShouldBeNull();
        OpenLibraryClient.SelectDominantAuthor(Docs(("OL1A", "Ana Souza", 100), ("OL2A", "Ana Souza", 3)), "ana souza").ShouldBe("OL1A");
    }

    [Fact]
    public void Display_name_prefers_latin_script_from_the_source()
    {
        OpenLibraryClient.PreferDisplayName("Haruki Murakami", "村上春樹").ShouldBe("Haruki Murakami");
        OpenLibraryClient.PreferDisplayName("George Orwell", "George Orwell").ShouldBe("George Orwell");
        OpenLibraryClient.PreferDisplayName("OL382524A", "村上春樹").ShouldBe("村上春樹");
    }
}
