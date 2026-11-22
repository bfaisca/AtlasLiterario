using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Authors;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Common;
using AtlasLiterario.Application.Library;
using AtlasLiterario.Application.Maps;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasLiterario.API.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class LibraryFlowTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<HttpClient> UserClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { name = "Leitora", email = $"u-{Guid.NewGuid():N}@example.com", password = "segredo123" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    /// <summary>Promotes the caller in the database (what scripts/promote-moderator.sh does).</summary>
    private async Task PromoteAsync(HttpClient client, UserRole role = UserRole.Moderator)
    {
        var me = await ReadAsync<UserProfileDto>(await client.GetAsync("/api/auth/me"), HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(u => u.Id == me.Id)).SetRole(role, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    [Fact]
    public async Task Search_add_list_update_and_delete()
    {
        var client = await UserClientAsync();

        // Search (Google Books is over quota in the fakes → Open Library only).
        var results = await ReadAsync<List<BookSearchResultDto>>(await client.GetAsync("/api/books/search?q=casmurro"), HttpStatusCode.OK);
        var hit = results.ShouldHaveSingleItem();
        hit.ResultKey.ShouldBe("OpenLibrary:OL1003040W");

        // Add from the external source (import + enrichment).
        var added = await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library", new { source = hit.Source, status = "Reading" }, Json), HttpStatusCode.Created);
        added.Status.ShouldBe(ReadingStatus.Reading);
        added.FinishedDate.ShouldBeNull();
        added.Book.Title.ShouldBe("Dom Casmurro");
        added.Book.GeoEnrichmentStatus.ShouldBe(GeoEnrichmentStatus.Completed);
        added.Book.OriginCountries.ShouldHaveSingleItem().CountryIso.ShouldBe("BRA");
        var machado = added.Book.Authors.ShouldHaveSingleItem();
        machado.Nationalities.ShouldHaveSingleItem().CountryName.ShouldBe("Brasil");

        // Adding the same book again (now by catalog id) is a conflict.
        var duplicate = await client.PostAsJsonAsync("/api/library", new { bookId = added.Book.Id, status = "Read" }, Json);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(duplicate)).ShouldBe("library.duplicate");

        // The search now returns the local book flagged as in the library.
        var again = await ReadAsync<List<BookSearchResultDto>>(await client.GetAsync("/api/books/search?q=casmurro"), HttpStatusCode.OK);
        var again1 = again.ShouldHaveSingleItem();
        again1.InLibrary.ShouldBeTrue();
        again1.LibraryItemId.ShouldBe(added.Id);

        // List with filters.
        var list = await ReadAsync<PagedResult<LibraryItemDto>>(
            await client.GetAsync("/api/library?status=Reading&countryIso=BRA&mapType=authors&sort=title&order=asc"), HttpStatusCode.OK);
        list.TotalCount.ShouldBe(1);
        list.Items.ShouldHaveSingleItem().Id.ShouldBe(added.Id);
        (await ReadAsync<PagedResult<LibraryItemDto>>(await client.GetAsync("/api/library?status=Read"), HttpStatusCode.OK))
            .TotalCount.ShouldBe(0);

        // Status → Read fills FinishedDate.
        var updated = await ReadAsync<LibraryItemDto>(
            await client.PutAsJsonAsync($"/api/library/{added.Id}",
                new { status = "Read", rating = 5, review = "Capitu!", startDate = "2025-01-10", finishedDate = (string?)null }, Json),
            HttpStatusCode.OK);
        updated.Status.ShouldBe(ReadingStatus.Read);
        updated.FinishedDate.ShouldNotBeNull();
        updated.Rating.ShouldBe(5);

        // Invalid rating → 400 with code.
        var invalid = await client.PutAsJsonAsync($"/api/library/{added.Id}", new { status = "Read", rating = 7 }, Json);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(invalid)).ShouldBe("library.invalid_rating");

        // Delete keeps the catalog book.
        (await client.DeleteAsync($"/api/library/{added.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/library/{added.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/books/{added.Book.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Users_cannot_see_or_change_each_others_items_or_correct_books_they_do_not_have()
    {
        var ana = await UserClientAsync();
        var bia = await UserClientAsync();
        var item = await ReadAsync<LibraryItemDto>(
            await ana.PostAsJsonAsync("/api/library",
                new { source = new { provider = "OpenLibrary", externalId = "OL2625457W" }, status = "Read", finishedDate = "2025-02-01" }, Json),
            HttpStatusCode.Created);

        (await bia.GetAsync($"/api/library/{item.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bia.PutAsJsonAsync($"/api/library/{item.Id}", new { status = "Abandoned" }, Json)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bia.DeleteAsync($"/api/library/{item.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync<PagedResult<LibraryItemDto>>(await bia.GetAsync("/api/library"), HttpStatusCode.OK)).TotalCount.ShouldBe(0);

        var forbidden = await bia.PutAsJsonAsync($"/api/books/{item.Book.Id}/origin-countries", new { countryIsos = new[] { "BRA" } }, Json);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(forbidden)).ShouldBe("book.not_in_library");
        var authorId = item.Book.Authors.Single().Id;
        var forbiddenAuthor = await bia.PutAsJsonAsync($"/api/authors/{authorId}/countries", new { citizenshipIsos = new[] { "BRA" } }, Json);
        forbiddenAuthor.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CodeAsync(forbiddenAuthor)).ShouldBe("author.not_in_library");
        (await ReadAsync<List<BookDetailsDto>>(await bia.GetAsync($"/api/authors/{authorId}/books"), HttpStatusCode.OK)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Owner_can_correct_origins_and_citizenships_manually()
    {
        var client = await UserClientAsync();
        await PromoteAsync(client); // changing existing countries is a moderator action
        var item = await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library",
                new { source = new { provider = "OpenLibrary", externalId = "OL2625457W" }, status = "WantToRead" }, Json),
            HttpStatusCode.Created);
        item.Book.OriginCountries.ShouldBeEmpty(); // fake Wikidata has no work for it → unknown

        var book = await ReadAsync<BookDetailsDto>(
            await client.PutAsJsonAsync($"/api/books/{item.Book.Id}/origin-countries", new { countryIsos = new[] { "jpn" } }, Json),
            HttpStatusCode.OK);
        var origin = book.OriginCountries.ShouldHaveSingleItem();
        origin.CountryIso.ShouldBe("JPN");
        origin.Source.ShouldBe(GeoSource.Manual);
        origin.IsVerified.ShouldBeTrue();
        origin.Confidence.ShouldBe(1m);

        var unknownIso = await client.PutAsJsonAsync($"/api/books/{item.Book.Id}/origin-countries", new { countryIsos = new[] { "XXX" } }, Json);
        unknownIso.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(unknownIso)).ShouldBe("country.unknown_iso");

        var authorId = item.Book.Authors.Single().Id;
        var author = await ReadAsync<AuthorDetailsDto>(
            await client.PutAsJsonAsync($"/api/authors/{authorId}/countries", new { citizenshipIsos = new[] { "JPN", "USA" } }, Json),
            HttpStatusCode.OK);
        author.Countries.Select(c => c.CountryIso).ShouldBe(["JPN", "USA"], ignoreOrder: true);
        author.Countries.ShouldAllBe(c => c.Source == GeoSource.Manual && c.Relationship == AuthorCountryRelationship.Citizenship);
        author.ExternalIds.ShouldContain(new ExternalSourceRef(ExternalProvider.Wikidata, "Q134798"));
        author.ExternalIds.ShouldNotContain(e => e.ExternalId == "108238901"); // VIAF is not exposed

        // Reprocessing keeps the manual data.
        var enriched = await ReadAsync<BookDetailsDto>(await client.PostAsync($"/api/books/{item.Book.Id}/enrich", null), HttpStatusCode.OK);
        enriched.OriginCountries.ShouldHaveSingleItem().Source.ShouldBe(GeoSource.Manual);
        enriched.Authors.Single().Nationalities.Select(n => n.CountryIso).ShouldBe(["JPN", "USA"], ignoreOrder: true);
    }

    [Fact]
    public async Task Manual_book_creation_and_validation()
    {
        var client = await UserClientAsync();

        var created = await client.PostAsJsonAsync("/api/books",
            new { title = "Livro Manual", isbn10 = "0-306-40615-2", authorNames = new[] { "Autora Inventada" }, originCountryIsos = new[] { "BRA" } }, Json);
        var book = await ReadAsync<BookDetailsDto>(created, HttpStatusCode.Created);
        book.Isbn13.ShouldBe("9780306406157");
        book.OriginCountries.ShouldHaveSingleItem().Source.ShouldBe(GeoSource.Manual);
        created.Headers.Location!.ToString().ShouldEndWith($"/api/books/{book.Id}");

        var duplicateIsbn = await client.PostAsJsonAsync("/api/books",
            new { title = "Outro", isbn13 = "9780306406157", authorNames = new[] { "X" } }, Json);
        duplicateIsbn.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(duplicateIsbn)).ShouldBe("book.duplicate_isbn");

        var noAuthors = await client.PostAsJsonAsync("/api/books", new { title = "Sem autor", authorNames = Array.Empty<string>() }, Json);
        noAuthors.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var both = await client.PostAsJsonAsync("/api/library",
            new { bookId = book.Id, source = new { provider = "OpenLibrary", externalId = "OL1003040W" }, status = "Read" }, Json);
        both.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(both)).ShouldBe("library.invalid_book_reference");

        var notInLibrary = await client.PutAsJsonAsync($"/api/books/{book.Id}", new { title = "Novo", authorNames = new[] { "A" } }, Json);
        notInLibrary.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_book_originCountryIsos_omitted_keeps_origins_and_array_replaces()
    {
        var client = await UserClientAsync();
        await PromoteAsync(client); // changing existing countries is a moderator action
        var book = await ReadAsync<BookDetailsDto>(
            await client.PostAsJsonAsync("/api/books",
                new { title = "Livro", authorNames = new[] { "Autor" }, originCountryIsos = new[] { "BRA" } }, Json),
            HttpStatusCode.Created);
        await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library", new { bookId = book.Id, status = "WantToRead" }, Json),
            HttpStatusCode.Created);

        // omitted -> unchanged
        var omitted = await ReadAsync<BookDetailsDto>(
            await client.PutAsJsonAsync($"/api/books/{book.Id}", new { title = "Livro 2", authorNames = new[] { "Autor" } }, Json),
            HttpStatusCode.OK);
        omitted.Title.ShouldBe("Livro 2");
        omitted.OriginCountries.ShouldHaveSingleItem().CountryIso.ShouldBe("BRA");

        // explicit null -> unchanged
        var nullValue = await ReadAsync<BookDetailsDto>(
            await client.PutAsJsonAsync($"/api/books/{book.Id}",
                new { title = "Livro 3", authorNames = new[] { "Autor" }, originCountryIsos = (string[]?)null }, Json),
            HttpStatusCode.OK);
        nullValue.OriginCountries.ShouldHaveSingleItem().CountryIso.ShouldBe("BRA");

        // array replaces
        var replaced = await ReadAsync<BookDetailsDto>(
            await client.PutAsJsonAsync($"/api/books/{book.Id}",
                new { title = "Livro 3", authorNames = new[] { "Autor" }, originCountryIsos = new[] { "PRT" } }, Json),
            HttpStatusCode.OK);
        replaced.OriginCountries.ShouldHaveSingleItem().CountryIso.ShouldBe("PRT");

        // empty array clears
        var cleared = await ReadAsync<BookDetailsDto>(
            await client.PutAsJsonAsync($"/api/books/{book.Id}",
                new { title = "Livro 3", authorNames = new[] { "Autor" }, originCountryIsos = Array.Empty<string>() }, Json),
            HttpStatusCode.OK);
        cleared.OriginCountries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_validation_and_library_query_validation()
    {
        var client = await UserClientAsync();

        var shortTerm = await client.GetAsync("/api/books/search?q=a");
        shortTerm.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(shortTerm)).ShouldBe("validation.failed");
        (await CodeAsync(await client.GetAsync("/api/books/search?q=123&type=isbn"))).ShouldBe("book.invalid_isbn13");
        (await client.GetAsync("/api/library?sort=popularity")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/library?mapType=moon")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/books/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Maps_follow_the_reading_status_and_expose_country_detail()
    {
        var client = await UserClientAsync();
        await PromoteAsync(client); // changing existing countries is a moderator action
        var casmurro = await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library",
                new { source = new { provider = "OpenLibrary", externalId = "OL1003040W" }, status = "Read", finishedDate = "2025-02-01" }, Json),
            HttpStatusCode.Created);
        var wood = await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library",
                new { source = new { provider = "OpenLibrary", externalId = "OL2625457W" }, status = "WantToRead" }, Json),
            HttpStatusCode.Created);

        // The catalog is shared by the tests of this class: pin the geography of Norwegian Wood explicitly.
        (await client.PutAsJsonAsync($"/api/books/{wood.Book.Id}/origin-countries", new { countryIsos = Array.Empty<string>() }, Json))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/authors/{wood.Book.Authors.Single().Id}/countries", new { citizenshipIsos = new[] { "JPN" } }, Json))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Only Dom Casmurro is Read so far.
        var authors = await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/authors"), HttpStatusCode.OK);
        authors.TotalReadBooks.ShouldBe(1);
        authors.Countries.ShouldHaveSingleItem().Iso.ShouldBe("BRA");

        // Marking Norwegian Wood as Read: JPN appears in the authors map; its origin is unknown.
        var read = await ReadAsync<LibraryItemDto>(
            await client.PutAsJsonAsync($"/api/library/{wood.Id}", new { status = "Read" }, Json), HttpStatusCode.OK);
        read.FinishedDate.ShouldNotBeNull();

        authors = await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/authors"), HttpStatusCode.OK);
        authors.MapType.ShouldBe(MapType.Authors);
        authors.TotalReadBooks.ShouldBe(2);
        authors.ExploredCountries.ShouldBe(2);
        authors.MaxBookCount.ShouldBe(1);
        authors.UnknownBookCount.ShouldBe(0);
        authors.Countries.Select(c => c.Iso).ShouldBe(["BRA", "JPN"], ignoreOrder: true);
        authors.Countries.Single(c => c.Iso == "JPN").Name.ShouldBe("Japão");

        var origins = await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/origins"), HttpStatusCode.OK);
        origins.TotalReadBooks.ShouldBe(2);
        origins.UnknownBookCount.ShouldBe(1); // Norwegian Wood has no known origin in the fakes
        origins.Countries.ShouldHaveSingleItem().Iso.ShouldBe("BRA");

        // Country detail
        var detail = await ReadAsync<CountryDetailDto>(await client.GetAsync("/api/maps/authors/countries/bra"), HttpStatusCode.OK);
        detail.Iso.ShouldBe("BRA");
        detail.Name.ShouldBe("Brasil");
        detail.BookCount.ShouldBe(1);
        detail.AuthorCount.ShouldBe(1);
        var book = detail.Books.ShouldHaveSingleItem();
        book.Title.ShouldBe("Dom Casmurro");
        book.LibraryItemId.ShouldBe(casmurro.Id);
        book.Authors.ShouldBe(["Machado de Assis"]);
        book.FinishedDate.ShouldBe(new DateOnly(2025, 2, 1));

        // Valid country without books -> 200 empty; unknown ISO -> 400; unknown map type -> 404.
        var empty = await ReadAsync<CountryDetailDto>(await client.GetAsync("/api/maps/origins/countries/JPN"), HttpStatusCode.OK);
        empty.BookCount.ShouldBe(0);
        empty.Books.ShouldBeEmpty();
        var unknown = await client.GetAsync("/api/maps/authors/countries/ZZZ");
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(unknown)).ShouldBe("country.unknown_iso");
        (await client.GetAsync("/api/maps/other/countries/BRA")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Back to Reading: the map is recalculated.
        await ReadAsync<LibraryItemDto>(
            await client.PutAsJsonAsync($"/api/library/{casmurro.Id}", new { status = "Reading" }, Json), HttpStatusCode.OK);
        authors = await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/authors"), HttpStatusCode.OK);
        authors.TotalReadBooks.ShouldBe(1);
        authors.Countries.ShouldHaveSingleItem().Iso.ShouldBe("JPN");
        origins = await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/origins"), HttpStatusCode.OK);
        origins.Countries.ShouldBeEmpty();
        origins.UnknownBookCount.ShouldBe(1);
        (await ReadAsync<CountryDetailDto>(await client.GetAsync("/api/maps/authors/countries/BRA"), HttpStatusCode.OK))
            .Books.ShouldBeEmpty();

        // Another user sees an empty map; anonymous gets 401.
        var other = await UserClientAsync();
        (await ReadAsync<MapDataDto>(await other.GetAsync("/api/maps/authors"), HttpStatusCode.OK)).Countries.ShouldBeEmpty();
        (await factory.CreateClient().GetAsync("/api/maps/authors")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Sharing needs a non-Private profile (403 share.private_profile); the link itself is still Etapa 6 (501).
        var blocked = await client.PostAsJsonAsync("/api/maps/share", new { mapType = "Authors" }, Json);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        JsonNode.Parse(await blocked.Content.ReadAsStringAsync())!["code"]!.GetValue<string>().ShouldBe("share.private_profile");
        (await client.PutAsJsonAsync("/api/auth/me", new { name = "Leitor", mapVisibility = "LinkOnly" }, Json)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/maps/share", new { mapType = "Authors" }, Json)).StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }
}
