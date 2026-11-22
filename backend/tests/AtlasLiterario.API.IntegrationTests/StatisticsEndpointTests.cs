using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Statistics;
using AtlasLiterario.Domain.Entities;
using AtlasLiterario.Domain.Enums;
using AtlasLiterario.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtlasLiterario.API.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class StatisticsEndpointTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private async Task<(HttpClient Client, Guid UserId)> UserAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { name = "Leitora", email = $"s-{Guid.NewGuid():N}@example.com", password = "segredo123" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var me = await client.GetFromJsonAsync<UserProfileDto>("/api/auth/me", Json);
        return (client, me!.Id);
    }

    private async Task ShelveAsync(Guid userId, string title, string authorIso, string originIso, ReadingStatus status, DateOnly? finished)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var countries = await db.Countries.ToDictionaryAsync(c => c.IsoCode, c => c.Id);
        var author = new Author($"Autor {Guid.NewGuid():N}", now);
        var book = new Book(title, now);
        db.Authors.Add(author);
        db.Books.Add(book);
        db.BookAuthors.Add(new BookAuthor(book.Id, author.Id, 0));
        db.AuthorCountries.Add(new AuthorCountry(author.Id, countries[authorIso], AuthorCountryRelationship.Citizenship, GeoSource.Wikidata, 0.9m, false, null, now));
        db.BookOriginCountries.Add(new BookOriginCountry(book.Id, countries[originIso], GeoSource.Wikidata, 0.9m, false, null, now));
        db.UserBooks.Add(new UserBook(userId, book.Id, status, null, null, null, finished, now));
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("/api/statistics/dashboard")]
    [InlineData("/api/statistics/passports")]
    public async Task Statistics_require_a_token(string url) =>
        (await factory.CreateClient().GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Dashboard_and_passports_return_the_contract_shape_for_the_logged_user_only_read_books()
    {
        var (client, userId) = await UserAsync();
        var (otherClient, otherId) = await UserAsync();
        await ShelveAsync(userId, "Dom Casmurro", "BRA", "BRA", ReadingStatus.Read, new DateOnly(2025, 1, 10));
        await ShelveAsync(userId, "Na estante", "ARG", "ARG", ReadingStatus.WantToRead, null);
        await ShelveAsync(otherId, "Alheio", "JPN", "JPN", ReadingStatus.Read, new DateOnly(2025, 2, 1));

        var dashboardBody = await client.GetStringAsync("/api/statistics/dashboard?status=All");
        var dashboard = JsonSerializer.Deserialize<DashboardDto>(dashboardBody, Json)!;
        dashboard.TotalReadBooks.ShouldBe(1);
        dashboard.WantToRead.ShouldBe(1);
        dashboard.DistinctCountries.ShouldBe(1);
        dashboard.TopCountries.Select(t => (t.Iso, t.MapType)).ShouldBe([("BRA", MapType.Authors), ("BRA", MapType.Origins)], ignoreOrder: true);
        dashboard.PassportProgress.TotalCountries.ShouldBe(250);
        var json = JsonNode.Parse(dashboardBody)!;
        foreach (var key in new[] { "totalReadBooks", "totalAuthors", "countriesByAuthors", "countriesByOrigins", "distinctCountries",
                     "continentsExplored", "readingNow", "wantToRead", "recentlyFinished", "topCountries", "passportProgress" })
        {
            json[key].ShouldNotBeNull(key);
        }

        json["topCountries"]![0]!["mapType"]!.GetValue<string>().ShouldBe("Authors");

        var passportsBody = await client.GetStringAsync("/api/statistics/passports");
        var passports = JsonSerializer.Deserialize<PassportsDto>(passportsBody, Json)!;
        passports.Authors.TopCountry!.Iso.ShouldBe("BRA");
        passports.DistinctCountries.ShouldBe(1);
        passports.Continents.Count.ShouldBe(6);
        passports.Timeline.First().Month.ShouldBe("2025-01");
        passports.Timeline.Last().CumulativeBooks.ShouldBe(1);
        JsonNode.Parse(passportsBody)!["progress"]!["percentage"]!.GetValue<double>().ShouldBe(0.4);

        var theirs = JsonSerializer.Deserialize<DashboardDto>(await otherClient.GetStringAsync("/api/statistics/dashboard"), Json)!;
        theirs.TopCountries.ShouldAllBe(t => t.Iso == "JPN");
    }

    [Fact]
    public async Task OpenApi_documents_both_statistics_endpoints_without_501()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        var paths = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["paths"]!;

        foreach (var path in new[] { "/api/statistics/dashboard", "/api/statistics/passports" })
        {
            var responses = paths[path]!["get"]!["responses"]!.AsObject();
            responses.ContainsKey("200").ShouldBeTrue(path);
            responses.ContainsKey("501").ShouldBeFalse(path);
        }
    }
}
