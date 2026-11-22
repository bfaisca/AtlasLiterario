using AtlasLiterario.Application.Abstractions;
using AtlasLiterario.Infrastructure.ExternalSources;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtlasLiterario.Infrastructure.Tests.Support;

public sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
}

/// <summary>InMemory database (with country/mapping seed) + real clients wired to a fixture handler.</summary>
public sealed class TestEnvironment : IDisposable
{
    public TestEnvironment()
    {
        Options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"infra-{Guid.NewGuid()}").Options;
        Db = new AppDbContext(Options);
        Db.Database.EnsureCreated();
        Cache = new ExternalLookupCacheStore(Options, Clock, NullLogger<ExternalLookupCacheStore>.Instance);
    }

    public DbContextOptions<AppDbContext> Options { get; }

    public AppDbContext Db { get; }

    public TestClock Clock { get; } = new();

    public FixtureHttpHandler Http { get; } = new();

    public ExternalLookupCacheStore Cache { get; }

    public GoogleBooksClient GoogleBooks(string apiKey = "")
    {
        var options = Microsoft.Extensions.Options.Options.Create(new GoogleBooksOptions { ApiKey = apiKey, Country = "BR" });
        var pipeline = new GoogleBooksApiKeyHandler(options) { InnerHandler = Http };
        return new GoogleBooksClient(new HttpClient(pipeline) { BaseAddress = new Uri(GoogleBooksClient.BaseAddress) }, options, Cache);
    }

    public OpenLibraryClient OpenLibrary() =>
        new(new HttpClient(Http, disposeHandler: false) { BaseAddress = new Uri(OpenLibraryClient.BaseAddress) }, Cache);

    public WikidataClient Wikidata() =>
        new(
            new HttpClient(Http, disposeHandler: false) { BaseAddress = new Uri(WikidataClient.BaseAddress) },
            Cache,
            new CountryCatalog(new AppDbContext(Options)),
            NullLogger<WikidataClient>.Instance);

    /// <summary>Routes for every captured Open Library / Wikidata fixture.</summary>
    public TestEnvironment WithRecordedSources()
    {
        Http
            // Open Library
            .On("/search.json?q=key:/works/OL498463W", "openlibrary-search-key-OL498463W.json")
            .On("/search.json?q=key:/works/OL274505W", "openlibrary-search-key-OL274505W.json")
            .On("/search.json?q=key:/works/OL2625457W", "openlibrary-search-key-OL2625457W.json")
            .On("/search.json?q=key:/works/OL1003040W", "openlibrary-search-key-OL1003040W.json")
            .On("/search.json?q=key:/works/OL1168083W", "openlibrary-search-key-OL1168083W.json")
            .On("/search.json?q=o processo kafka", "openlibrary-search-kafka-o-processo.json")
            .On("/search.json?q=cem anos de solidão", "openlibrary-search-garcia-marquez-cem-anos.json")
            .On("/search.json?q=norwegian wood murakami", "openlibrary-search-murakami-norwegian-wood.json")
            .On("/search.json?q=dom casmurro", "openlibrary-search-machado-dom-casmurro.json")
            .On("/works/OL498463W.json", "openlibrary-work-OL498463W.json")
            .On("/works/OL274505W.json", "openlibrary-work-OL274505W.json")
            .On("/works/OL2625457W.json", "openlibrary-work-OL2625457W.json")
            .On("/works/OL1003040W.json", "openlibrary-work-OL1003040W.json")
            .On("/works/OL1168083W.json", "openlibrary-work-OL1168083W.json")
            .On("/isbn/9788535914849.json", "openlibrary-edition-OL26449397M.json")
            .On("/books/OL26449397M.json", "openlibrary-edition-OL26449397M.json")
            .On("/authors/OL33146A.json", "openlibrary-author-OL33146A.json")
            .On("/authors/OL27363A.json", "openlibrary-author-OL27363A.json")
            .On("/authors/OL382524A.json", "openlibrary-author-OL382524A.json")
            .On("/authors/OL93286A.json", "openlibrary-author-OL93286A.json")
            .On("/authors/OL15318546A.json", "openlibrary-author-OL15318546A.json")
            .On("/search/authors.json?q=Franz Kafka", "openlibrary-authorsearch-franz-kafka.json")
            .On("/search/authors.json?q=Gabriel García Márquez", "openlibrary-authorsearch-gabriel-garcía-márquez.json")
            .On("/search/authors.json?q=Haruki Murakami", "openlibrary-authorsearch-haruki-murakami.json")
            // Wikidata SPARQL
            .On("wd:Q905 p:P27", "wikidata-citizenship-Q905.json")
            .On("wd:Q5878 p:P27", "wikidata-citizenship-Q5878.json")
            .On("wd:Q134798 p:P27", "wikidata-citizenship-Q134798.json")
            .On("wd:Q311145 p:P27", "wikidata-citizenship-Q311145.json")
            .On("wd:Q5686 p:P27", "wikidata-citizenship-Q5686.json")
            .On("wd:Q5593 p:P27", "wikidata-citizenship-Q5593.json")
            .On("wdt:P648 \"OL498463W\"", "wikidata-findwork-OL498463W.json")
            .On("wdt:P648 \"OL274505W\"", "wikidata-findwork-OL274505W.json")
            .On("wdt:P648 \"OL2625457W\"", "wikidata-findwork-OL2625457W.json")
            .On("wdt:P648 \"OL1003040W\"", "wikidata-findwork-OL1003040W.json")
            .On("wdt:P648 \"OL1168083W\"", "wikidata-findwork-OL1168083W.json")
            .On("wdt:P50 wd:Q134798", "wikidata-findwork-title-Q134798-norwegian-wood.json")
            .On("wdt:P50 wd:Q905", "wikidata-findwork-title-Q905-o-processo.json")
            .On("wd:Q36097 p:P495", "wikidata-work-Q36097.json")
            .On("wd:Q178869 p:P495", "wikidata-work-Q178869.json")
            .On("wd:Q1236531 p:P495", "wikidata-work-Q1236531.json")
            .On("wd:Q208460 p:P495", "wikidata-work-Q208460.json")
            .On("wd:Q751348 p:P495", "wikidata-work-Q751348.json");
        return this;
    }

    public void Dispose() => Db.Dispose();
}
