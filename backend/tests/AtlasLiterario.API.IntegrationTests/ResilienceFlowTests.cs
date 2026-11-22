using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Library;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasLiterario.API.IntegrationTests;

/// <summary>
/// POST /library must never lose the user's action nor exceed the request budget when Wikidata is rate limited,
/// silent or the client disconnects (E2E defect: book left Pending and no user_book after a 20 s client abort).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ResilienceFlowTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly object CasmurroSource = new { provider = "OpenLibrary", externalId = "OL1003040W" };

    public async Task InitializeAsync()
    {
        // The catalog is shared inside this fixture: start every test from an empty one.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserBooks.RemoveRange(db.UserBooks);
        db.BookOriginCountries.RemoveRange(db.BookOriginCountries);
        db.AuthorCountries.RemoveRange(db.AuthorCountries);
        db.ExternalBookIdentifiers.RemoveRange(db.ExternalBookIdentifiers);
        db.ExternalAuthorIdentifiers.RemoveRange(db.ExternalAuthorIdentifiers);
        db.BookAuthors.RemoveRange(db.BookAuthors);
        db.Books.RemoveRange(db.Books);
        db.Authors.RemoveRange(db.Authors);
        await db.SaveChangesAsync();
        factory.GeoMode = GeoMode.Normal;
        factory.Budget = TimeSpan.FromSeconds(12);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> UserClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { name = "Leitora", email = $"r-{Guid.NewGuid():N}@example.com", password = "segredo123" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<(int Books, int UserBooks, GeoEnrichmentStatus? Status)> DbStateAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var status = await db.Books.AsNoTracking().Select(b => (GeoEnrichmentStatus?)b.GeoEnrichmentStatus).FirstOrDefaultAsync();
        return (await db.Books.CountAsync(), await db.UserBooks.CountAsync(), status);
    }

    private static async Task<LibraryItemDto> CreatedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        return JsonSerializer.Deserialize<LibraryItemDto>(body, Json)!;
    }

    [Fact]
    public async Task Rate_limited_wikidata_does_not_block_or_lose_the_book()
    {
        factory.GeoMode = GeoMode.RateLimited; // 429 with Retry-After 120 -> the client gives up at once
        var client = await UserClientAsync();

        var clock = Stopwatch.StartNew();
        var item = await CreatedAsync(await client.PostAsJsonAsync("/api/library", new { source = CasmurroSource, status = "WantToRead" }, Json));
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));

        item.Book.GeoEnrichmentStatus.ShouldBeOneOf(GeoEnrichmentStatus.Partial, GeoEnrichmentStatus.Failed);
        item.Book.OriginCountries.ShouldBeEmpty(); // unknown, never invented
        var state = await DbStateAsync();
        state.UserBooks.ShouldBe(1);
        (await client.GetAsync($"/api/library/{item.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Silent_wikidata_is_cut_by_the_request_budget_and_returns_201()
    {
        factory.GeoMode = GeoMode.Hang;
        factory.Budget = TimeSpan.FromSeconds(2);
        var client = await UserClientAsync();

        var clock = Stopwatch.StartNew();
        var item = await CreatedAsync(await client.PostAsJsonAsync("/api/library", new { source = CasmurroSource, status = "Read" }, Json));
        var elapsed = clock.Elapsed;

        elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2) + TimeSpan.FromSeconds(3), "budget + margin");
        item.Book.GeoEnrichmentStatus.ShouldBeOneOf(GeoEnrichmentStatus.Partial, GeoEnrichmentStatus.Failed);
        var state = await DbStateAsync();
        (state.Books, state.UserBooks).ShouldBe((1, 1));
        state.Status.ShouldBe(item.Book.GeoEnrichmentStatus);
    }

    [Fact]
    public async Task Client_abort_during_enrichment_keeps_book_and_user_book_consistent()
    {
        factory.GeoMode = GeoMode.Hang;
        factory.Budget = TimeSpan.FromSeconds(30);
        var client = await UserClientAsync();

        using var abort = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<OperationCanceledException>(
            () => client.PostAsJsonAsync("/api/library", new { source = CasmurroSource, status = "WantToRead" }, Json, abort.Token));

        // The server finishes the commit on its own: poll until the status is no longer Pending.
        (int Books, int UserBooks, GeoEnrichmentStatus? Status) state = default;
        for (var i = 0; i < 50; i++)
        {
            state = await DbStateAsync();
            if (state.Status is not null and not GeoEnrichmentStatus.Pending)
            {
                break;
            }

            await Task.Delay(100);
        }

        state.Books.ShouldBe(1);
        state.UserBooks.ShouldBe(1);
        state.Status.ShouldBeOneOf(GeoEnrichmentStatus.Partial, GeoEnrichmentStatus.Failed);

        // The user can see the item and retry the enrichment later.
        factory.GeoMode = GeoMode.Normal;
        var list = await client.GetFromJsonAsync<AtlasLiterario.Application.Common.PagedResult<LibraryItemDto>>("/api/library", Json);
        list!.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Adding_an_unfinished_book_by_id_triggers_enrichment_again_and_completed_books_are_not_reprocessed()
    {
        factory.GeoMode = GeoMode.RateLimited;
        var first = await UserClientAsync();
        var failed = await CreatedAsync(await first.PostAsJsonAsync("/api/library", new { source = CasmurroSource, status = "WantToRead" }, Json));
        failed.Book.GeoEnrichmentStatus.ShouldNotBe(GeoEnrichmentStatus.Completed);

        // Wikidata is back: another user adds the same catalog book by id -> it is enriched now.
        factory.GeoMode = GeoMode.Normal;
        var second = await UserClientAsync();
        var healed = await CreatedAsync(await second.PostAsJsonAsync("/api/library", new { bookId = failed.Book.Id, status = "Read" }, Json));
        healed.Book.GeoEnrichmentStatus.ShouldBe(GeoEnrichmentStatus.Completed);
        healed.Book.OriginCountries.ShouldHaveSingleItem().CountryIso.ShouldBe("BRA");
        healed.Book.Id.ShouldBe(failed.Book.Id);

        // Completed already: a third user adding it does not call Wikidata.
        var callsBefore = factory.GeoCalls;
        var third = await UserClientAsync();
        var again = await CreatedAsync(await third.PostAsJsonAsync("/api/library", new { bookId = failed.Book.Id, status = "WantToRead" }, Json));
        again.Book.GeoEnrichmentStatus.ShouldBe(GeoEnrichmentStatus.Completed);
        factory.GeoCalls.ShouldBe(callsBefore);
    }

    [Fact]
    public async Task Repeating_the_same_add_does_not_duplicate_anything()
    {
        factory.GeoMode = GeoMode.RateLimited;
        var client = await UserClientAsync();
        var created = await CreatedAsync(await client.PostAsJsonAsync("/api/library", new { source = CasmurroSource, status = "WantToRead" }, Json));

        var repeat = await client.PostAsJsonAsync("/api/library", new { source = CasmurroSource, status = "WantToRead" }, Json);

        repeat.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = JsonDocument.Parse(await repeat.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("library.duplicate");
        problem.GetProperty("libraryItemId").GetString().ShouldBe(created.Id.ToString());

        // Same through the catalog id.
        var byId = await client.PostAsJsonAsync("/api/library", new { bookId = created.Book.Id, status = "Read" }, Json);
        byId.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await byId.Content.ReadAsStringAsync()).RootElement.GetProperty("libraryItemId").GetString()
            .ShouldBe(created.Id.ToString());
        var state = await DbStateAsync();
        (state.Books, state.UserBooks).ShouldBe((1, 1));
    }
}
