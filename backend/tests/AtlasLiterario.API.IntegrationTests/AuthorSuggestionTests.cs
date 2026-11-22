using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AtlasLiterario.Application.Auth;
using AtlasLiterario.Application.Authors;
using AtlasLiterario.Application.Books;
using AtlasLiterario.Application.Maps;

namespace AtlasLiterario.API.IntegrationTests;

/// <summary>v1.10: GET /authors?q=&amp;limit=, CreateBookRequest.authorIds and CountryDetailDto.authors over HTTP.</summary>
[Trait("Category", "Integration")]
public sealed class AuthorSuggestionTests(AtlasApiFactory factory) : IClassFixture<AtlasApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private async Task<HttpClient> LoggedInAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { name = "Leitora", email = $"s-{Guid.NewGuid():N}@example.com", password = "segredo123" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    [Fact]
    public async Task Search_requires_authentication()
    {
        (await factory.CreateClient().GetAsync("/api/authors?q=ab")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/authors", "q")]
    [InlineData("/api/authors?q=a", "q")]
    [InlineData("/api/authors?q=ab&limit=0", "limit")]
    [InlineData("/api/authors?q=ab&limit=21", "limit")]
    public async Task Invalid_parameters_return_400_validation_failed_with_the_field(string url, string field)
    {
        var client = await LoggedInAsync();

        var response = await client.GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await BodyAsync(response);
        body["code"]!.GetValue<string>().ShouldBe("validation.failed");
        body["errors"]![field].ShouldNotBeNull();
    }

    [Fact]
    public async Task Created_authors_are_suggested_and_can_be_reused_by_id_and_listed_in_the_country_panel()
    {
        var client = await LoggedInAsync();
        var unique = $"Zzyzx{Guid.NewGuid():N}"[..12];
        var created = await client.PostAsJsonAsync("/api/books", new { title = "Livro A", authorNames = new[] { $"{unique} Silva" } }, Json);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var suggestions = (await client.GetFromJsonAsync<List<AuthorSuggestionDto>>($"/api/authors?q={unique.ToLowerInvariant()}&limit=5", Json))!;
        var author = suggestions.ShouldHaveSingleItem();
        author.NationalityIsos.ShouldBeEmpty();

        var reuse = await client.PostAsJsonAsync("/api/books",
            new { title = "Livro B", authorNames = new[] { "outro nome" }, authorIds = new Guid?[] { author.Id } }, Json);
        reuse.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await reuse.Content.ReadFromJsonAsync<BookDetailsDto>(Json))!.Authors.ShouldHaveSingleItem().Id.ShouldBe(author.Id);

        var mismatched = await client.PostAsJsonAsync("/api/books",
            new { title = "Livro C", authorNames = new[] { "a", "b" }, authorIds = new Guid?[] { author.Id } }, Json);
        mismatched.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await BodyAsync(mismatched))["errors"]!["authorIds"].ShouldNotBeNull();

        var panel = await client.GetFromJsonAsync<CountryDetailDto>("/api/maps/authors/countries/BRA", Json);
        panel!.Authors.ShouldBeEmpty();
        var origins = await client.GetFromJsonAsync<JsonNode>("/api/maps/origins/countries/BRA", Json);
        origins!["authors"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenApi_document_exposes_the_v1_10_schemas()
    {
        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        document["paths"]!["/api/authors"]!["get"].ShouldNotBeNull();
        var schemas = document["components"]!["schemas"]!;
        schemas["AuthorSuggestionDto"]!["properties"]!.AsObject().Select(p => p.Key)
            .ShouldBe(["id", "name", "nationalityIsos", "nationalityNames"], ignoreOrder: true);
        schemas["CountryAuthorDto"]!["properties"]!.AsObject().Select(p => p.Key)
            .ShouldBe(["authorId", "name", "countryIso", "countryName", "books"], ignoreOrder: true);
        schemas["CountryAuthorDto"]!["properties"]!["books"].ShouldNotBeNull();
        document["paths"]!["/api/authors/duplicates"]!["get"].ShouldNotBeNull();
        document["paths"]!["/api/authors/merge"]!["post"].ShouldNotBeNull();
        schemas["AuthorMergeResultDto"]!["properties"]!.AsObject().Select(p => p.Key)
            .ShouldBe(["targetId", "mergedAuthors", "booksRelinked", "booksAlreadyLinked", "countriesAdded"], ignoreOrder: true);
        schemas["MergeAuthorsRequest"]!["properties"]!.AsObject().Select(p => p.Key)
            .ShouldBe(["targetId", "sourceIds"], ignoreOrder: true);
        schemas["CountryDetailDto"]!["properties"]!["authors"].ShouldNotBeNull();
        schemas["CreateBookRequest"]!["properties"]!["authorIds"].ShouldNotBeNull();
    }
}
