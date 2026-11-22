using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

/// <summary>Library geo filter and map status filter. Own fixture: the catalog is shared inside a fixture.</summary>
[Trait("Category", "Integration")]
public sealed class LibraryGeoStatusFlowTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>
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

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).ShouldBe("validation.failed");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
    }

    private static async Task<string[]> TitlesAsync(HttpClient client, string query) =>
        (await ReadAsync<PagedResult<LibraryItemDto>>(await client.GetAsync($"/api/library?{query}"), HttpStatusCode.OK))
            .Items.Select(i => i.Book.Title).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Library_geoMissing_filter_combines_with_other_filters_and_validates()
    {
        var client = await UserClientAsync();
        await PromoteAsync(client);
        await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library",
                new { source = new { provider = "OpenLibrary", externalId = "OL1003040W" }, status = "Read", finishedDate = "2025-02-01" }, Json),
            HttpStatusCode.Created);
        var wood = await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library",
                new { source = new { provider = "OpenLibrary", externalId = "OL2625457W" }, status = "WantToRead" }, Json),
            HttpStatusCode.Created);
        var woodAuthor = wood.Book.Authors.Single().Id;

        // Pin Norwegian Wood: no origin, author with citizenship.
        (await client.PutAsJsonAsync($"/api/books/{wood.Book.Id}/origin-countries", new { countryIsos = Array.Empty<string>() }, Json))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/authors/{woodAuthor}/countries", new { citizenshipIsos = new[] { "JPN" } }, Json))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await TitlesAsync(client, "")).ShouldBe(["Dom Casmurro", "Norwegian Wood"]);
        (await TitlesAsync(client, "geoMissing=origin")).ShouldBe(["Norwegian Wood"]);
        (await TitlesAsync(client, "geoMissing=any")).ShouldBe(["Norwegian Wood"]);
        (await TitlesAsync(client, "geoMissing=author")).ShouldBeEmpty();
        (await TitlesAsync(client, "geoMissing=both")).ShouldBeEmpty();

        // Clearing the author's nationalities makes the book miss both; the filter is case-insensitive and ANDs with status.
        (await client.PutAsJsonAsync($"/api/authors/{woodAuthor}/countries", new { citizenshipIsos = Array.Empty<string>() }, Json))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TitlesAsync(client, "geoMissing=BOTH")).ShouldBe(["Norwegian Wood"]);
        (await TitlesAsync(client, "geoMissing=Author")).ShouldBe(["Norwegian Wood"]);
        (await TitlesAsync(client, "geoMissing=any&status=WantToRead")).ShouldBe(["Norwegian Wood"]);
        (await TitlesAsync(client, "geoMissing=any&status=Read")).ShouldBeEmpty();
        (await TitlesAsync(client, "geoMissing=both&q=casmurro")).ShouldBeEmpty();

        // Another user does not see it.
        var other = await UserClientAsync();
        (await TitlesAsync(other, "geoMissing=any")).ShouldBeEmpty();

        var errors = await ErrorsAsync(await client.GetAsync("/api/library?geoMissing=none"));
        errors.TryGetProperty("geoMissing", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Maps_can_be_viewed_by_status_while_default_stays_Read()
    {
        var client = await UserClientAsync();
        await PromoteAsync(client);
        await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library",
                new { source = new { provider = "OpenLibrary", externalId = "OL1003040W" }, status = "Read", finishedDate = "2025-02-01" }, Json),
            HttpStatusCode.Created);
        var wood = await ReadAsync<LibraryItemDto>(
            await client.PostAsJsonAsync("/api/library",
                new { source = new { provider = "OpenLibrary", externalId = "OL2625457W" }, status = "WantToRead" }, Json),
            HttpStatusCode.Created);
        (await client.PutAsJsonAsync($"/api/books/{wood.Book.Id}/origin-countries", new { countryIsos = Array.Empty<string>() }, Json))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PutAsJsonAsync($"/api/authors/{wood.Book.Authors.Single().Id}/countries", new { citizenshipIsos = new[] { "JPN" } }, Json))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Default (no parameter) is exactly the previous behavior: Read only.
        var byDefault = await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/authors"), HttpStatusCode.OK);
        byDefault.Status.ShouldBe("Read");
        byDefault.TotalReadBooks.ShouldBe(1);
        byDefault.Countries.ShouldHaveSingleItem().Iso.ShouldBe("BRA");
        (await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/authors?status=read"), HttpStatusCode.OK))
            .ShouldBeEquivalentTo(byDefault);

        var want = await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/authors?status=WantToRead"), HttpStatusCode.OK);
        want.Status.ShouldBe("WantToRead");
        want.TotalReadBooks.ShouldBe(1);
        want.Countries.ShouldHaveSingleItem().Iso.ShouldBe("JPN");

        var all = await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/authors?status=All"), HttpStatusCode.OK);
        all.Status.ShouldBe("All");
        all.TotalReadBooks.ShouldBe(2);
        all.ExploredCountries.ShouldBe(2);

        var originsAll = await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/origins?status=all"), HttpStatusCode.OK);
        originsAll.Countries.ShouldHaveSingleItem().Iso.ShouldBe("BRA");
        originsAll.UnknownBookCount.ShouldBe(1);
        (await ReadAsync<MapDataDto>(await client.GetAsync("/api/maps/origins?status=Reading"), HttpStatusCode.OK))
            .TotalReadBooks.ShouldBe(0);

        // Country detail honours the status; the default is still Read.
        (await ReadAsync<CountryDetailDto>(await client.GetAsync("/api/maps/authors/countries/JPN"), HttpStatusCode.OK))
            .Books.ShouldBeEmpty();
        var jpn = await ReadAsync<CountryDetailDto>(await client.GetAsync("/api/maps/authors/countries/jpn?status=WantToRead"), HttpStatusCode.OK);
        jpn.Status.ShouldBe("WantToRead");
        jpn.Books.ShouldHaveSingleItem().Title.ShouldBe("Norwegian Wood");
        (await ReadAsync<CountryDetailDto>(await client.GetAsync("/api/maps/authors/countries/JPN"), HttpStatusCode.OK))
            .Status.ShouldBe("Read");

        // Invalid values are 400 on the status field (maps and detail).
        foreach (var url in new[] { "/api/maps/authors?status=Done", "/api/maps/origins?status=9", "/api/maps/authors/countries/BRA?status=x" })
        {
            (await ErrorsAsync(await client.GetAsync(url))).TryGetProperty("status", out _).ShouldBeTrue(url);
        }

        // Statistics ignore the maps' status filter (always Read).
        (await client.GetAsync("/api/statistics/dashboard?status=All")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
